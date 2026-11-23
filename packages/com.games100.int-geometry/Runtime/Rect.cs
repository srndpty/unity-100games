using System;

namespace IntGeometry
{
    /// <summary>軸に沿った整数の矩形（両端を含む）。</summary>
    public readonly struct Rect : IEquatable<Rect>
    {
        public Rect(int minX, int minY, int maxX, int maxY)
        {
            if (maxX < minX || maxY < minY) { throw new ArgumentException("最大は最小以上にしてください。"); }
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public int MinX { get; }

        public int MinY { get; }

        public int MaxX { get; }

        public int MaxY { get; }

        public int Width => MaxX - MinX;

        public int Height => MaxY - MinY;

        public bool Contains(Vec2i point) => point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;

        /// <summary>中心 <paramref name="center"/>・半径 <paramref name="radius"/> の円の一部でも矩形に入っているか（外接矩形で判定）。</summary>
        public bool Overlaps(Vec2i center, int radius) =>
            center.X + radius > MinX && center.X - radius < MaxX && center.Y + radius > MinY && center.Y - radius < MaxY;

        public static bool operator ==(Rect left, Rect right) => left.Equals(right);

        public static bool operator !=(Rect left, Rect right) => !left.Equals(right);

        public bool Equals(Rect other) => MinX == other.MinX && MinY == other.MinY && MaxX == other.MaxX && MaxY == other.MaxY;

        public override bool Equals(object? obj) => obj is Rect other && Equals(other);

        public override int GetHashCode() => unchecked((((((MinX * 397) ^ MinY) * 397) ^ MaxX) * 397) ^ MaxY);

        public override string ToString() => $"[{MinX}, {MinY}]-[{MaxX}, {MaxY}]";
    }
}
