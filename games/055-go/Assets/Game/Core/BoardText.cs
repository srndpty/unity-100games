using System;
using System.Text;

namespace Game.Core
{
    /// <summary>
    /// 盤の文字列表記。1 行が盤の 1 行で、先頭の行が 9 の行（上）。X が黒、O が白、. が空点。空白は読み飛ばす。
    /// テストの局面と、記録の見やすい出力に使う。
    /// </summary>
    public static class BoardText
    {
        public static Stone[] ParseCells(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var cells = new Stone[Points.Count];
            var rows = text.Replace("\r", string.Empty).Split('\n');
            var row = Points.Size - 1;
            foreach (var line in rows)
            {
                var compact = line.Replace(" ", string.Empty).Replace("\t", string.Empty);
                if (compact.Length == 0) { continue; }
                if (row < 0) { throw new FormatException("行が 9 行を超えています。"); }
                if (compact.Length != Points.Size) { throw new FormatException($"{row + 1} の行の幅が {compact.Length} です（9 が必要）。"); }
                for (var column = 0; column < Points.Size; column++)
                {
                    cells[Points.At(column, row)] = compact[column] switch
                    {
                        'X' => Stone.Black,
                        'O' => Stone.White,
                        '.' => Stone.Empty,
                        _ => throw new FormatException($"読めない文字 '{compact[column]}'（{row + 1} の行）"),
                    };
                }

                row--;
            }

            if (row != -1) { throw new FormatException("行が 9 行に足りません。"); }
            return cells;
        }

        public static Board Parse(string text) => Board.FromCells(ParseCells(text));

        public static string Format(Board board)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            var sb = new StringBuilder();
            for (var row = Points.Size - 1; row >= 0; row--)
            {
                for (var column = 0; column < Points.Size; column++)
                {
                    sb.Append(board[Points.At(column, row)] switch
                    {
                        Stone.Black => 'X',
                        Stone.White => 'O',
                        _ => '.',
                    });
                }

                sb.Append('\n');
            }

            return sb.ToString();
        }
    }
}
