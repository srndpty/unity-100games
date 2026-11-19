using System;
using Game.Core;

namespace Game.App
{
    /// <summary>選んでいるマス。盤の外へは出ない。</summary>
    public sealed class CellCursor
    {
        /// <summary>試合の開始時の位置（盤の中央）。</summary>
        public const int Home = 40;

        public int Cell { get; private set; } = Home;

        public void Move(int dx, int dy)
        {
            Cell = Board.Cell(Clamp(Board.Row(Cell) + Math.Sign(dy)), Clamp(Board.Column(Cell) + Math.Sign(dx)));
        }

        public void MoveTo(int cell)
        {
            Board.RequireCell(cell, nameof(cell));
            Cell = cell;
        }

        public void Reset() => Cell = Home;

        /// <summary>
        /// 盤の座標（左上の角が 0、1 マスが 1、右と下が正）からマスを求める。盤外なら null。
        /// 範囲の判定を整数へ丸める前に行い、-0.5 のような値を 0 列目にしない。
        /// </summary>
        public static int? CellAt(double column, double row)
        {
            if (!double.IsFinite(column) || !double.IsFinite(row)) { return null; }
            if (column < 0 || row < 0 || column >= Board.Size || row >= Board.Size) { return null; }
            return Board.Cell((int)row, (int)column);
        }

        private static int Clamp(int value) => Math.Min(Math.Max(value, 0), Board.Size - 1);
    }
}
