using System;

namespace Game.Core
{
    /// <summary>滑走路。南端 (<see cref="X"/>, <see cref="StartZ"/>) から北（+z）へ <see cref="Length"/>、中心線から左右に <see cref="HalfWidth"/>。</summary>
    public readonly struct Runway : IEquatable<Runway>
    {
        public Runway(long x, long startZ, long length, long halfWidth)
        {
            X = x;
            StartZ = startZ;
            Length = length;
            HalfWidth = halfWidth;
        }

        public long X { get; }

        public long StartZ { get; }

        public long Length { get; }

        public long HalfWidth { get; }

        public long EndZ => StartZ + Length;

        public static bool operator ==(Runway left, Runway right) => left.Equals(right);

        public static bool operator !=(Runway left, Runway right) => !left.Equals(right);

        /// <summary>水平位置が滑走路の上か（端ちょうどは上）。</summary>
        public bool Contains(Vec3l position) =>
            Math.Abs(position.X - X) <= HalfWidth && position.Z >= StartZ && position.Z <= EndZ;

        public bool Equals(Runway other) => X == other.X && StartZ == other.StartZ && Length == other.Length && HalfWidth == other.HalfWidth;

        public override bool Equals(object? obj) => obj is Runway other && Equals(other);

        public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ (StartZ.GetHashCode() * 131) ^ (Length.GetHashCode() * 31) ^ HalfWidth.GetHashCode());
    }
}
