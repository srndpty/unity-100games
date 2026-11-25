using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 車同士の接触。全車の 1 Tick の移動（壁に当たる前）を受け取り、Tick の中で最初に触れた組から順に解決する。
    /// <list type="bullet">
    /// <item>Tick の中で両者は始めの位置から移動の終わりへ直線で動くとみなし、相対軌跡を <see cref="Sweep.First"/> で掃引する。
    /// 中心の距離が直径より小さくなったら触れたとする（ちょうど接するだけでは触れない）。</item>
    /// <item>近づいているときだけ、等しい質量として法線方向の速度を <see cref="KartConfig.BumpBounce"/> の反発で分け合う。
    /// 各車の新しい速さは、新しい速度の向きへの成分（向きは変えない）。</item>
    /// <item>触れた時刻の位置から、直径より 1 mm 以上離れるまで中心を結ぶ向きに押し離し、Tick の残りの時間は新しい速度で進む
    /// （残りを捨てると、くっついて走る 2 台が毎 Tick ほとんど進めなくなる）。新しい速度の大きさは <see cref="KartConfig.BoostSpeed"/> までにする。</item>
    /// <item>1 つの組は 1 Tick に 1 回まで。押し離した先が壁なら、その後の壁の解決が押し戻す。</item>
    /// </list>
    /// 1 台の移動は 1 回の接触で「前の移動と BoostSpeed の大きい方 + 半径 + 3」までしか伸びず、相手は 3 台までなので、
    /// 掃引の値は各成分 2 × (BoostSpeed + 3 × (Radius + 3)) + 2 × Radius 以下に収まる（<see cref="KartConfig.Validate"/> が
    /// <see cref="Sweep.FirstLimit"/> 以下を保証する）。
    /// </summary>
    public static class KartContact
    {
        /// <summary>
        /// <paramref name="active"/> のビットが立った車どうしの接触を解決し、<paramref name="deltas"/> と速さを書き換える。
        /// 触れた車のビットを返す。
        /// </summary>
        public static int Resolve(Kart[] karts, Vec2i[] deltas, int active, KartConfig config)
        {
            if (karts == null) { throw new ArgumentNullException(nameof(karts)); }
            if (deltas == null) { throw new ArgumentNullException(nameof(deltas)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (deltas.Length != karts.Length) { throw new ArgumentException("移動と車の数が違います。", nameof(deltas)); }
            if (karts.Length > KartConfig.KartCount) { throw new ArgumentException("車は 4 台までです。", nameof(karts)); }

            var diameter = config.Radius * 2;
            var count = karts.Length;
            var bumped = 0;
            var resolved = 0;
            for (var pass = 0; pass < count * (count - 1) / 2; pass++)
            {
                var first = -1;
                var second = -1;
                var best = Touch.None;
                for (var i = 0; i < count; i++)
                {
                    if ((active & (1 << i)) == 0) { continue; }
                    for (var j = i + 1; j < count; j++)
                    {
                        if ((active & (1 << j)) == 0 || (resolved & PairBit(i, j, count)) != 0) { continue; }
                        var touch = Sweep.First(karts[i].Position, karts[i].Position + deltas[i], karts[j].Position, karts[j].Position + deltas[j], diameter);
                        if (!touch.IsEarlierThan(best)) { continue; }
                        best = touch;
                        first = i;
                        second = j;
                    }
                }

                if (first < 0) { break; }
                resolved |= PairBit(first, second, count);
                bumped |= (1 << first) | (1 << second);
                Collide(ref karts[first], ref karts[second], ref deltas[first], ref deltas[second], best, config);
            }

            return bumped;
        }

        private static void Collide(ref Kart a, ref Kart b, ref Vec2i deltaA, ref Vec2i deltaB, Touch touch, KartConfig config)
        {
            var atA = a.Position + Before(deltaA, touch);
            var atB = b.Position + Before(deltaB, touch);
            var normal = atB - atA;
            if (normal.IsZero) { normal = Forward(a.Heading); }
            var unit = IntMath.Scale(normal, Trig.One);

            var velocityA = deltaA;
            var velocityB = deltaB;
            var approach = Vec2i.Dot(deltaA - deltaB, unit) / Trig.One;
            if (approach > 0)
            {
                var impulse = approach * (1000 + config.BumpBounce) / 2000;
                var push = new Vec2i((int)((unit.X * impulse) / Trig.One), (int)((unit.Y * impulse) / Trig.One));
                velocityA = Cap(deltaA - push, config.BoostSpeed);
                velocityB = Cap(deltaB + push, config.BoostSpeed);
                a.Speed = SpeedAlong(velocityA, a.Heading, config);
                b.Speed = SpeedAlong(velocityB, b.Heading, config);
            }

            var distance = (int)IntMath.Sqrt(normal.LengthSquared);
            var overlap = (config.Radius * 2) + 1 - distance;
            if (overlap > 0)
            {
                var half = (overlap / 2) + 2;
                atA -= IntMath.Scale(normal, half);
                atB += IntMath.Scale(normal, half);
            }

            deltaA = atA + After(velocityA, touch) - a.Position;
            deltaB = atB + After(velocityB, touch) - b.Position;
        }

        /// <summary>触れた時刻までに進む分。</summary>
        private static Vec2i Before(Vec2i delta, Touch touch) => new(
            (int)((delta.X * touch.Numerator) / touch.Denominator),
            (int)((delta.Y * touch.Numerator) / touch.Denominator));

        /// <summary>触れた時刻から Tick の終わりまでに進む分。</summary>
        private static Vec2i After(Vec2i velocity, Touch touch)
        {
            var rest = touch.Denominator - touch.Numerator;
            return new Vec2i((int)((velocity.X * rest) / touch.Denominator), (int)((velocity.Y * rest) / touch.Denominator));
        }

        private static Vec2i Cap(Vec2i velocity, int limit) =>
            velocity.LengthSquared > (long)limit * limit ? IntMath.Scale(velocity, limit) : velocity;

        private static int SpeedAlong(Vec2i velocity, int heading, KartConfig config)
        {
            var speed = (int)(Vec2i.Dot(velocity, Forward(heading)) / Trig.One);
            return IntMath.Clamp(speed, -config.ReverseSpeed, config.BoostSpeed);
        }

        private static Vec2i Forward(int heading) => new(Trig.Cos(heading), Trig.Sin(heading));

        private static int PairBit(int i, int j, int count) => 1 << ((i * count) + j);
    }
}
