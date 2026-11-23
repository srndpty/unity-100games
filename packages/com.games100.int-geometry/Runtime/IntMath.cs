using System;

namespace IntGeometry
{
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
}
