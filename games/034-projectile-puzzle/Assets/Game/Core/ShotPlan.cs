using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Game.Core
{
    /// <summary>解答の 1 発：引き（m）と、撃ってから起爆するまでの Tick（起爆しなければ -1）。</summary>
    public readonly struct ShotStep
    {
        public ShotStep(V2 pull, int detonateAfter)
        {
            Pull = pull;
            DetonateAfter = detonateAfter;
        }

        public V2 Pull { get; }

        public int DetonateAfter { get; }
    }

    /// <summary>
    /// 解答の手順（<c>Assets/Game/Data/Solutions.txt</c>）。撮影とテストで再生する（ゲーム本体の画面には出さない）。書式：
    /// <code>
    /// stage
    /// shot 引きx 引きy [起爆までの Tick]
    /// end
    /// </code>
    /// 面の順に並べる。<c>#</c> から行末と空行は無視する。
    /// </summary>
    public static class ShotPlan
    {
        private static readonly char[] _separators = { ' ', '\t' };

        public static ReadOnlyCollection<ReadOnlyCollection<ShotStep>> Parse(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var plans = new List<ReadOnlyCollection<ShotStep>>();
            List<ShotStep>? draft = null;
            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var comment = line.IndexOf('#');
                if (comment >= 0) { line = line.Substring(0, comment); }
                var words = line.Split(_separators, StringSplitOptions.RemoveEmptyEntries);
                if (words.Length == 0) { continue; }
                try
                {
                    switch (words[0])
                    {
                        case "stage":
                            if (draft != null) { throw new FormatException("前の面が end で閉じていません。"); }
                            draft = new List<ShotStep>();
                            break;
                        case "shot":
                            if (draft == null) { throw new FormatException("stage の外に shot があります。"); }
                            if (words.Length != 3 && words.Length != 4) { throw new FormatException("shot は 引きx 引きy [起爆までの Tick] です。"); }
                            var detonate = words.Length == 4 ? Integer(words[3]) : -1;
                            draft.Add(new ShotStep(new V2(Number(words[1]), Number(words[2])), detonate));
                            break;
                        case "end":
                            if (draft == null) { throw new FormatException("stage の外に end があります。"); }
                            plans.Add(draft.AsReadOnly());
                            draft = null;
                            break;
                        default:
                            throw new FormatException($"未知の項目「{words[0]}」です。");
                    }
                }
                catch (FormatException error)
                {
                    throw new FormatException($"{i + 1} 行目：{error.Message}", error);
                }
            }

            if (draft != null) { throw new FormatException("最後の面が end で閉じていません。"); }
            return plans.AsReadOnly();
        }

        private static float Number(string word)
        {
            if (!float.TryParse(word, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
            {
                throw new FormatException($"「{word}」は数ではありません。");
            }

            return value;
        }

        private static int Integer(string word)
        {
            if (!int.TryParse(word, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) { throw new FormatException($"「{word}」は 0 以上の整数ではありません。"); }
            return value;
        }
    }
}
