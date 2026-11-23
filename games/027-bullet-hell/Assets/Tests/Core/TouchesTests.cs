using System.Numerics;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>
    /// 動く 2 つの円の接触（<see cref="VeilRun.Touches"/>）。ちょうど接するだけでは触れない、を座標が大きい（ボスが 1 Tick で遠くへ動く）ときも守る。
    /// テスト側の参照実装は、最も近づく点の距離を外積（ラグランジュの恒等式）で <see cref="BigInteger"/> のまま比べる（本体の 2 次式の判別式とは別の式）。
    /// </summary>
    public sealed class TouchesTests
    {
        /// <summary>レビューの例：x 方向に領域を横切る移動と、257 単位下の静止した点。最も近い距離は半径の和 257 にちょうど等しい。</summary>
        [TestCase(256, false)]
        [TestCase(257, false)]
        [TestCase(258, true)]
        public void LongMove_PassingAtExactlyTheRadius_IsNotContact(int radiusSum, bool hit)
        {
            var aStart = new Vec2i(0, 257);
            var aEnd = new Vec2i(384 * 256, 257);
            var b = new Vec2i(192 * 256, 0);
            Assert.That(VeilRun.Touches(aStart, aEnd, b, b, radiusSum), Is.EqualTo(hit));
            Assert.That(Reference(aStart, aEnd, b, b, radiusSum), Is.EqualTo(hit));
        }

        /// <summary>
        /// 斜めの長い移動の接線・1 単位外側・1 単位内側（相手も動く）。相対移動 d = (60000, 45000)（長さ 75000、向き (4, 3)/5）の中点で、
        /// 相手から法線 (-3, 4)/5 の向きにちょうど 12000 離れる組を作る。半径の和 12000 は接するだけ、12001 で触れる。
        /// </summary>
        [TestCase(0, false)]
        [TestCase(-1, false)]
        [TestCase(1, true)]
        public void LongDiagonal_TangentOutsideAndInside(int extra, bool hit)
        {
            var bStart = new Vec2i(0, 0);
            var bEnd = new Vec2i(-20000, -15000);
            var p0 = new Vec2i(-30000 - 7200, -22500 + 9600);
            var aStart = bStart + p0;
            var aEnd = aStart + new Vec2i(60000, 45000) + (bEnd - bStart);
            var radius = 12000 + extra;
            Assert.That(VeilRun.Touches(aStart, aEnd, bStart, bEnd, radius), Is.EqualTo(hit));
            Assert.That(Reference(aStart, aEnd, bStart, bEnd, radius), Is.EqualTo(hit));
        }

        [Test]
        public void SmallAndLargeCoordinates_AgreeWithTheReference()
        {
            var random = new SeededRandom(27);
            var hits = 0;
            for (var n = 0; n < 20000; n++)
            {
                // 4 回に 1 回は大きな移動（座標が縮めずに long の 2 次式へ入らない範囲）、残りは弾と自機くらいの小さな組。
                var large = n % 4 == 0;
                var reach = large ? 400 * Field.Unit : 24 * Field.Unit;
                var step = large ? 400 * Field.Unit : 6 * Field.Unit;
                var aStart = new Vec2i(random.Range(-reach, reach), random.Range(-reach, reach));
                var aEnd = aStart + new Vec2i(random.Range(-step, step), random.Range(-step, step));
                var bStart = new Vec2i(random.Range(-reach, reach), random.Range(-reach, reach));
                var bEnd = bStart + new Vec2i(random.Range(-step / 8, step / 8), random.Range(-step / 8, step / 8));
                var radius = random.Range(1, large ? 64 * Field.Unit : 12 * Field.Unit);
                var expected = Reference(aStart, aEnd, bStart, bEnd, radius);
                Assert.That(VeilRun.Touches(aStart, aEnd, bStart, bEnd, radius), Is.EqualTo(expected), $"#{n} {aStart}->{aEnd} / {bStart}->{bEnd} r {radius}");
                if (expected) { hits++; }
            }

            Assert.That(hits, Is.GreaterThan(500), "触れる組も十分に含む");
        }

        [Test]
        public void TangentFamily_AtManyScales_IsNeverContact()
        {
            // 横に動く点と、その真下 r の静止点。移動の長さを 1 単位から領域の幅まで変えても、接するだけなら触れず、半径を 1 増やすと触れる。
            for (var length = 1; length <= 400 * Field.Unit; length = (length * 3) + 1)
            {
                foreach (var r in new[] { 1, 255, 257, 4096, 20000 })
                {
                    var aStart = new Vec2i(-length, r);
                    var aEnd = new Vec2i(length, r);
                    Assert.That(VeilRun.Touches(aStart, aEnd, Vec2i.Zero, Vec2i.Zero, r), Is.False, $"長さ {length}・半径 {r}");
                    Assert.That(VeilRun.Touches(aStart, aEnd, Vec2i.Zero, Vec2i.Zero, r + 1), Is.True, $"長さ {length}・半径 {r + 1}");
                }
            }
        }

        [Test]
        public void NonPositiveRadius_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => VeilRun.Touches(Px(0, 0), Px(1, 0), Px(0, 0), Px(0, 0), 0));
        }

        /// <summary>
        /// 参照実装：相対位置 p(t) = p0 + t d の最小距離が半径より小さいか。始め・終わりのどちらかが内側か、
        /// 最も近い時刻が (0, 1) の中にあって、そこでの距離の 2 乗 = (p0 × d)^2 / |d|^2 が R^2 より小さいか。
        /// </summary>
        private static bool Reference(Vec2i aStart, Vec2i aEnd, Vec2i bStart, Vec2i bEnd, int radius)
        {
            BigInteger px = aStart.X - bStart.X;
            BigInteger py = aStart.Y - bStart.Y;
            BigInteger dx = (long)aEnd.X - aStart.X - ((long)bEnd.X - bStart.X);
            BigInteger dy = (long)aEnd.Y - aStart.Y - ((long)bEnd.Y - bStart.Y);
            var r2 = (BigInteger)radius * radius;
            if ((px * px) + (py * py) < r2) { return true; }
            var ex = px + dx;
            var ey = py + dy;
            if ((ex * ex) + (ey * ey) < r2) { return true; }
            var a = (dx * dx) + (dy * dy);
            if (a.IsZero) { return false; }
            var dot = (px * dx) + (py * dy);
            if (dot >= 0 || -dot >= a) { return false; }
            var cross = (px * dy) - (py * dx);
            return cross * cross < r2 * a;
        }
    }
}
