using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 整数の 3 次元ベクトル。x 右・y 上・z 前（北）の左手系で、Unity の座標と同じ向き。
    /// 位置・速度（µm、µm/Tick）にも、Q30 の単位ベクトル（1.0 = 2^30）にも使う。
    /// </summary>
    public readonly struct Vec3l : IEquatable<Vec3l>
    {
        public Vec3l(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public long X { get; }

        public long Y { get; }

        public long Z { get; }

        public static Vec3l Zero => default;

        public bool IsZero => X == 0 && Y == 0 && Z == 0;

        /// <summary>長さの 2 乗。成分が 2^30 以下なら桁あふれしない。</summary>
        public long LengthSquared => (X * X) + (Y * Y) + (Z * Z);

        /// <summary>長さ（切り捨て）。成分が 2^30 を超えても縮めてから求める。</summary>
        public long Length
        {
            get
            {
                var x = X;
                var y = Y;
                var z = Z;
                var shift = 0;
                while (Math.Abs(x) > Fixed.One || Math.Abs(y) > Fixed.One || Math.Abs(z) > Fixed.One)
                {
                    x >>= 1;
                    y >>= 1;
                    z >>= 1;
                    shift++;
                }

                return IntMath.Sqrt((x * x) + (y * y) + (z * z)) << shift;
            }
        }

        public static Vec3l operator +(Vec3l a, Vec3l b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3l operator -(Vec3l a, Vec3l b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vec3l operator -(Vec3l a) => new(-a.X, -a.Y, -a.Z);

        public static bool operator ==(Vec3l left, Vec3l right) => left.Equals(right);

        public static bool operator !=(Vec3l left, Vec3l right) => !left.Equals(right);

        /// <summary>内積。積の和が long に収まる範囲で使う（片方が Q30 の単位ベクトルならもう片方は 2^31 程度まで）。</summary>
        public static long Dot(Vec3l a, Vec3l b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

        /// <summary>Q30 の単位ベクトル <paramref name="unit"/> への射影の長さ（四捨五入）。</summary>
        public static long Project(Vec3l value, Vec3l unit) => Fixed.Round(Dot(value, unit));

        /// <summary>Q30 の単位ベクトルを長さ <paramref name="length"/> 倍する（四捨五入）。</summary>
        public static Vec3l ScaleUnit(Vec3l unit, long length) =>
            new(Fixed.Round(unit.X * length), Fixed.Round(unit.Y * length), Fixed.Round(unit.Z * length));

        /// <summary><paramref name="value"/> を <paramref name="numerator"/> / <paramref name="denominator"/> 倍する（四捨五入、分母は正）。</summary>
        public static Vec3l MulDiv(Vec3l value, long numerator, long denominator) =>
            new(Fixed.RoundDiv(value.X * numerator, denominator), Fixed.RoundDiv(value.Y * numerator, denominator), Fixed.RoundDiv(value.Z * numerator, denominator));

        public bool Equals(Vec3l other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object? obj) => obj is Vec3l other && Equals(other);

        public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ (Y.GetHashCode() * 31) ^ Z.GetHashCode());

        public override string ToString() => $"({X}, {Y}, {Z})";
    }
}
