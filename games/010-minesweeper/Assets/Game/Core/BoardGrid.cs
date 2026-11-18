using System;

namespace Game.Core
{
    /// <summary>セル番号と座標の変換、8近傍の列挙。</summary>
    public readonly struct BoardGrid : IEquatable<BoardGrid>
    {
        public const int MaxNeighbors = 8;

        public BoardGrid(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }
        public int Height { get; }
        public int CellCount => Width * Height;

        public bool Contains(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        public int Index(int x, int y) => (y * Width) + x;

        public int X(int cell) => cell % Width;

        public int Y(int cell) => cell / Width;

        /// <summary>盤面内の近傍を <paramref name="buffer"/> に書き、その数を返す。毎回の配列確保を避けるため呼び出し側が用意する。</summary>
        public int Neighbors(int cell, int[] buffer)
        {
            var count = 0;
            var cellX = X(cell);
            var cellY = Y(cell);
            for (var y = cellY - 1; y <= cellY + 1; y++)
            {
                for (var x = cellX - 1; x <= cellX + 1; x++)
                {
                    if ((x != cellX || y != cellY) && Contains(x, y)) { buffer[count++] = Index(x, y); }
                }
            }

            return count;
        }

        public bool Equals(BoardGrid other) => Width == other.Width && Height == other.Height;

        public override bool Equals(object? obj) => obj is BoardGrid other && Equals(other);

        public override int GetHashCode() => (Width * 397) ^ Height;

        public static bool operator ==(BoardGrid left, BoardGrid right) => left.Equals(right);

        public static bool operator !=(BoardGrid left, BoardGrid right) => !left.Equals(right);
    }
}
