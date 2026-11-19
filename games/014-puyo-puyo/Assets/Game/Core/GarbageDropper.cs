using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>枯れ胞子を盤面へ降らせる。</summary>
    public static class GarbageDropper
    {
        /// <summary>6個ごとに全列へ1個ずつ、端数は乱数で選んだ別々の列へ置く。満杯の列にあふれた分は消える。</summary>
        public static FallResult Drop(Board board, int count, SeededRandom random)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            if (random == null) { throw new ArgumentNullException(nameof(random)); }
            if (count < 0) { throw new ArgumentOutOfRangeException(nameof(count), count, "0 以上を指定してください。"); }

            var perColumn = new int[Board.Width];
            for (var x = 0; x < Board.Width; x++) { perColumn[x] = count / Board.Width; }
            foreach (var x in PickColumns(count % Board.Width, random)) { perColumn[x]++; }

            var cells = board.ToCells();
            var moves = new List<FallMove>();
            for (var x = 0; x < Board.Width; x++)
            {
                var y = ColumnHeight(cells, x);
                for (var i = 0; i < perColumn[x] && y < Board.Height; i++, y++)
                {
                    cells[Board.Index(x, y)] = Cell.Ash;
                    moves.Add(new FallMove(x, Board.Height + i, y, Cell.Ash));
                }
            }

            return new FallResult(Board.FromCells(cells), moves.AsReadOnly());
        }

        private static int[] PickColumns(int count, SeededRandom random)
        {
            var columns = new int[Board.Width];
            for (var i = 0; i < columns.Length; i++) { columns[i] = i; }
            for (var i = 0; i < count; i++)
            {
                var j = i + random.Next(columns.Length - i);
                (columns[i], columns[j]) = (columns[j], columns[i]);
            }

            var picked = new int[count];
            Array.Copy(columns, picked, count);
            return picked;
        }

        private static int ColumnHeight(Cell[] cells, int x)
        {
            for (var y = Board.Height - 1; y >= 0; y--)
            {
                if (cells[Board.Index(x, y)] != Cell.Empty) { return y + 1; }
            }

            return 0;
        }
    }
}
