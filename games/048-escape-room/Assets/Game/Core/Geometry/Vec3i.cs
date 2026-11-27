using System;

namespace Game.Core.Geometry
{
    /// <summary>整数の 3 次元ベクトル（部屋の座標は mm）。x は右・y は上・z は奥（Unity と同じ左手系）。</summary>
    public readonly struct Vec3i : IEquatable<Vec3i>
    {
        public Vec3i(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public int X { get; }
        public int Y { get; }
        public int Z { get; }

        public static Vec3i Zero => default;

        public int this[int axis] => axis switch
        {
            0 => X,
            1 => Y,
            2 => Z,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };

        public static Vec3i operator +(Vec3i a, Vec3i b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vec3i operator -(Vec3i a, Vec3i b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vec3i operator -(Vec3i a) => new(-a.X, -a.Y, -a.Z);

        public static Vec3i operator *(Vec3i a, int k) => new(a.X * k, a.Y * k, a.Z * k);

        public static bool operator ==(Vec3i left, Vec3i right) => left.Equals(right);

        public static bool operator !=(Vec3i left, Vec3i right) => !left.Equals(right);

        public static long Dot(Vec3i a, Vec3i b) => ((long)a.X * b.X) + ((long)a.Y * b.Y) + ((long)a.Z * b.Z);

        public bool Equals(Vec3i other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object? obj) => obj is Vec3i other && Equals(other);

        public override int GetHashCode() => unchecked((((X * 397) ^ Y) * 397) ^ Z);

        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    /// <summary>軸に平行な箱（両端を含む閉区間、mm）。</summary>
    public readonly struct Box3 : IEquatable<Box3>
    {
        public Box3(Vec3i min, Vec3i max)
        {
            Min = min;
            Max = max;
        }

        public Vec3i Min { get; }
        public Vec3i Max { get; }

        public Vec3i Size => Max - Min;

        /// <summary>中心（mm、端数は切り捨て）。</summary>
        public Vec3i Center => new((Min.X + Max.X) / 2, (Min.Y + Max.Y) / 2, (Min.Z + Max.Z) / 2);

        /// <summary>壁（0 北・1 東・2 南・3 西）を向いて見たときの手前の面の中心（クリックの確かめに使う）。</summary>
        public Vec3i FrontCenter(int wall)
        {
            var c = Center;
            return (wall & 3) switch
            {
                0 => new Vec3i(c.X, c.Y, Min.Z),
                1 => new Vec3i(Min.X, c.Y, c.Z),
                2 => new Vec3i(c.X, c.Y, Max.Z),
                _ => new Vec3i(Max.X, c.Y, c.Z),
            };
        }

        public bool IsValid => Min.X < Max.X && Min.Y < Max.Y && Min.Z < Max.Z;

        public bool Contains(Vec3i p) =>
            p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z;

        public bool Inside(Box3 outer) =>
            Min.X >= outer.Min.X && Min.Y >= outer.Min.Y && Min.Z >= outer.Min.Z && Max.X <= outer.Max.X && Max.Y <= outer.Max.Y && Max.Z <= outer.Max.Z;

        public static bool operator ==(Box3 left, Box3 right) => left.Equals(right);

        public static bool operator !=(Box3 left, Box3 right) => !left.Equals(right);

        public bool Equals(Box3 other) => Min == other.Min && Max == other.Max;

        public override bool Equals(object? obj) => obj is Box3 other && Equals(other);

        public override int GetHashCode() => unchecked((Min.GetHashCode() * 397) ^ Max.GetHashCode());

        public override string ToString() => $"[{Min} - {Max}]";
    }
}
