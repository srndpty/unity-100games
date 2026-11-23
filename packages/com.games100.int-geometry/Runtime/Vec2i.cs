using System;

namespace IntGeometry
{
    /// <summary>整数の 2 次元ベクトル（位置・速さ）。単位は使う側が決める（例：1 セル = 256 単位）。</summary>
    public readonly struct Vec2i : IEquatable<Vec2i>
    {
        public Vec2i(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }

        public int Y { get; }

        public static Vec2i Zero => default;

        public bool IsZero => X == 0 && Y == 0;

        /// <summary>長さの 2 乗（桁あふれしないよう long）。</summary>
        public long LengthSquared => ((long)X * X) + ((long)Y * Y);

        public static Vec2i operator +(Vec2i a, Vec2i b) => new(a.X + b.X, a.Y + b.Y);

        public static Vec2i operator -(Vec2i a, Vec2i b) => new(a.X - b.X, a.Y - b.Y);

        public static Vec2i operator -(Vec2i a) => new(-a.X, -a.Y);

        public static bool operator ==(Vec2i left, Vec2i right) => left.Equals(right);

        public static bool operator !=(Vec2i left, Vec2i right) => !left.Equals(right);

        public static long Dot(Vec2i a, Vec2i b) => ((long)a.X * b.X) + ((long)a.Y * b.Y);

        public static long DistanceSquared(Vec2i a, Vec2i b) => (a - b).LengthSquared;

        public bool Equals(Vec2i other) => X == other.X && Y == other.Y;

        public override bool Equals(object? obj) => obj is Vec2i other && Equals(other);

        public override int GetHashCode() => unchecked((X * 397) ^ Y);

        public override string ToString() => $"({X}, {Y})";
    }
}
