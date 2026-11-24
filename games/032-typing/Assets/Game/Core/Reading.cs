using System;
using System.Collections.Generic;
using System.Text;

namespace Game.Core
{
    /// <summary>読みの区切り：読みの位置から <see cref="Length"/> かなぶんを、<see cref="Spelling"/> で打つ。</summary>
    public readonly struct Chunk : IEquatable<Chunk>
    {
        public Chunk(int length, string spelling)
        {
            Length = length;
            Spelling = spelling;
        }

        /// <summary>この区切りが覆うかなの数。</summary>
        public int Length { get; }

        public string Spelling { get; }

        public bool Equals(Chunk other) => Length == other.Length && string.Equals(Spelling, other.Spelling, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is Chunk other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Length, StringComparer.Ordinal.GetHashCode(Spelling));

        public static bool operator ==(Chunk left, Chunk right) => left.Equals(right);

        public static bool operator !=(Chunk left, Chunk right) => !left.Equals(right);

        public override string ToString() => $"{Length}:{Spelling}";
    }

    /// <summary>
    /// ひらがなの読みと、読みの各位置から打てる区切り（綴りの候補）の一覧。照合器（<see cref="RomajiMatcher"/>）が使う。
    /// <para>位置 i の区切りの並び（先頭ほど案内に使う）：</para>
    /// <list type="bullet">
    /// <item>「ん」：単独の <c>n</c>＋次の区切り（次の綴りが母音・<c>y</c>・<c>n</c> で始まらないとき）、<c>nn</c>、<c>xn</c>。</item>
    /// <item>「っ」：次の区切りの先頭の子音を重ねたもの（母音・<c>n</c>・<c>-</c> 始まりには重ねない）、<c>xtu</c> などの単独の綴り。</item>
    /// <item>それ以外：2 かなの単位（拗音など）の綴り、1 かなの単位の綴り。</item>
    /// </list>
    /// </summary>
    public sealed class Reading
    {
        private readonly Chunk[][] _chunks;
        private readonly string[] _defaults;

        private Reading(string kana)
        {
            Kana = kana;
            _chunks = new Chunk[kana.Length][];
            for (var i = kana.Length - 1; i >= 0; i--) { _chunks[i] = Build(i); }

            _defaults = new string[kana.Length + 1];
            _defaults[kana.Length] = string.Empty;
            for (var i = kana.Length - 1; i >= 0; i--)
            {
                var first = _chunks[i][0];
                _defaults[i] = first.Spelling + _defaults[i + first.Length];
            }

            var keys = new StringBuilder();
            foreach (var chunk in _chunks[0])
            {
                if (!keys.ToString().Contains(chunk.Spelling[0])) { keys.Append(chunk.Spelling[0]); }
            }

            FirstKeys = keys.ToString();
        }

        /// <summary>ひらがなの読み。</summary>
        public string Kana { get; }

        /// <summary>最初の 1 打として受け付けるキー。</summary>
        public string FirstKeys { get; }

        /// <summary>全体を既定の綴りで打つ文字列。</summary>
        public string DefaultSpelling => _defaults[0];

        /// <summary>読みを区切る。表にないかな・空・末尾の「っ」は <see cref="FormatException"/>。</summary>
        public static Reading Parse(string kana)
        {
            if (!TryParse(kana, out var reading, out var error)) { throw new FormatException(error); }
            return reading!;
        }

        public static bool TryParse(string kana, out Reading? reading, out string error)
        {
            reading = null;
            if (string.IsNullOrEmpty(kana))
            {
                error = "読みが空です。";
                return false;
            }

            foreach (var c in kana)
            {
                if (!RomajiTable.IsUnit(c.ToString()))
                {
                    error = $"読み「{kana}」に打てないかな「{c}」があります。";
                    return false;
                }
            }

            if (kana[kana.Length - 1] == 'っ')
            {
                error = $"読み「{kana}」が「っ」で終わっています。";
                return false;
            }

            reading = new Reading(kana);
            error = string.Empty;
            return true;
        }

        /// <summary>読みの位置 <paramref name="position"/> から打てる区切り（先頭ほど案内に使う）。</summary>
        public IReadOnlyList<Chunk> ChunksAt(int position) => Array.AsReadOnly(_chunks[position]);

        internal Chunk ChunkAt(int position, int index) => _chunks[position][index];

        internal int ChunkCount(int position) => _chunks[position].Length;

        /// <summary>位置 <paramref name="position"/> から終わりまでを既定の綴りで打つ文字列。</summary>
        public string DefaultFrom(int position) => _defaults[position];

        public override string ToString() => Kana;

        private Chunk[] Build(int i)
        {
            var list = new List<Chunk>();

            void Add(Chunk chunk)
            {
                if (!list.Contains(chunk)) { list.Add(chunk); }
            }

            var c = Kana[i];
            var hasNext = i + 1 < Kana.Length;
            if (c == 'ん' && hasNext)
            {
                foreach (var next in _chunks[i + 1])
                {
                    if (!"aiueoyn".Contains(next.Spelling[0])) { Add(new Chunk(1 + next.Length, "n" + next.Spelling)); }
                }
            }

            if (c == 'っ' && hasNext)
            {
                foreach (var next in _chunks[i + 1])
                {
                    var head = next.Spelling[0];
                    if (head >= 'a' && head <= 'z' && !"aiueon".Contains(head)) { Add(new Chunk(1 + next.Length, head + next.Spelling)); }
                }
            }

            if (hasNext)
            {
                foreach (var spelling in RomajiTable.SpellingsOf(Kana.Substring(i, 2))) { Add(new Chunk(2, spelling)); }
            }

            foreach (var spelling in RomajiTable.SpellingsOf(c.ToString())) { Add(new Chunk(1, spelling)); }
            return list.ToArray();
        }
    }
}
