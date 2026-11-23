using System;

namespace Game.Core
{
    /// <summary>1 Tick の中の時刻を表す分数（分母は正）。0 が Tick の始め、1 が終わり。</summary>
    public readonly struct Moment : IEquatable<Moment>, IComparable<Moment>
    {
        public Moment(long numerator, long denominator)
        {
            if (denominator <= 0) { throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "分母は正にしてください。"); }
            Numerator = numerator;
            Denominator = denominator;
        }

        public static Moment Zero => new(0, 1);

        public static Moment One => new(1, 1);

        public long Numerator { get; }

        public long Denominator { get; }

        public int CompareTo(Moment other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

        public bool Equals(Moment other) => CompareTo(other) == 0;

        public override bool Equals(object? obj) => obj is Moment other && Equals(other);

        // 等しい分数は同じ実数なので、正しく丸めた double も等しい。
        public override int GetHashCode() => ((double)Numerator / Denominator).GetHashCode();

        public override string ToString() => $"{Numerator}/{Denominator}";

        /// <summary><paramref name="from"/> から <paramref name="to"/> へ線形に動く値のこの時刻の値（0 へ向けて切り捨て）。</summary>
        public long Lerp(long from, long to) => from + ((to - from) * Numerator / Denominator);

        public static Moment Max(Moment a, Moment b) => a.CompareTo(b) >= 0 ? a : b;

        public static Moment Min(Moment a, Moment b) => a.CompareTo(b) <= 0 ? a : b;

        public static bool operator ==(Moment left, Moment right) => left.Equals(right);

        public static bool operator !=(Moment left, Moment right) => !left.Equals(right);

        public static bool operator <(Moment left, Moment right) => left.CompareTo(right) < 0;

        public static bool operator >(Moment left, Moment right) => left.CompareTo(right) > 0;

        public static bool operator <=(Moment left, Moment right) => left.CompareTo(right) <= 0;

        public static bool operator >=(Moment left, Moment right) => left.CompareTo(right) >= 0;
    }

    /// <summary>
    /// 重なっている時刻の開区間 (Lo, Hi)。Tick の範囲 [0, 1] の外の端は -1 と 2 で表す
    /// （[0, 1] との共通部分だけを問うので、無限大の代わりになる）。
    /// </summary>
    public readonly struct Span
    {
        public Span(Moment lo, Moment hi)
        {
            Lo = lo;
            Hi = hi;
        }

        /// <summary>Tick の間ずっと重なっている。</summary>
        public static Span Always => new(new Moment(-1, 1), new Moment(2, 1));

        /// <summary>重ならない。</summary>
        public static Span Never => new(new Moment(1, 1), new Moment(0, 1));

        public Moment Lo { get; }

        public Moment Hi { get; }

        /// <summary>この Tick の中（[0, 1]）に重なる時刻がある。</summary>
        public bool Hits => Lo < Hi && Lo < Moment.One && Hi > Moment.Zero;

        /// <summary>最初に触れた時刻（<see cref="Hits"/> のときだけ意味がある）。</summary>
        public Moment Contact => Lo < Moment.Zero ? Moment.Zero : Lo;

        public Span Intersect(Span other) => new(Moment.Max(Lo, other.Lo), Moment.Min(Hi, other.Hi));
    }

    /// <summary>軸平行の箱（最小の角と大きさ）。x＝横、y＝高さ、z＝奥行き。</summary>
    public readonly struct Box3 : IEquatable<Box3>
    {
        public Box3(int x, int y, int z, int width, int height, int depth)
        {
            if (width <= 0 || height <= 0 || depth <= 0) { throw new ArgumentOutOfRangeException(nameof(width), "大きさは正にしてください。"); }
            X = x;
            Y = y;
            Z = z;
            Width = width;
            Height = height;
            Depth = depth;
        }

        public int X { get; }

        public int Y { get; }

        public int Z { get; }

        public int Width { get; }

        public int Height { get; }

        public int Depth { get; }

        public Box3 Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz, Width, Height, Depth);

        public bool Equals(Box3 other) => X == other.X && Y == other.Y && Z == other.Z && Width == other.Width && Height == other.Height && Depth == other.Depth;

        public override bool Equals(object? obj) => obj is Box3 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, Z, Width, Height, Depth);

        public override string ToString() => $"Box3(x {X}+{Width}, y {Y}+{Height}, z {Z}+{Depth})";

        public static bool operator ==(Box3 left, Box3 right) => left.Equals(right);

        public static bool operator !=(Box3 left, Box3 right) => !left.Equals(right);
    }

    /// <summary>
    /// 1 Tick の間に直線で動く箱と、止まっている箱の掃引。各軸で重なる時刻の開区間を求め、その共通部分で判定する
    /// （整数の分数で厳密。ちょうど接するだけでは触れない）。
    /// </summary>
    public static class Sweep3
    {
        /// <summary>
        /// 1 軸ぶん：区間 [start, start + size] が Tick の間に [end, end + size] へ動くとき、
        /// 止まっている区間 [other, other + otherSize] と重なる（端だけの接触は含まない）時刻の開区間。
        /// </summary>
        public static Span Axis(int start, int end, int size, int other, int otherSize)
        {
            long d = (long)end - start;
            if (d == 0) { return start < (long)other + otherSize && (long)start + size > other ? Span.Always : Span.Never; }
            if (d > 0)
            {
                return new Span(new Moment((long)other - size - start, d), new Moment((long)other + otherSize - start, d));
            }

            var e = -d;
            return new Span(new Moment((long)start - other - otherSize, e), new Moment((long)start + size - other, e));
        }

        /// <summary><paramref name="from"/> から <paramref name="to"/>（同じ大きさ）へ動く箱と、止まっている箱の重なる時刻。</summary>
        public static Span Boxes(Box3 from, Box3 to, Box3 obstacle)
        {
            if (from.Width != to.Width || from.Height != to.Height || from.Depth != to.Depth) { throw new ArgumentException("動く箱の大きさは Tick の間で同じにしてください。", nameof(to)); }
            var z = Axis(from.Z, to.Z, from.Depth, obstacle.Z, obstacle.Depth);
            if (!z.Hits) { return z; }
            var x = z.Intersect(Axis(from.X, to.X, from.Width, obstacle.X, obstacle.Width));
            if (!x.Hits) { return x; }
            return x.Intersect(Axis(from.Y, to.Y, from.Height, obstacle.Y, obstacle.Height));
        }
    }
}
