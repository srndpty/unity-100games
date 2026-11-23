using System;

namespace Game.Core
{
    /// <summary>整数の 2 次元ベクトル（位置・速さ）。1 px = <see cref="Field.Unit"/> 単位。</summary>
    public readonly struct Vec2i : IEquatable<Vec2i>
    {
        public Vec2i(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }

        public int Y { get; }

        public static Vec2i Zero => default;

        public bool IsZero => X == 0 && Y == 0;

        /// <summary>長さの 2 乗（桁あふれしないよう long）。</summary>
        public long LengthSquared => ((long)X * X) + ((long)Y * Y);

        public static Vec2i operator +(Vec2i a, Vec2i b) => new(a.X + b.X, a.Y + b.Y);

        public static Vec2i operator -(Vec2i a, Vec2i b) => new(a.X - b.X, a.Y - b.Y);

        public static Vec2i operator -(Vec2i a) => new(-a.X, -a.Y);

        public static bool operator ==(Vec2i left, Vec2i right) => left.Equals(right);

        public static bool operator !=(Vec2i left, Vec2i right) => !left.Equals(right);

        public static long Dot(Vec2i a, Vec2i b) => ((long)a.X * b.X) + ((long)a.Y * b.Y);

        public static long DistanceSquared(Vec2i a, Vec2i b) => (a - b).LengthSquared;

        public bool Equals(Vec2i other) => X == other.X && Y == other.Y;

        public override bool Equals(object? obj) => obj is Vec2i other && Equals(other);

        public override int GetHashCode() => unchecked((X * 397) ^ Y);

        public override string ToString() => $"({X}, {Y})";
    }

    /// <summary>整数だけの計算（Mono と .NET で同じ結果になる）。</summary>
    public static class IntMath
    {
        /// <summary>切り捨ての整数平方根。</summary>
        public static long Sqrt(long value)
        {
            if (value < 0) { throw new ArgumentOutOfRangeException(nameof(value), value, "0 以上にしてください。"); }
            if (value < 2) { return value; }

            // ニュートン法。初期値を大きめに取ると単調に減って収束する。
            var x = value;
            var y = (x + 1) / 2;
            while (y < x)
            {
                x = y;
                y = (x + (value / x)) / 2;
            }

            return x;
        }

        /// <summary>切り上げの整数平方根。</summary>
        public static long SqrtCeiling(long value)
        {
            var root = Sqrt(value);
            return root * root == value ? root : root + 1;
        }

        /// <summary>正規化で長さを求めるときの精度（長さを 2^<see cref="ScaleBits"/> 倍して整数平方根を取る）。</summary>
        public const int ScaleBits = 10;

        /// <summary>正規化の前に向きの成分をこの大きさ以下へ縮める（2 乗の和を 2^<see cref="ScaleBits"/> の 2 乗倍しても桁あふれしない）。</summary>
        public const int MaxDirectionComponent = 1 << 20;

        /// <summary>
        /// <paramref name="direction"/> の向きで長さ <paramref name="length"/> のベクトル（各成分を四捨五入）。
        /// 向きが 0 なら 0 を返す（呼び出し側で直前の向きを保つ）。
        /// 長さは 2^<see cref="ScaleBits"/> 倍してから整数平方根を取るので、(1, 1) のような短い向きでも長さの誤差は丸めの分だけになる
        /// （向きの長さを先に整数へ切り捨てると、(1, 1) を長さ 96 にしたとき (96, 96) になり、実際の長さが約 136 になる）。
        /// </summary>
        public static Vec2i Scale(Vec2i direction, int length)
        {
            if (length < 0) { throw new ArgumentOutOfRangeException(nameof(length), length, "0 以上にしてください。"); }
            if (direction.IsZero) { return Vec2i.Zero; }

            // 大きすぎる向きは、両成分を同じだけ右シフトして縮める（向きはほぼ保たれる。両方 0 にはならない）。
            long x = direction.X;
            long y = direction.Y;
            while (Math.Abs(x) > MaxDirectionComponent || Math.Abs(y) > MaxDirectionComponent)
            {
                x /= 2;
                y /= 2;
            }

            var magnitude = Sqrt(((x * x) + (y * y)) << (2 * ScaleBits));
            if (magnitude == 0) { return Vec2i.Zero; }
            return new Vec2i(RoundDiv((x * length) << ScaleBits, magnitude), RoundDiv((y * length) << ScaleBits, magnitude));
        }

        /// <summary>四捨五入の割り算（0 から遠いほうへ丸める。分母は正）。</summary>
        public static int RoundDiv(long numerator, long denominator)
        {
            if (denominator <= 0) { throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "分母は正の値にしてください。"); }
            var half = denominator / 2;
            return (int)(numerator >= 0 ? (numerator + half) / denominator : -((-numerator + half) / denominator));
        }

        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }

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

    /// <summary>Tick の中で最初に触れた時刻（0〜1 の分数）。触れていなければ <see cref="Hit"/> が false。</summary>
    public readonly struct Touch
    {
        private Touch(bool hit, long numerator, long denominator)
        {
            Hit = hit;
            Numerator = numerator;
            Denominator = denominator;
        }

        public static Touch None => new(false, 1, 1);

        public bool Hit { get; }

        public long Numerator { get; }

        public long Denominator { get; }

        public static Touch At(long numerator, long denominator)
        {
            if (denominator <= 0) { throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "分母は正の値にしてください。"); }
            return new Touch(true, numerator, denominator);
        }

        /// <summary>この接触が <paramref name="other"/> より早ければ true（触れていない接触はいつも遅い）。</summary>
        public bool IsEarlierThan(Touch other)
        {
            if (!Hit) { return false; }
            if (!other.Hit) { return true; }
            return Numerator * other.Denominator < other.Numerator * Denominator;
        }
    }

    /// <summary>
    /// 動く 2 つの円の接触判定（026 から流用）。Tick の中で両者は始めの位置から終わりの位置へ直線で動くとみなし、相対軌跡を掃引する
    /// （端点だけの判定では、速い弾が自機をすり抜ける・斜めのかすりを見逃す。002・009 の知見）。
    /// 中心の距離が半径の和より小さくなったら触れたとする（ちょうど接するだけでは触れない）。
    /// <para>
    /// 触れるかどうかは整数で厳密に判定する。最初に触れた時刻は平方根を含むので、切り上げの整数平方根で求めた分数にする
    /// （同じ Tick の中で複数の相手のどれが先かを決めるためだけに使う）。
    /// </para>
    /// </summary>
    public static class Sweep
    {
        public static Touch First(Vec2i aStart, Vec2i aEnd, Vec2i bStart, Vec2i bEnd, int radiusSum)
        {
            if (radiusSum <= 0) { throw new ArgumentOutOfRangeException(nameof(radiusSum), radiusSum, "半径の和は正にしてください。"); }

            // 相対位置 p(t) = p0 + t d。|p(t)|^2 = a t^2 + b t + c（c は始めの距離^2 - R^2）。
            var p0 = aStart - bStart;
            var d = (aEnd - aStart) - (bEnd - bStart);
            var r2 = (long)radiusSum * radiusSum;
            var c = p0.LengthSquared - r2;
            if (c < 0) { return Touch.At(0, 1); }

            var a = d.LengthSquared;
            var b = 2 * Vec2i.Dot(p0, d);
            if (a == 0 || b >= 0) { return Touch.None; }

            // 最も近づく時刻 t* = -b / 2a。t* が 1 以上なら終わりの位置で、そうでなければ t* で近さを判定する。
            var discriminant = (b * b) - (4 * a * c);
            var reaches = -b >= 2 * a ? a + b + c < 0 : discriminant > 0;
            if (!reaches) { return Touch.None; }

            // 最初の根 t = (-b - √disc) / 2a。√ を切り上げると少し早めの時刻になる（0 を下回らないよう寄せる）。
            var numerator = -b - IntMath.SqrtCeiling(discriminant);
            return Touch.At(numerator < 0 ? 0 : numerator, 2 * a);
        }
    }
}
