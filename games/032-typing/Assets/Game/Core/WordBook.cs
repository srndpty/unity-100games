using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Core
{
    /// <summary>出題する語：画面に出す表示文と、照合に使う読みを分けて持つ。</summary>
    public sealed class WordEntry
    {
        public WordEntry(int index, string display, Reading reading, int tier)
        {
            Index = index;
            Display = display;
            Reading = reading;
            Tier = tier;
        }

        /// <summary>単語帳の中の番号（0 始まり）。</summary>
        public int Index { get; }

        /// <summary>画面に出す文（漢字かな交じり）。照合には使わない。</summary>
        public string Display { get; }

        /// <summary>照合に使うひらがなの読み。</summary>
        public Reading Reading { get; }

        /// <summary>段（1〜<see cref="WordBook.MaxTier"/>）。長いほど高い。</summary>
        public int Tier { get; }

        public override string ToString() => $"{Display}（{Reading.Kana}）";
    }

    /// <summary>
    /// 単語帳。1 行に「表示文 TAB 読み TAB 段」。空行と <c>#</c> で始まる行は読み飛ばす。
    /// 表示文・読みの重複、表にないかな、範囲外の段は拒否する。
    /// </summary>
    public sealed class WordBook
    {
        /// <summary>本番の単語帳の語数。</summary>
        public const int ProductionCount = 50;

        public const int MaxTier = 3;

        private readonly WordEntry[] _entries;

        private WordBook(WordEntry[] entries) => _entries = entries;

        public IReadOnlyList<WordEntry> Entries => Array.AsReadOnly(_entries);

        public int Count => _entries.Length;

        public WordEntry this[int index] => _entries[index];

        /// <summary>本番の単語帳として読む：<see cref="Parse"/> の検査に加え、ちょうど 50 語で、全ての段に語がある。</summary>
        public static WordBook Production(string text)
        {
            var book = Parse(text);
            if (book.Count != ProductionCount) { throw new FormatException($"単語帳は {ProductionCount} 語が必要です（{book.Count} 語）。"); }
            for (var tier = 1; tier <= MaxTier; tier++)
            {
                if (book.CountInTiers(tier, tier) == 0) { throw new FormatException($"段 {tier} の語がありません。"); }
            }

            return book;
        }

        /// <summary>単語帳の文を読む。不正な行は行番号付きの <see cref="FormatException"/>。</summary>
        public static WordBook Parse(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var entries = new List<WordEntry>();
            var displays = new HashSet<string>(StringComparer.Ordinal);
            var readings = new HashSet<string>(StringComparer.Ordinal);
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            for (var n = 0; n < lines.Length; n++)
            {
                var line = lines[n];
                if (line.Trim().Length == 0 || line.StartsWith('#')) { continue; }
                var fields = line.Split('\t');
                if (fields.Length != 3) { throw Error(n, "「表示文 TAB 読み TAB 段」の 3 列ではありません。"); }
                var display = fields[0].Trim();
                var kana = fields[1].Trim();
                if (display.Length == 0) { throw Error(n, "表示文が空です。"); }
                if (!int.TryParse(fields[2].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var tier) || tier < 1 || tier > MaxTier)
                {
                    throw Error(n, $"段は 1〜{MaxTier} の整数です。");
                }

                if (!Reading.TryParse(kana, out var reading, out var error)) { throw Error(n, error); }
                if (!displays.Add(display)) { throw Error(n, $"表示文「{display}」が重複しています。"); }
                if (!readings.Add(kana)) { throw Error(n, $"読み「{kana}」が重複しています。"); }
                entries.Add(new WordEntry(entries.Count, display, reading!, tier));
            }

            if (entries.Count == 0) { throw new FormatException("単語帳に語がありません。"); }
            return new WordBook(entries.ToArray());
        }

        /// <summary>段が <paramref name="minTier"/>〜<paramref name="maxTier"/> の語の数。</summary>
        public int CountInTiers(int minTier, int maxTier)
        {
            var count = 0;
            foreach (var entry in _entries)
            {
                if (entry.Tier >= minTier && entry.Tier <= maxTier) { count++; }
            }

            return count;
        }

        private static FormatException Error(int line, string message) => new($"{line + 1} 行目：{message}");
    }
}
