using System;

namespace Game.Core
{
    /// <summary>
    /// 9 路盤の交点の番号（0〜80）。番号 = 行 × 9 + 列。列 0 が左（A）、行 0 が下（1）。
    /// 表記は列 A〜J（I を除く）と行 1〜9（下から）。パスは <see cref="Pass"/>。
    /// </summary>
    public static class Points
    {
        public const int Size = 9;
        public const int Count = Size * Size;

        /// <summary>パスを表す番号（盤の外）。</summary>
        public const int Pass = -1;

        private const string ColumnLetters = "ABCDEFGHJ";

        private static readonly int[][] _neighbors = Build(diagonal: false);
        private static readonly int[][] _diagonals = Build(diagonal: true);

        public static bool IsOnBoard(int point) => point >= 0 && point < Count;

        public static int At(int column, int row)
        {
            if (column < 0 || column >= Size || row < 0 || row >= Size)
            {
                throw new ArgumentOutOfRangeException(nameof(column), $"({column}, {row}) は盤の外です。");
            }

            return (row * Size) + column;
        }

        public static int Column(int point) => point % Size;

        public static int Row(int point) => point / Size;

        /// <summary>上下左右の隣（盤の中だけ。隅は 2 つ、辺は 3 つ）。</summary>
        public static int[] Neighbors(int point) => _neighbors[point];

        /// <summary>斜めの隣（盤の中だけ）。</summary>
        public static int[] Diagonals(int point) => _diagonals[point];

        /// <summary>"C4" のような表記。パスは "パス"。</summary>
        public static string Name(int point)
        {
            if (point == Pass) { return "パス"; }
            if (!IsOnBoard(point)) { throw new ArgumentOutOfRangeException(nameof(point), point, "盤の外です。"); }
            return $"{ColumnLetters[Column(point)]}{Row(point) + 1}";
        }

        /// <summary>"C4" のような表記を番号にする。大文字小文字は問わない。読めなければ false。</summary>
        public static bool TryParse(string text, out int point)
        {
            point = Pass;
            if (text == null || text.Length != 2) { return false; }
            var column = ColumnLetters.IndexOf(char.ToUpperInvariant(text[0]));
            var row = text[1] - '1';
            if (column < 0 || row < 0 || row >= Size) { return false; }
            point = At(column, row);
            return true;
        }

        public static int Parse(string text)
        {
            if (!TryParse(text, out var point)) { throw new FormatException($"交点の表記ではありません: {text}"); }
            return point;
        }

        private static int[][] Build(bool diagonal)
        {
            var table = new int[Count][];
            for (var p = 0; p < Count; p++)
            {
                var column = Column(p);
                var row = Row(p);
                var list = new System.Collections.Generic.List<int>(4);

                void Add(int dc, int dr)
                {
                    var c = column + dc;
                    var r = row + dr;
                    if (c >= 0 && c < Size && r >= 0 && r < Size) { list.Add((r * Size) + c); }
                }

                if (diagonal)
                {
                    Add(-1, -1);
                    Add(1, -1);
                    Add(-1, 1);
                    Add(1, 1);
                }
                else
                {
                    Add(0, -1);
                    Add(-1, 0);
                    Add(1, 0);
                    Add(0, 1);
                }

                table[p] = list.ToArray();
            }

            return table;
        }
    }
}
