using System;

namespace TileMotion
{
    /// <summary>
    /// 軸に平行な箱。位置は左下の角で、1 タイルを <see cref="Units.Tile"/> 等分した整数（y は上向き）。
    /// 重なりは開区間で判定し、辺が接するだけなら重ならない。
    /// </summary>
    public readonly struct Box : IEquatable<Box>
    {
        public Box(int x, int y, int width, int height)
        {
            if (width <= 0) { throw new ArgumentOutOfRangeException(nameof(width), width, "幅は正の値にしてください。"); }
            if (height <= 0) { throw new ArgumentOutOfRangeException(nameof(height), height, "高さは正の値にしてください。"); }
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public int X { get; }

        public int Y { get; }

        public int Width { get; }

        public int Height { get; }

        public int Left => X;

        public int Right => X + Width;

        public int Bottom => Y;

        public int Top => Y + Height;

        public int CenterX => X + (Width / 2);

        public Box At(int x, int y) => new(x, y, Width, Height);

        public Box Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

        /// <summary>
        /// 2 つの箱を囲む箱。同じ大きさの箱が 1 軸だけ動いたとき（横か縦の一方だけが違うとき）は、
        /// 途中のすべての位置が通る範囲とちょうど一致する（掃引の判定に使う）。
        /// </summary>
        public static Box Span(Box a, Box b)
        {
            var left = Math.Min(a.Left, b.Left);
            var bottom = Math.Min(a.Bottom, b.Bottom);
            return new Box(left, bottom, Math.Max(a.Right, b.Right) - left, Math.Max(a.Top, b.Top) - bottom);
        }

        public bool Overlaps(Box other)
        {
            return Left < other.Right && other.Left < Right && Bottom < other.Top && other.Bottom < Top;
        }

        public bool Equals(Box other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

        public override bool Equals(object? obj) => obj is Box other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

        public override string ToString() => $"Box({X}, {Y}, {Width}×{Height})";

        public static bool operator ==(Box left, Box right) => left.Equals(right);

        public static bool operator !=(Box left, Box right) => !left.Equals(right);
    }

    /// <summary>単位と時間の定数。</summary>
    public static class Units
    {
        /// <summary>1 タイルの大きさ（整数の単位）。1 タイル = 16 px なので 1 px = 16 単位。</summary>
        public const int Tile = 256;

        public const int Pixel = Tile / 16;

        public const int TickRate = 60;

        /// <summary>負の数でも切り下げる整数の割り算。</summary>
        public static int FloorDiv(int value, int divisor)
        {
            var quotient = value / divisor;
            return (value % divisor != 0 && (value < 0) != (divisor < 0)) ? quotient - 1 : quotient;
        }
    }
}
