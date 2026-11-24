using System.Numerics;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class GeometryTests
    {
        [Test]
        public void InsideIsStrictAtTheRadius()
        {
            var a = new Vec2i(0, 0);
            var b = new Vec2i(4000, 0);

            // 線分の内側・端の円・長さ 0 の円のそれぞれで、ちょうど半径は外、1 単位内側は内。
            Assert.That(Capsule.Inside(new Vec2i(2000, 640), a, b, 640), Is.False);
            Assert.That(Capsule.Inside(new Vec2i(2000, 639), a, b, 640), Is.True);
            Assert.That(Capsule.Inside(new Vec2i(-640, 0), a, b, 640), Is.False);
            Assert.That(Capsule.Inside(new Vec2i(-639, 0), a, b, 640), Is.True);
            Assert.That(Capsule.Inside(new Vec2i(4384, 512), a, b, 640), Is.False, "3-4-5 の直角三角形でちょうど 640");
            Assert.That(Capsule.Inside(new Vec2i(4384, 511), a, b, 640), Is.True);
            Assert.That(Capsule.Inside(new Vec2i(384, 512), a, a, 640), Is.False);
            Assert.That(Capsule.Inside(new Vec2i(384, 511), a, a, 640), Is.True);
        }

        /// <summary>外積で判定する <see cref="Capsule.Inside"/> を、射影（ピタゴラス）の式の BigInteger による参照実装と照合する。</summary>
        [Test]
        public void InsideMatchesProjectionReference()
        {
            var random = new SeededRandom(35);
            for (var i = 0; i < 20000; i++)
            {
                var a = new Vec2i(random.Range(0, Board.Width), random.Range(0, Board.Height));
                var b = a + new Vec2i(random.Range(-8000, 8000), random.Range(-8000, 8000));
                if (i % 10 == 0) { b = a; }
                var point = a + new Vec2i(random.Range(-12000, 12000), random.Range(-12000, 12000));
                var radius = random.Range(1, 6000);
                Assert.That(Capsule.Inside(point, a, b, radius), Is.EqualTo(ReferenceInside(point, a, b, radius)), $"{point} {a}-{b} r={radius}");
            }
        }

        [Test]
        public void DistanceFloorNeverExceedsTheTrueDistance()
        {
            var random = new SeededRandom(7);
            for (var i = 0; i < 5000; i++)
            {
                var a = new Vec2i(random.Range(0, 20000), random.Range(0, 20000));
                var b = a + new Vec2i(random.Range(-8000, 8000), random.Range(-8000, 8000));
                var point = a + new Vec2i(random.Range(-9000, 9000), random.Range(-9000, 9000));
                var floor = Capsule.DistanceFloor(point, a, b);

                // floor 以下の半径では内側にならず、floor + 1 では内側になる（距離の切り捨てそのもの）。
                Assert.That(Capsule.Inside(point, a, b, (int)floor), Is.False);
                Assert.That(Capsule.Inside(point, a, b, (int)floor + 1), Is.True);
            }
        }

        [Test]
        public void SegmentsWithinDetectsCrossingAndNearMisses()
        {
            var a = new Vec2i(0, 0);
            var b = new Vec2i(1000, 0);
            Assert.That(Capsule.SegmentsWithin(new Vec2i(500, -100), new Vec2i(500, 100), a, b, 1), Is.True, "交わる");
            Assert.That(Capsule.SegmentsWithin(new Vec2i(0, 50), new Vec2i(1000, 50), a, b, 50), Is.False, "平行でちょうど半径");
            Assert.That(Capsule.SegmentsWithin(new Vec2i(0, 49), new Vec2i(1000, 49), a, b, 50), Is.True);
            Assert.That(Capsule.SegmentsWithin(new Vec2i(1030, 40), new Vec2i(1100, 300), a, b, 51), Is.True, "端点どうしが近い（距離 50）");
            Assert.That(Capsule.SegmentsWithin(new Vec2i(1030, 40), new Vec2i(1100, 300), a, b, 50), Is.False);
        }

        [Test]
        public void ClosestIsOnTheSegment()
        {
            var a = new Vec2i(0, 0);
            var b = new Vec2i(1000, 1000);
            Assert.That(Capsule.Closest(new Vec2i(-500, -20), a, b), Is.EqualTo(a));
            Assert.That(Capsule.Closest(new Vec2i(2000, 1200), a, b), Is.EqualTo(b));
            Assert.That(Capsule.Closest(new Vec2i(0, 1000), a, b), Is.EqualTo(new Vec2i(500, 500)));
        }

        private static bool ReferenceInside(Vec2i point, Vec2i a, Vec2i b, int radius)
        {
            BigInteger px = point.X - (long)a.X;
            BigInteger py = point.Y - (long)a.Y;
            BigInteger dx = b.X - (long)a.X;
            BigInteger dy = b.Y - (long)a.Y;
            BigInteger r2 = (long)radius * radius;
            var length2 = (dx * dx) + (dy * dy);
            var ap2 = (px * px) + (py * py);
            if (length2.IsZero) { return ap2 < r2; }
            var dot = (px * dx) + (py * dy);
            if (dot <= 0) { return ap2 < r2; }
            if (dot >= length2)
            {
                var bx = point.X - (long)b.X;
                var by = point.Y - (long)b.Y;
                return (bx * bx) + (by * by) < r2;
            }

            // 距離^2 = |ap|^2 - dot^2 / |ab|^2。両辺に |ab|^2 を掛けて比べる。
            return (ap2 * length2) - (dot * dot) < r2 * length2;
        }
    }
}
