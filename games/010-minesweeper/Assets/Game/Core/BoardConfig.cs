using System;

namespace Game.Core
{
    /// <summary>盤面の大きさと爆弾の数。</summary>
    [Serializable]
    public sealed class BoardConfig
    {
        public const int MinSize = 4;
        public const int MaxSize = 32;

        /// <summary>初手のセルと周囲8セル。盤面の中央で初手を打っても爆弾を置き切れる数を保証する。</summary>
        public const int ProtectedCells = 9;

        public int Width = 16;
        public int Height = 16;
        public int MineCount = 40;

        public int CellCount => Width * Height;
        public int SafeCellCount => CellCount - MineCount;

        public void Validate()
        {
            RequireRange(Width, MinSize, MaxSize, nameof(Width));
            RequireRange(Height, MinSize, MaxSize, nameof(Height));
            RequireRange(MineCount, 1, CellCount - ProtectedCells, nameof(MineCount));
        }

        public BoardConfig Copy() => new() { Width = Width, Height = Height, MineCount = MineCount };

        private static void RequireRange(int value, int min, int max, string name)
        {
            if (value < min || value > max) { throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。"); }
        }
    }
}
