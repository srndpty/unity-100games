using System;

namespace Game.Core
{
    /// <summary>文字列で書いた盤面。撮影の場面とテストで決まった盤面を作るのに使う。</summary>
    public static class BoardText
    {
        /// <summary>最初の行が最上段。C A K L P V は色、. は空き。</summary>
        public static Board Parse(params string[] rows)
        {
            if (rows == null) { throw new ArgumentNullException(nameof(rows)); }
            if (rows.Length != Board.Height) { throw new ArgumentException($"{Board.Height} 行で指定してください。", nameof(rows)); }
            var cells = new Piece[Board.CellCount];
            for (var r = 0; r < rows.Length; r++)
            {
                if (rows[r] == null || rows[r].Length != Board.Width) { throw new ArgumentException($"{Board.Width} 列で指定してください。", nameof(rows)); }
                var y = rows.Length - 1 - r;
                for (var x = 0; x < Board.Width; x++) { cells[Board.Index(x, y)] = ToPiece(rows[r][x]); }
            }

            return Board.FromCells(cells);
        }

        private static Piece ToPiece(char c) => c switch
        {
            '.' => Piece.Empty,
            'C' => Piece.Coral,
            'A' => Piece.Amber,
            'K' => Piece.Kelp,
            'L' => Piece.Lagoon,
            'P' => Piece.Pearl,
            'V' => Piece.Violet,
            _ => throw new ArgumentOutOfRangeException(nameof(c), c, "未定義の文字です。"),
        };
    }
}
