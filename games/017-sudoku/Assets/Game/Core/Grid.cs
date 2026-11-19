using System;
using System.Text;

namespace Game.Core
{
    /// <summary>
    /// 81 マスの数字（0 は空き）。不変で、書き換えは新しい盤を返す。
    /// 数字の重複は許す（矛盾した盤も表せる）。重複の有無は <see cref="HasConflict"/> で調べる。
    /// </summary>
    public sealed class Grid : IEquatable<Grid>
    {
        private readonly byte[] _cells;

        private Grid(byte[] cells)
        {
            _cells = cells;
        }

        public static Grid Empty { get; } = new(new byte[Board.CellCount]);

        public int this[int cell] => _cells[cell];

        public static Grid FromDigits(int[] digits)
        {
            if (digits == null) { throw new ArgumentNullException(nameof(digits)); }
            if (digits.Length != Board.CellCount) { throw new ArgumentException("81 マスを指定してください。", nameof(digits)); }
            var cells = new byte[Board.CellCount];
            for (var i = 0; i < cells.Length; i++)
            {
                if (digits[i] != 0 && !Board.IsDigit(digits[i])) { throw new ArgumentOutOfRangeException(nameof(digits), digits[i], "0〜9 で指定してください。"); }
                cells[i] = (byte)digits[i];
            }

            return new Grid(cells);
        }

        /// <summary>
        /// 81 個の数字を読む。空きは「.」か「0」。空白・改行・「|」「-」「+」は読み飛ばす。
        /// </summary>
        public static Grid Parse(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var digits = new int[Board.CellCount];
            var count = 0;
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c) || c == '|' || c == '-' || c == '+') { continue; }
                if (count == Board.CellCount) { throw new FormatException("82 マス目以降があります。"); }
                digits[count++] = c switch
                {
                    '.' => 0,
                    >= '0' and <= '9' => c - '0',
                    _ => throw new FormatException($"読めない文字です: '{c}'"),
                };
            }

            if (count != Board.CellCount) { throw new FormatException($"マスの数が {count} です。81 マスを指定してください。"); }
            return FromDigits(digits);
        }

        public Grid With(int cell, int digit)
        {
            Board.RequireCell(cell, nameof(cell));
            if (digit != 0 && !Board.IsDigit(digit)) { throw new ArgumentOutOfRangeException(nameof(digit), digit, "0〜9 で指定してください。"); }
            var cells = (byte[])_cells.Clone();
            cells[cell] = (byte)digit;
            return new Grid(cells);
        }

        public int[] ToDigits()
        {
            var digits = new int[Board.CellCount];
            for (var i = 0; i < digits.Length; i++) { digits[i] = _cells[i]; }
            return digits;
        }

        public int FilledCount
        {
            get
            {
                var count = 0;
                foreach (var c in _cells)
                {
                    if (c != 0) { count++; }
                }

                return count;
            }
        }

        public bool IsComplete => FilledCount == Board.CellCount;

        /// <summary>同じ行・列・ブロックに同じ数字が 2 つ以上ある。</summary>
        public bool HasConflict
        {
            get
            {
                for (var unit = 0; unit < Board.UnitCount; unit++)
                {
                    var seen = 0;
                    for (var i = 0; i < Board.Size; i++)
                    {
                        var digit = _cells[Board.UnitCell(unit, i)];
                        if (digit == 0) { continue; }
                        if (Digits.Has(seen, digit)) { return true; }
                        seen |= Digits.Bit(digit);
                    }
                }

                return false;
            }
        }

        /// <summary>cell に digit を置くと重複する、同じ単位の別のマス。重複しなければ -1。</summary>
        public int ConflictWith(int cell, int digit)
        {
            Board.RequireCell(cell, nameof(cell));
            for (var i = 0; i < Board.PeerCount; i++)
            {
                var peer = Board.Peer(cell, i);
                if (_cells[peer] == digit) { return peer; }
            }

            return -1;
        }

        /// <summary>空きを「.」にした 81 文字。</summary>
        public override string ToString()
        {
            var builder = new StringBuilder(Board.CellCount);
            foreach (var c in _cells) { builder.Append(c == 0 ? '.' : (char)('0' + c)); }
            return builder.ToString();
        }

        /// <summary>実行環境に依存しない 32 ビットのハッシュ（FNV-1a）。再現性のテストで固定値と照合する。</summary>
        public uint StableHash()
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in _cells)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }

                return hash;
            }
        }

        public bool Equals(Grid? other)
        {
            if (other is null) { return false; }
            for (var i = 0; i < Board.CellCount; i++)
            {
                if (_cells[i] != other._cells[i]) { return false; }
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is Grid other && Equals(other);

        public override int GetHashCode() => unchecked((int)StableHash());
    }
}
