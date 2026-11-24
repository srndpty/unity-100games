using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 1 Tick の水平の経路と、止まっている円（木・カップ）との接触。
    /// 経路は始めの位置 P から移動 D を <see cref="Units.Samples"/> 等分した点の列 p(k) = P + D·k/N（k = 0〜N）とみなし、
    /// 円の中心との距離が半径より小さい（ちょうど接するだけでは触れない）最初の点 k を求める。
    /// 点の間隔は最大でも 2 m / 256 ≈ 8 mm なので、半径 54 mm のカップも 1 Tick で飛び越えない。
    /// <para>
    /// 距離は N 倍した整数で厳密に比べる：|N(P − C) + D k|² &lt; (N r)²。k の 2 次式は下に凸なので、内側の点は連続した区間になる。
    /// 最も近い点を求め、そこまでを二分探索して最初の点を返す。テストは全ての点を順に調べる参照実装と照合する。
    /// </para>
    /// </summary>
    public static class CircleSweep
    {
        /// <summary>
        /// 経路の点 k = 0〜<paramref name="lastSample"/> のうち、円の内側に入る最初の k。入らなければ −1。
        /// </summary>
        public static int First(Vec2i start, Vec2i delta, Vec2i center, int radius, int lastSample)
        {
            if (radius <= 0 || radius > Units.MaxCircleRadius)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), radius, $"半径は 1〜{Units.MaxCircleRadius} にしてください。");
            }

            if (Math.Abs(delta.X) > Units.MaxTickMove || Math.Abs(delta.Y) > Units.MaxTickMove)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), delta, $"1 Tick の移動は各成分 {Units.MaxTickMove} 以下にしてください。");
            }

            if (lastSample < 0 || lastSample > Units.Samples)
            {
                throw new ArgumentOutOfRangeException(nameof(lastSample), lastSample, $"点の番号は 0〜{Units.Samples} にしてください。");
            }

            // 各軸で、半径と移動を足しても届かない組は計算しない（以後の積を long に収める）。
            long px = (long)start.X - center.X;
            long py = (long)start.Y - center.Y;
            if (Math.Abs(px) > radius + Math.Abs((long)delta.X) || Math.Abs(py) > radius + Math.Abs((long)delta.Y)) { return -1; }

            var ax = px * Units.Samples;
            var ay = py * Units.Samples;
            var r2 = (long)radius * Units.Samples * radius * Units.Samples;
            if (Inside(ax, ay, delta, 0, r2)) { return 0; }

            // 最も近づく実数の k* = −(A·D)/|D|²。0 以下なら遠ざかる一方。
            var a = delta.LengthSquared;
            if (a == 0) { return -1; }
            var dot = (ax * delta.X) + (ay * delta.Y);
            if (dot >= 0) { return -1; }
            var floor = -dot / a;
            var nearest = floor >= lastSample ? lastSample : Closer(ax, ay, delta, (int)floor);
            if (!Inside(ax, ay, delta, nearest, r2)) { return -1; }

            // [0, nearest] では内側かどうかが「外…外、内…内」と並ぶ（凸な 2 次式の負の区間が nearest を含む）。
            var low = 0;
            var high = nearest;
            while (high - low > 1)
            {
                var mid = (low + high) / 2;
                if (Inside(ax, ay, delta, mid, r2)) { high = mid; }
                else { low = mid; }
            }

            return high;
        }

        /// <summary>点 k が円の内側か（N 倍した座標で比べる）。</summary>
        private static bool Inside(long ax, long ay, Vec2i delta, int k, long r2) => Distance(ax, ay, delta, k) < r2;

        /// <summary>経路の点 k の位置（N 等分の端数は 0 方向へ切り捨て）。</summary>
        public static Vec2i Point(Vec2i start, Vec2i delta, int k) =>
            new(start.X + (int)((long)delta.X * k / Units.Samples), start.Y + (int)((long)delta.Y * k / Units.Samples));

        /// <summary>k と k+1 のうち円の中心に近いほう。</summary>
        private static int Closer(long ax, long ay, Vec2i delta, int k)
        {
            var here = Distance(ax, ay, delta, k);
            var next = Distance(ax, ay, delta, k + 1);
            return next < here ? k + 1 : k;
        }

        private static long Distance(long ax, long ay, Vec2i delta, int k)
        {
            var x = ax + ((long)delta.X * k);
            var y = ay + ((long)delta.Y * k);
            return (x * x) + (y * y);
        }
    }
}
