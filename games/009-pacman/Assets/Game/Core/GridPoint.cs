using System;

namespace Game.Core
{
    /// <summary>盤面上の整数座標。目標マスは盤面の外を指すことがある。</summary>
    public readonly struct GridPoint : IEquatable<GridPoint>
    {
        public GridPoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public GridPoint Offset(Direction direction, int distance) =>
            new(X + (direction.Dx() * distance), Y + (direction.Dy() * distance));

        public long DistanceSquared(GridPoint other)
        {
            long dx = X - other.X;
            long dy = Y - other.Y;
            return (dx * dx) + (dy * dy);
        }

        public bool Equals(GridPoint other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is GridPoint other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Y);
        public override string ToString() => "(" + X + ", " + Y + ")";
        public static bool operator ==(GridPoint left, GridPoint right) => left.Equals(right);
        public static bool operator !=(GridPoint left, GridPoint right) => !left.Equals(right);
    }
}
