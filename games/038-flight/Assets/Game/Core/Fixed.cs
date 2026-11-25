using System;

namespace Game.Core
{
    /// <summary>
    /// Q30 の固定小数点（1.0 = 2^30）と、整数だけの三角関数。Mono と .NET で同じ値になる。
    /// 角度は 1 周 = <see cref="Units.FullTurn"/>。
    /// </summary>
    public static class Fixed
    {
        public const int Shift = 30;

        public const long One = 1L << Shift;

        /// <summary>π を 2^30 倍して四捨五入した値。</summary>
        public const long PiQ30 = 3373259426L;

        /// <summary>Q30 の積の後始末（2^30 で割って四捨五入）。</summary>
        public static long Round(long productQ60) => (productQ60 + (1L << (Shift - 1))) >> Shift;

        /// <summary>四捨五入の割り算（0 から遠いほうへ丸める。分母は正）。</summary>
        public static long RoundDiv(long numerator, long denominator)
        {
            if (denominator <= 0) { throw new ArgumentOutOfRangeException(nameof(denominator), denominator, "分母は正の値にしてください。"); }
            var half = denominator / 2;
            return numerator >= 0 ? (numerator + half) / denominator : -((-numerator + half) / denominator);
        }

        /// <summary>角度を -32768 より大きく 32768 以下の符号つきへ直す。</summary>
        public static int Wrap(int angle)
        {
            var a = angle & (Units.FullTurn - 1);
            return a > Units.FullTurn / 2 ? a - Units.FullTurn : a;
        }

        /// <summary>角度の単位をラジアン（Q30）へ直す。</summary>
        public static long Radians(int angle) => (long)angle * PiQ30 / (Units.FullTurn / 2);

        /// <summary>sin（Q30）。sin(π - a) = sin a で ±π/2 の内側へ写してから展開する（π の 2 乗は Q60 で long に収まらない）。</summary>
        public static long Sin(int angle)
        {
            const int Quarter = Units.FullTurn / 4;
            const int Half = Units.FullTurn / 2;
            var a = Wrap(angle);
            if (a > Quarter) { a = Half - a; }
            else if (a < -Quarter) { a = -Half - a; }
            return SinRadians(Radians(a));
        }

        /// <summary>cos（Q30）。</summary>
        public static long Cos(int angle) => Sin(angle + (Units.FullTurn / 4));

        /// <summary>|x| ≤ π/2 のラジアン（Q30）の sin。整数のテイラー展開を項が 0 になるまで足す。</summary>
        public static long SinRadians(long x)
        {
            var x2 = Round(x * x);
            var term = x;
            var sum = x;
            for (var n = 1; term != 0; n++)
            {
                term = -Round(term * x2) / ((2 * n) * ((2 * n) + 1));
                sum += term;
            }

            return sum;
        }

        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        public static long Clamp(long value, long min, long max) => value < min ? min : value > max ? max : value;
    }
}
