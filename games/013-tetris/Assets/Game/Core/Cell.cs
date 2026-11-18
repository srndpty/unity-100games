using System;

namespace Game.Core
{
    /// <summary>マスの座標、または座標の差分。x は右向き、y は上向きで、最下段が 0。</summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public Cell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public static bool operator ==(Cell left, Cell right) => left.Equals(right);
        public static bool operator !=(Cell left, Cell right) => !left.Equals(right);

        public bool Equals(Cell other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is Cell other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Y);
        public override string ToString() => $"({X}, {Y})";
    }
}
