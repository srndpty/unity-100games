using System;

namespace Game.Core
{
    /// <summary>
    /// 長さと時間の単位。長さは 1 単位 = 0.1 mm の整数、時間は 60 Tick/秒。
    /// 浮動小数点を使わないので、Mono と .NET で同じ結果になる。
    /// </summary>
    public static class Units
    {
        public const int PerMeter = 10000;

        public const int TickRate = 60;

        /// <summary>整数の平方根（切り捨て）。</summary>
        public static long Sqrt(long value)
        {
            if (value < 0) { throw new ArgumentOutOfRangeException(nameof(value), value, "0 以上にしてください。"); }
            if (value < 2) { return value; }
            var x = (long)Math.Sqrt(value);

            // double の誤差を整数で補正する（結果は環境に依らない）。
            while (x * x > value) { x--; }
            while ((x + 1) * (x + 1) <= value) { x++; }
            return x;
        }

        /// <summary>a / b を 0 から遠い向きに切り上げる（b は正）。</summary>
        public static long CeilDiv(long a, long b) => a >= 0 ? (a + b - 1) / b : -((-a) / b);
    }

    /// <summary>整数の 3 次元ベクトル。x：横（右が正）、y：縦（奥が正、ネットが 0）、z：高さ（地面が 0）。</summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public Vec3(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public int X { get; }

        public int Y { get; }

        public int Z { get; }

        public static Vec3 Zero => default;

        public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static bool operator ==(Vec3 left, Vec3 right) => left.Equals(right);

        public static bool operator !=(Vec3 left, Vec3 right) => !left.Equals(right);

        public Vec3 WithZ(int z) => new(X, Y, z);

        /// <summary>地面での距離の 2 乗。</summary>
        public long FlatDistanceSquared(Vec3 other)
        {
            long dx = X - other.X;
            long dy = Y - other.Y;
            return (dx * dx) + (dy * dy);
        }

        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object? obj) => obj is Vec3 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        public override string ToString() => $"({X}, {Y}, {Z})";
    }
}
