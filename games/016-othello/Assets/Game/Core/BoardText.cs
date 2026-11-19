using System;
using System.Text;

namespace Game.Core
{
    /// <summary>文字列で書いた局面。撮影の場面とテストで決まった局面を作るのに使う。</summary>
    public static class BoardText
    {
        /// <summary>最初の行が1段目（上）。B は黒、W は白、. は空き。</summary>
        public static Position Parse(Side toMove, params string[] rows)
        {
            if (rows == null) { throw new ArgumentNullException(nameof(rows)); }
            if (rows.Length != Square.Size) { throw new ArgumentException($"{Square.Size} 行で指定してください。", nameof(rows)); }
            var black = 0UL;
            var white = 0UL;
            for (var y = 0; y < rows.Length; y++)
            {
                if (rows[y] == null || rows[y].Length != Square.Size) { throw new ArgumentException($"{Square.Size} 列で指定してください。", nameof(rows)); }
                for (var x = 0; x < Square.Size; x++)
                {
                    var bit = new Square(x, y).Bit;
                    switch (rows[y][x])
                    {
                        case 'B': black |= bit; break;
                        case 'W': white |= bit; break;
                        case '.': break;
                        default: throw new ArgumentOutOfRangeException(nameof(rows), rows[y][x], "B・W・. で指定してください。");
                    }
                }
            }

            return new Position(black, white, toMove);
        }

        /// <summary><see cref="Parse"/> と同じ書式の8行。</summary>
        public static string[] Format(Position position)
        {
            if (position == null) { throw new ArgumentNullException(nameof(position)); }
            var rows = new string[Square.Size];
            for (var y = 0; y < Square.Size; y++)
            {
                var row = new StringBuilder(Square.Size);
                for (var x = 0; x < Square.Size; x++)
                {
                    row.Append(position.At(new Square(x, y)) switch
                    {
                        Disc.Black => 'B',
                        Disc.White => 'W',
                        _ => '.',
                    });
                }

                rows[y] = row.ToString();
            }

            return rows;
        }
    }
}
