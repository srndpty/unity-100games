using System;

namespace Game.Core
{
    /// <summary>マスに置かれる結晶。Empty は消えた直後の空きで、解決の途中にだけ現れる。</summary>
    public enum Piece : byte
    {
        Empty = 0,
        Coral = 1,
        Amber = 2,
        Kelp = 3,
        Lagoon = 4,
        Pearl = 5,
        Violet = 6,
    }

    public static class Pieces
    {
        /// <summary>使える色の最大数。</summary>
        public const int MaxColors = 6;

        /// <summary>index 番目（0 始まり）の色。</summary>
        public static Piece Color(int index)
        {
            if (index < 0 || index >= MaxColors) { throw new ArgumentOutOfRangeException(nameof(index), index, "未定義の色です。"); }
            return (Piece)(index + 1);
        }
    }

    /// <summary>盤面のマスの位置。y = 0 が最下段。</summary>
    public readonly struct Point : IEquatable<Point>
    {
        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        /// <summary>上下左右に隣り合うか。</summary>
        public bool IsAdjacentTo(Point other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y) == 1;

        public Point Offset(int dx, int dy) => new(X + dx, Y + dy);

        public bool Equals(Point other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is Point other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Y;
        public override string ToString() => $"({X}, {Y})";

        public static bool operator ==(Point left, Point right) => left.Equals(right);
        public static bool operator !=(Point left, Point right) => !left.Equals(right);
    }

    /// <summary>2マスの入れ替え。</summary>
    public readonly struct Swap : IEquatable<Swap>
    {
        public Swap(Point from, Point to)
        {
            From = from;
            To = to;
        }

        public Point From { get; }
        public Point To { get; }

        public bool Equals(Swap other) => From == other.From && To == other.To;
        public override bool Equals(object? obj) => obj is Swap other && Equals(other);
        public override int GetHashCode() => (From.GetHashCode() * 397) ^ To.GetHashCode();
        public override string ToString() => $"{From}<->{To}";

        public static bool operator ==(Swap left, Swap right) => left.Equals(right);
        public static bool operator !=(Swap left, Swap right) => !left.Equals(right);
    }
}
