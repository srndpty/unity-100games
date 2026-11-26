using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Game.Core.Runtime
{
    /// <summary>選択の列 1 本とその結末。</summary>
    public sealed class StoryPath
    {
        public StoryPath(IReadOnlyList<int> choices, int ending, uint finalHash, int textCount)
        {
            Choices = choices;
            Ending = ending;
            FinalHash = finalHash;
            TextCount = textCount;
        }

        /// <summary>選んだ選択肢の番号（出てきた順）。</summary>
        public IReadOnlyList<int> Choices { get; }

        /// <summary>着いた結末の番号。</summary>
        public int Ending { get; }

        /// <summary>結末に着いたときの実行器のハッシュ。</summary>
        public uint FinalHash { get; }

        /// <summary>通った文の数（同じ文を 2 回通れば 2）。</summary>
        public int TextCount { get; }

        public string ChoicesText => Choices.Count == 0 ? "-" : string.Join(",", Choices);
    }

    /// <summary>網羅探索の結果。<see cref="Error"/> が空でなければ探索は途中で止まった。</summary>
    public sealed class ExploreResult
    {
        public ExploreResult(IReadOnlyList<StoryPath> paths, string error, int errorLine)
        {
            Paths = paths;
            Error = error;
            ErrorLine = errorLine;
        }

        public IReadOnlyList<StoryPath> Paths { get; }
        public string Error { get; }
        public int ErrorLine { get; }
        public bool Ok => Error.Length == 0;
    }

    /// <summary>
    /// 実行器を複製して全ての選択の列をたどる。検証（どの経路も結末で終わる・全結末に届く）と、テスト・撮影の選択の列の作成に使う。
    /// 1 本の経路の中で同じ（番地・変数）の待ちに戻ったら終わらない経路として止める。
    /// </summary>
    public static class PathExplorer
    {
        public static ExploreResult Explore(CompiledScript script, int maxPaths)
        {
            if (script == null) { throw new ArgumentNullException(nameof(script)); }
            var paths = new List<StoryPath>();
            ScriptRunner start;
            try
            {
                start = new ScriptRunner(script);
            }
            catch (FlowLimitExceededException e)
            {
                return new ExploreResult(paths.AsReadOnly(), e.Message, e.Line);
            }

            var stack = new Stack<Frame>();
            stack.Push(new Frame(start, Array.Empty<int>(), new HashSet<string>(StringComparer.Ordinal), 0));
            while (stack.Count > 0)
            {
                var f = stack.Pop();
                var r = f.Runner;
                var seen = f.Seen;
                var texts = f.Texts;
                try
                {
                    while (r.Phase == RunnerPhase.Text)
                    {
                        if (!seen.Add(Key(r))) { return Loop(paths, r); }
                        texts++;
                        r.Advance();
                    }
                }
                catch (FlowLimitExceededException e)
                {
                    return new ExploreResult(paths.AsReadOnly(), e.Message, e.Line);
                }

                if (r.Phase == RunnerPhase.Ended)
                {
                    if (paths.Count >= maxPaths) { return new ExploreResult(paths.AsReadOnly(), $"経路が {maxPaths} 本を超えました", r.Current.Line); }
                    paths.Add(new StoryPath(Array.AsReadOnly(f.Choices), r.EndingIndex, r.Hash(), texts));
                    continue;
                }

                if (!seen.Add(Key(r))) { return Loop(paths, r); }

                // 番号の小さい選択肢から順に出てくるよう、逆順に積む。
                for (int i = r.Current.Options.Count - 1; i >= 0; i--)
                {
                    var next = r.Clone();
                    try
                    {
                        next.Choose(i);
                    }
                    catch (FlowLimitExceededException e)
                    {
                        return new ExploreResult(paths.AsReadOnly(), e.Message, e.Line);
                    }

                    var choices = new int[f.Choices.Length + 1];
                    Array.Copy(f.Choices, choices, f.Choices.Length);
                    choices[choices.Length - 1] = i;
                    stack.Push(new Frame(next, choices, new HashSet<string>(seen, StringComparer.Ordinal), texts));
                }
            }

            return new ExploreResult(new ReadOnlyCollection<StoryPath>(paths), "", 0);
        }

        private static ExploreResult Loop(List<StoryPath> paths, ScriptRunner r) =>
            new(paths.AsReadOnly(), $"行 {r.Current.Line}：同じ状態に戻る経路があり、結末に着きません", r.Current.Line);

        private static string Key(ScriptRunner r)
        {
            var sb = new StringBuilder();
            sb.Append(r.Pc.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < r.Script.Variables.Count; i++)
            {
                sb.Append(',').Append(r.Value(i).ToString(CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        private sealed class Frame
        {
            public Frame(ScriptRunner runner, int[] choices, HashSet<string> seen, int texts)
            {
                Runner = runner;
                Choices = choices;
                Seen = seen;
                Texts = texts;
            }

            public ScriptRunner Runner { get; }
            public int[] Choices { get; }
            public HashSet<string> Seen { get; }
            public int Texts { get; }
        }
    }
}
