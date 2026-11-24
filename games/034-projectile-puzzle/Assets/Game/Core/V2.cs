using System;

namespace Game.Core
{
    /// <summary>2 次元のベクトル（m・m/s）。Unity の <c>Vector2</c> と同じ値を Core で扱う。</summary>
    public readonly struct V2 : IEquatable<V2>
    {
        public V2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }

        public float Y { get; }

        public static V2 Zero => default;

        public float LengthSquared => X * X + Y * Y;

        public float Length => MathF.Sqrt(LengthSquared);

        public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y);

        public static V2 operator +(V2 a, V2 b) => new(a.X + b.X, a.Y + b.Y);

        public static V2 operator -(V2 a, V2 b) => new(a.X - b.X, a.Y - b.Y);

        public static V2 operator -(V2 a) => new(-a.X, -a.Y);

        public static V2 operator *(V2 a, float k) => new(a.X * k, a.Y * k);

        public static bool operator ==(V2 left, V2 right) => left.Equals(right);

        public static bool operator !=(V2 left, V2 right) => !left.Equals(right);

        public bool Equals(V2 other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override bool Equals(object? obj) => obj is V2 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);

        public override string ToString() => FormattableString.Invariant($"({X:0.###}, {Y:0.###})");
    }
}
