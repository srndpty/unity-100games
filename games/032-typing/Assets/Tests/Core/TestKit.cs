using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Game.Core.Tests
{
    /// <summary>テストの共通の部品。</summary>
    internal static class TestKit
    {
        /// <summary>本番の単語帳（Assets/Game/Data/Words.txt）の文。.NET（テストの出力先）と Unity（プロジェクトの直下）の両方から探す。</summary>
        public static string WordsText()
        {
            foreach (var start in new[] { NUnit.Framework.TestContext.CurrentContext.TestDirectory, Environment.CurrentDirectory })
            {
                for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                {
                    var candidate = Path.Combine(directory.FullName, "Assets", "Game", "Data", "Words.txt");
                    if (File.Exists(candidate)) { return File.ReadAllText(candidate, Encoding.UTF8); }
                }
            }

            throw new FileNotFoundException("Assets/Game/Data/Words.txt が見つかりません。");
        }

        public static WordBook Production() => WordBook.Production(WordsText());

        /// <summary>読みをそのまま表示文にした段 1 の単語帳。</summary>
        public static WordBook Book(params string[] readings) =>
            WordBook.Parse(string.Join("\n", readings.Select(r => $"{r}\t{r}\t1")));

        /// <summary>自動では出現しない設定（敵は <see cref="TypingRun.Place"/> で置く）。</summary>
        public static VigilConfig Quiet(int hp = 3, int goal = 99, int approach = 600)
        {
            var config = new VigilConfig { Hp = hp };
            foreach (var level in new[] { config.Easy, config.Normal, config.Hard })
            {
                level.MinTier = 1;
                level.MaxTier = 1;
                level.MaxEnemies = 1;
                level.Goal = goal;
                level.ApproachTicks = approach;
                level.FirstSpawnTicks = 3600;
                level.SpawnMinTicks = 3600;
                level.SpawnMaxTicks = 3600;
            }

            return config;
        }

        /// <summary>敵を置くだけの試合（自動の出現なし）。</summary>
        public static TypingRun QuietRun(WordBook book, int hp = 3, int goal = 99, int approach = 600) =>
            new(Quiet(hp, goal, approach), book, Difficulty.Easy, 1);

        public static WordEntry Word(WordBook book, string reading) => book.Entries.First(e => e.Reading.Kana == reading);

        /// <summary>文字列を 1 文字ずつ打ち、最後の結果を返す。</summary>
        public static TypeOutcome TypeAll(TypingRun run, string keys)
        {
            var outcome = TypeOutcome.Ignored;
            foreach (var key in keys) { outcome = run.Type(key); }
            return outcome;
        }

        /// <summary>文字列を 1 文字ずつ照合し、最後の結果を返す。途中で誤字なら例外。</summary>
        public static KeyResult TypeAll(RomajiMatcher matcher, string keys)
        {
            var result = KeyResult.Miss;
            for (var i = 0; i < keys.Length; i++)
            {
                result = matcher.Type(keys[i]);
                if (result == KeyResult.Miss) { throw new InvalidOperationException($"「{matcher.Reading.Kana}」を「{keys}」と打つと {i} 文字目が誤字になりました。"); }
            }

            return result;
        }

        /// <summary>
        /// 素朴な参照実装：読みを表の単位（1 かな・2 かな）へ区切る全ての方法と、各単位の全ての綴りを総当たりで並べ、
        /// 「ん」の単独の n と「っ」の子音の重ねの条件を、並べた後の綴りで確かめて残す。照合器とは別の組み立て方で書く。
        /// </summary>
        public static HashSet<string> ReferenceSpellings(string kana)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            var tokens = new List<(string Unit, string Spelling)>();

            void Segment(int i)
            {
                if (i == kana.Length)
                {
                    var built = Resolve(tokens);
                    if (built != null) { result.Add(built); }
                    return;
                }

                for (var length = 1; length <= 2 && i + length <= kana.Length; length++)
                {
                    var unit = kana.Substring(i, length);
                    var spellings = RomajiTable.SpellingsOf(unit).ToList();
                    if (spellings.Count == 0) { continue; }
                    if (unit == "ん") { spellings.Add("n"); }
                    if (unit == "っ") { spellings.Add("*"); }
                    foreach (var spelling in spellings)
                    {
                        tokens.Add((unit, spelling));
                        Segment(i + length);
                        tokens.RemoveAt(tokens.Count - 1);
                    }
                }
            }

            Segment(0);
            return result;
        }

        private static string? Resolve(List<(string Unit, string Spelling)> tokens)
        {
            var resolved = new string[tokens.Count];
            for (var k = tokens.Count - 1; k >= 0; k--)
            {
                var spelling = tokens[k].Spelling;
                var next = k + 1 < tokens.Count ? resolved[k + 1] : null;
                if (spelling == "n" && tokens[k].Unit == "ん")
                {
                    if (next == null || "aiueoyn".Contains(next[0])) { return null; }
                }
                else if (spelling == "*")
                {
                    if (next == null || next[0] < 'a' || next[0] > 'z' || "aiueon".Contains(next[0])) { return null; }
                    spelling = next[0].ToString();
                }

                resolved[k] = spelling;
            }

            return string.Concat(resolved);
        }

        /// <summary>
        /// 照合器が受け付ける打ち方を全て数える：打った文字列ごとに全てのキーを試す。
        /// 返すのは（打ち終えた文字列の集合、途中で受け付けた文字列の集合）。行き止まり（どのキーも受け付けない途中）があれば例外。
        /// </summary>
        public static (HashSet<string> Complete, HashSet<string> Prefixes) MatcherLanguage(Reading reading)
        {
            var complete = new HashSet<string>(StringComparer.Ordinal);
            var prefixes = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<RomajiMatcher>();
            queue.Enqueue(new RomajiMatcher(reading));
            while (queue.Count > 0)
            {
                var matcher = queue.Dequeue();
                prefixes.Add(matcher.Typed);
                var any = false;
                foreach (var key in RomajiTable.Keys)
                {
                    var accepts = matcher.Accepts(key);
                    var next = matcher.Clone();
                    var result = next.Type(key);
                    if (accepts != (result != KeyResult.Miss)) { throw new InvalidOperationException($"Accepts と Type が食い違います：{matcher.Typed}+{key}"); }
                    if (result == KeyResult.Miss) { continue; }
                    any = true;
                    if (result == KeyResult.Completed) { complete.Add(next.Typed); }
                    else { queue.Enqueue(next); }
                }

                if (!any) { throw new InvalidOperationException($"「{reading.Kana}」を「{matcher.Typed}」まで打つと続けられません。"); }
            }

            return (complete, prefixes);
        }

        public static HashSet<string> PrefixesOf(IEnumerable<string> words)
        {
            var prefixes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var word in words)
            {
                for (var length = 0; length < word.Length; length++) { prefixes.Add(word.Substring(0, length)); }
            }

            return prefixes;
        }
    }
}
