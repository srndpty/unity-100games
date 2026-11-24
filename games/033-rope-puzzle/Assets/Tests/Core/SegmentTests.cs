using System;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class SegmentTests
    {
        private static Vec2i V(int x, int y) => new(x, y);

        [Test]
        public void CrossingSegmentsIntersect()
        {
            Assert.That(Segment.Intersects(V(0, 0), V(10, 10), V(0, 10), V(10, 0)), Is.True);
        }

        [Test]
        public void TouchingAtEndpointIntersects()
        {
            Assert.That(Segment.Intersects(V(0, 0), V(10, 0), V(10, 0), V(10, 10)), Is.True);
            Assert.That(Segment.Intersects(V(0, 0), V(10, 0), V(5, 0), V(5, 10)), Is.True, "端点が相手の途中に乗る");
        }

        [Test]
        public void ParallelAndSeparateDoNotIntersect()
        {
            Assert.That(Segment.Intersects(V(0, 0), V(10, 0), V(0, 1), V(10, 1)), Is.False);
            Assert.That(Segment.Intersects(V(0, 0), V(10, 0), V(11, 0), V(20, 0)), Is.False, "同じ直線上で離れている");
            Assert.That(Segment.Intersects(V(0, 0), V(10, 0), V(5, 1), V(5, 10)), Is.False, "1 だけ届かない");
        }

        [Test]
        public void CollinearOverlapIntersects()
        {
            Assert.That(Segment.Intersects(V(0, 0), V(10, 0), V(5, 0), V(20, 0)), Is.True);
        }

        [Test]
        public void ZeroLengthNeverIntersects()
        {
            Assert.That(Segment.Intersects(V(5, 0), V(5, 0), V(0, 0), V(10, 0)), Is.False, "押しただけの点では切らない");
            Assert.That(Segment.Intersects(V(0, 0), V(10, 0), V(5, 0), V(5, 0)), Is.False);
        }

        [Test]
        public void LargeCoordinatesDoNotOverflow()
        {
            var w = Board.Width;
            var h = Board.Height;
            Assert.That(Segment.Intersects(V(-w, -h), V(2 * w, 2 * h), V(-w, 2 * h), V(2 * w, -h)), Is.True);
            Assert.That(Segment.Intersects(V(-w, -h), V(2 * w, -h), V(-w, -h + 1), V(2 * w, -h + 1)), Is.False);
        }

        /// <summary>素朴な参照実装（交点の媒介変数を分数で求める）と、小さな座標の乱数で照合する。</summary>
        [Test]
        public void MatchesParametricReference()
        {
            var random = new SeededRandom(33);
            for (var i = 0; i < 20000; i++)
            {
                var a0 = V(random.Range(-8, 9), random.Range(-8, 9));
                var a1 = V(random.Range(-8, 9), random.Range(-8, 9));
                var b0 = V(random.Range(-8, 9), random.Range(-8, 9));
                var b1 = V(random.Range(-8, 9), random.Range(-8, 9));
                Assert.That(Segment.Intersects(a0, a1, b0, b1), Is.EqualTo(Reference(a0, a1, b0, b1)), $"{a0}-{a1} と {b0}-{b1}");
            }
        }

        /// <summary>
        /// a0 + t (a1 - a0) = b0 + u (b1 - b0) を解く。平行なら、同じ直線上で a の各点が b に乗るかを細かい刻みで調べる。
        /// </summary>
        private static bool Reference(Vec2i a0, Vec2i a1, Vec2i b0, Vec2i b1)
        {
            if (a0 == a1 || b0 == b1) { return false; }
            long rx = a1.X - a0.X, ry = a1.Y - a0.Y, sx = b1.X - b0.X, sy = b1.Y - b0.Y;
            long qx = b0.X - a0.X, qy = b0.Y - a0.Y;
            var denominator = (rx * sy) - (ry * sx);
            if (denominator != 0)
            {
                var t = (qx * sy) - (qy * sx);
                var u = (qx * ry) - (qy * rx);
                if (denominator < 0) { denominator = -denominator; t = -t; u = -u; }
                return t >= 0 && t <= denominator && u >= 0 && u <= denominator;
            }

            if ((qx * ry) - (qy * rx) != 0) { return false; }

            // 同じ直線上：a 上の点を 1/64 刻みで b に含まれるか調べる（座標が小さいので端点の一致も拾える）。
            for (var k = 0; k <= 64; k++)
            {
                var px = (a0.X * 64) + (rx * k);
                var py = (a0.Y * 64) + (ry * k);
                if (px >= Math.Min(b0.X, b1.X) * 64 && px <= Math.Max(b0.X, b1.X) * 64 && py >= Math.Min(b0.Y, b1.Y) * 64 && py <= Math.Max(b0.Y, b1.Y) * 64) { return true; }
            }

            // b の端点が a に乗る場合（a が長く b が短い）。
            return OnSegment(a0, a1, b0) || OnSegment(a0, a1, b1);
        }

        private static bool OnSegment(Vec2i a, Vec2i b, Vec2i p) =>
            p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) && p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
    }
}
