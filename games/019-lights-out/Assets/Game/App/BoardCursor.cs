using System;
using Game.Core;

namespace Game.App
{
    /// <summary>キーボードで選ぶマス。盤の端で止まり、反対側へは回り込まない。</summary>
    public sealed class BoardCursor
    {
        public const int Center = (LightsBoard.CellCount - 1) / 2;

        public int Cell { get; private set; } = Center;

        public int X => Cell % LightsBoard.Size;

        public int Y => Cell / LightsBoard.Size;

        /// <summary>dx・dy は -1、0、1。盤の外へは動かない。</summary>
        public void Move(int dx, int dy)
        {
            if (dx < -1 || dx > 1) { throw new ArgumentOutOfRangeException(nameof(dx)); }
            if (dy < -1 || dy > 1) { throw new ArgumentOutOfRangeException(nameof(dy)); }
            var x = Math.Max(0, Math.Min(LightsBoard.Size - 1, X + dx));
            var y = Math.Max(0, Math.Min(LightsBoard.Size - 1, Y + dy));
            Cell = LightsBoard.IndexOf(x, y);
        }

        public void MoveTo(int cell) => Cell = LightsBoard.RequireIndex(cell);

        public void Reset() => Cell = Center;
    }
}
