using System;

namespace Game.Core
{
    /// <summary>
    /// 整数の角度と三角関数。1 周 = <see cref="FullTurn"/>（0 は右向き、反時計回りに増える。下向きは <see cref="Down"/>）。
    /// <para>
    /// sin / cos は 1 周を <see cref="TableSize"/> 分割した表を引き、値は Q14（1.0 = <see cref="One"/>）。
    /// 表は起動時に整数だけのテイラー展開（Q30）で作るので、<c>Math.Sin</c> の実装差に依らず Mono と .NET で同じ値になる。
    /// </para>
    /// </summary>
    public static class Trig
    {
        /// <summary>1 周の角度の単位数。</summary>
        public const int FullTurn = 65536;

        public const int Right = 0;

        public const int Up = FullTurn / 4;

        public const int Left = FullTurn / 2;

        public const int Down = FullTurn * 3 / 4;

        /// <summary>1 周を何分割した表を引くか。</summary>
        public const int TableSize = 4096;

        /// <summary>sin / cos の 1.0。</summary>
        public const int One = 1 << 14;

        /// <summary>角度の単位から表の添字へのシフト量（65536 / 4096 = 16 = 2^4）。</summary>
        public const int IndexShift = 4;

        private const int Quarter = TableSize / 4;

        private const int Q30 = 30;

        /// <summary>π/2 を 2^30 倍して四捨五入した値。</summary>
        private const long HalfPiQ30 = 1686629713L;

        /// <summary>0〜π/2 の sin（Q14）。添字 0〜<see cref="Quarter"/>。</summary>
        private static readonly int[] _quarterSine = BuildQuarterSine();

        /// <summary>角度を 0 以上 <see cref="FullTurn"/> 未満へ直す。</summary>
        public static int Normalize(int angle) => angle & (FullTurn - 1);

        /// <summary>表の添字（0〜<see cref="TableSize"/>-1）の sin（Q14）。</summary>
        public static int SinIndex(int index)
        {
            index &= TableSize - 1;
            if (index < Quarter) { return _quarterSine[index]; }
            if (index < Quarter * 2) { return _quarterSine[(Quarter * 2) - index]; }
            if (index < Quarter * 3) { return -_quarterSine[index - (Quarter * 2)]; }
            return -_quarterSine[TableSize - index];
        }

        /// <summary>角度の sin（Q14）。表の刻み（角度 16 単位）の下の値を使う。</summary>
        public static int Sin(int angle) => SinIndex(Normalize(angle) >> IndexShift);

        /// <summary>角度の cos（Q14）。</summary>
        public static int Cos(int angle) => SinIndex((Normalize(angle) >> IndexShift) + Quarter);

        /// <summary>
        /// ベクトル (x, y) の角度。表の刻み（角度 16 単位）に丸め、その刻みの向きのうち (x, y) に最も近いものを返す。
        /// (0, 0) では向きが決まらないので <paramref name="fallback"/> を返す。
        /// </summary>
        public static int Atan2(long y, long x, int fallback)
        {
            if (x == 0 && y == 0) { return Normalize(fallback); }

            // 第 1 象限（0〜π/2）へ写してから表を二分探索し、元の象限へ戻す。
            var ax = Math.Abs(x);
            var ay = Math.Abs(y);
            var index = QuarterIndex(ay, ax);
            int full;
            if (x >= 0 && y >= 0) { full = index; }
            else if (x < 0 && y >= 0) { full = (Quarter * 2) - index; }
            else if (x < 0) { full = (Quarter * 2) + index; }
            else { full = TableSize - index; }

            return Normalize((full & (TableSize - 1)) << IndexShift);
        }

        /// <summary>第 1 象限の (x, y)（ともに 0 以上、両方 0 ではない）に最も近い表の添字 0〜<see cref="Quarter"/>。</summary>
        private static int QuarterIndex(long y, long x)
        {
            // 大きい値は桁あふれしないよう縮める（向きはほぼ保たれる）。
            while (x > (1L << 40) || y > (1L << 40))
            {
                x >>= 1;
                y >>= 1;
            }

            // f(k) = x sin(k) - y cos(k) は k について単調に増える。f(k) <= 0 となる最大の k を探す。
            var low = 0;
            var high = Quarter;
            while (low < high)
            {
                var mid = (low + high + 1) / 2;
                if (Cross(x, y, mid) <= 0) { low = mid; }
                else { high = mid - 1; }
            }

            if (low == Quarter) { return low; }

            // low と low+1 のうち、向きの差（外積の大きさ）が小さいほう。
            var below = -Cross(x, y, low);
            var above = Cross(x, y, low + 1);
            return above < below ? low + 1 : low;
        }

        private static long Cross(long x, long y, int index) => (x * _quarterSine[index]) - (y * _quarterSine[Quarter - index]);

        private static int[] BuildQuarterSine()
        {
            var table = new int[Quarter + 1];
            for (var k = 0; k <= Quarter; k++)
            {
                // x = k / Quarter * π/2（Q30）。sin x = x - x^3/3! + x^5/5! - …（項が 0 になるまで）。
                var x = HalfPiQ30 * k / Quarter;
                var x2 = (x * x) >> Q30;
                var term = x;
                var sum = x;
                for (var n = 1; term != 0; n++)
                {
                    term = -(term * x2 >> Q30) / ((2 * n) * ((2 * n) + 1));
                    sum += term;
                }

                // Q30 → Q14 の四捨五入。
                table[k] = (int)((sum + (1L << (Q30 - 15))) >> (Q30 - 14));
            }

            return table;
        }
    }
}
