using System;

namespace Game.Core
{
    /// <summary>Unity に依存しない 2 次元ベクトル。</summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }

        public float Y { get; }

        public static Vec2 Zero => default;

        public float Length => MathF.Sqrt((X * X) + (Y * Y));

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);

        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);

        public static Vec2 operator *(Vec2 v, float scale) => new Vec2(v.X * scale, v.Y * scale);

        public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);

        public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);

        public static Vec2 Lerp(Vec2 from, Vec2 to, float t) => from + ((to - from) * t);

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);
    }
}
