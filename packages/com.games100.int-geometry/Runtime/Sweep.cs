using System;
using System.Numerics;

namespace IntGeometry
{
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
    /// 動く 2 つの円の接触判定（026・027 から抽出）。Tick の中で両者は始めの位置から終わりの位置へ直線で動くとみなし、相対軌跡を掃引する
    /// （端点だけの判定では、速い弾が相手をすり抜ける・斜めのかすりを見逃す。002・009 の知見）。
    /// 中心の距離が半径の和より小さくなったら触れたとする（ちょうど接するだけでは触れない）。
    /// <para>
    /// どちらの関数も、各軸で「始めの距離 &gt; 両者の移動量 + 半径の和」の組は計算せずに触れないとする（遠い組は桁を気にしなくてよい）。
    /// </para>
    /// </summary>
    public static class Sweep
    {
        /// <summary>
        /// <see cref="First"/> が扱える、相対位置・相対移動・半径の和の各成分の上限。これ以下なら 2 次式（判別式など）が long に収まる。
        /// </summary>
        public const int FirstLimit = 1 << 14;

        /// <summary>
        /// 最初に触れた時刻。触れるかどうかは整数で厳密に判定する。時刻は平方根を含むので、切り上げの整数平方根で求めた分数にする
        /// （同じ Tick の中で複数の相手のどれが先かを決めるためだけに使う）。
        /// 近い組の値が <see cref="FirstLimit"/> を超えたら例外にする（黙って桁あふれさせない）。大きな移動の有無だけなら <see cref="Touches"/> を使う。
        /// </summary>
        public static Touch First(Vec2i aStart, Vec2i aEnd, Vec2i bStart, Vec2i bEnd, int radiusSum)
        {
            if (!Relative(aStart, aEnd, bStart, bEnd, radiusSum, out var p0x, out var p0y, out var dx, out var dy)) { return Touch.None; }

            var largest = Math.Max(Math.Max(Math.Abs(p0x), Math.Abs(p0y)), Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)), radiusSum));
            if (largest > FirstLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(radiusSum), largest, $"掃引の値は各成分 {FirstLimit} 以下にしてください。");
            }

            // 相対位置 p(t) = p0 + t d。|p(t)|^2 = a t^2 + b t + c（c は始めの距離^2 - R^2）。
            var r2 = (long)radiusSum * radiusSum;
            var c = (p0x * p0x) + (p0y * p0y) - r2;
            if (c < 0) { return Touch.At(0, 1); }

            var a = (dx * dx) + (dy * dy);
            var b = 2 * ((p0x * dx) + (p0y * dy));
            if (a == 0 || b >= 0) { return Touch.None; }

            // 最も近づく時刻 t* = -b / 2a。t* が 1 以上なら終わりの位置で、そうでなければ t* で近さを判定する。
            var discriminant = (b * b) - (4 * a * c);
            var reaches = -b >= 2 * a ? a + b + c < 0 : discriminant > 0;
            if (!reaches) { return Touch.None; }

            // 最初の根 t = (-b - √disc) / 2a。√ を切り上げると少し早めの時刻になる（0 を下回らないよう寄せる）。
            var numerator = -b - IntMath.SqrtCeiling(discriminant);
            return Touch.At(numerator < 0 ? 0 : numerator, 2 * a);
        }

        /// <summary>
        /// 触れるかどうかだけを、座標の大きさに依らず厳密に判定する（027 の <c>VeilRun.Touches</c> から抽出）。
        /// 小さい組は <see cref="First"/> と同じ long の式、大きい組は同じ式を <see cref="BigInteger"/> で計算する。
        /// </summary>
        public static bool Touches(Vec2i aStart, Vec2i aEnd, Vec2i bStart, Vec2i bEnd, int radiusSum)
        {
            if (!Relative(aStart, aEnd, bStart, bEnd, radiusSum, out var p0x, out var p0y, out var dx, out var dy)) { return false; }

            var largest = Math.Max(Math.Max(Math.Abs(p0x), Math.Abs(p0y)), Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)), radiusSum));
            if (largest <= FirstLimit) { return First(aStart, aEnd, bStart, bEnd, radiusSum).Hit; }

            long radius = radiusSum;
            var c = (p0x * p0x) + (p0y * p0y) - (radius * radius);
            if (c < 0) { return true; }
            var a = (dx * dx) + (dy * dy);
            var b = 2 * ((p0x * dx) + (p0y * dy));
            if (a == 0 || b >= 0) { return false; }

            // 最も近づく時刻 t* = -b / 2a。t* が 1 以上なら終わりの位置で、そうでなければ t* での近さ（判別式が正か）で判定する。
            if (-b >= 2 * a) { return a + b + c < 0; }
            return (new BigInteger(b) * b) - (new BigInteger(4) * a * c) > 0;
        }

        /// <summary>相対位置と相対移動。各軸で届かない組（遠い組）なら false。</summary>
        private static bool Relative(Vec2i aStart, Vec2i aEnd, Vec2i bStart, Vec2i bEnd, int radiusSum, out long p0x, out long p0y, out long dx, out long dy)
        {
            if (radiusSum <= 0) { throw new ArgumentOutOfRangeException(nameof(radiusSum), radiusSum, "半径の和は正にしてください。"); }
            p0x = (long)aStart.X - bStart.X;
            p0y = (long)aStart.Y - bStart.Y;
            long ax = (long)aEnd.X - aStart.X;
            long ay = (long)aEnd.Y - aStart.Y;
            long bx = (long)bEnd.X - bStart.X;
            long by = (long)bEnd.Y - bStart.Y;
            dx = ax - bx;
            dy = ay - by;
            return Math.Abs(p0x) <= Math.Abs(ax) + Math.Abs(bx) + radiusSum && Math.Abs(p0y) <= Math.Abs(ay) + Math.Abs(by) + radiusSum;
        }
    }
}
