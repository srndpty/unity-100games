using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>連続衝突検出（経路の点の列と保守的前進）。</summary>
    public sealed class BallPathTests
    {
        /// <summary>保守的前進で飛ばした結果が、全ての点を順に調べる素朴な走査と一致する（相手・点の番号とも）。</summary>
        [Test]
        public void ConservativeAdvanceMatchesNaiveScan()
        {
            var random = new SeededRandom(35);
            var shapes = new Shape[6];
            var scratch = new int[6];
            var hits = 0;
            for (var i = 0; i < 20000; i++)
            {
                var p0 = new Vec2i(random.Range(4000, 12000), random.Range(4000, 12000));
                var p1 = p0 + new Vec2i(random.Range(-600, 600), random.Range(-600, 600));
                var count = random.Range(1, shapes.Length);
                for (var s = 0; s < count; s++)
                {
                    var a = p0 + new Vec2i(random.Range(-2500, 2500), random.Range(-2500, 2500));
                    var b = random.Next(3) == 0 ? a : a + new Vec2i(random.Range(-3000, 3000), random.Range(-3000, 3000));
                    shapes[s] = new Shape(a, b, random.Range(2, 1800), s);
                }

                var found = BallPath.First(p0, p1, shapes, count, scratch, out var step);
                var expected = Naive(p0, p1, shapes, count, out var expectedStep);
                Assert.That(found, Is.EqualTo(expected), $"case {i}");
                if (found >= 0)
                {
                    hits++;
                    Assert.That(step, Is.EqualTo(expectedStep), $"case {i}");
                }
            }

            Assert.That(hits, Is.GreaterThan(2000), "当たる例も十分に含む");
        }

        /// <summary>最大の速さで薄い壁（半径の和 2 単位）に斜めに当てても、点の列は壁を飛び越えない。</summary>
        [Test]
        public void MaxSpeedPathDoesNotSkipAThinWall([Values(0, 1, 7, 45, 89)] int degrees)
        {
            var shapes = new[] { new Shape(new Vec2i(-5000, 0), new Vec2i(5000, 0), 2, 0) };
            var scratch = new int[1];
            var length = new PinballConfig().MaxSpeed / Board.VelocityScale;
            var direction = new Vec2i(Trig.Cos(Trig.Down + (degrees * Trig.FullTurn / 360)), Trig.Sin(Trig.Down + (degrees * Trig.FullTurn / 360)));
            for (var offset = 0; offset < 40; offset++)
            {
                // 壁の少し上から、壁をまたぐ長さの経路。
                var p0 = new Vec2i(offset * 13, 3 + (offset % 5));
                var p1 = p0 + IntMath.Scale(direction, length);
                if (p1.Y > -3) { continue; }
                Assert.That(BallPath.First(p0, p1, shapes, 1, scratch, out _), Is.EqualTo(0), $"{p0}→{p1}");
            }
        }

        [Test]
        public void GrazingDiagonalPassIsDetected()
        {
            // 円（ポスト）の脇を斜めにかすめる：両端の点は外だが、途中の点が内側に入る。
            var post = new[] { new Shape(new Vec2i(0, 0), new Vec2i(0, 0), 600, 0) };
            var scratch = new int[1];
            var p0 = new Vec2i(-700, 1100);
            var p1 = new Vec2i(1100, -700);
            Assert.That(Capsule.Inside(p0, post[0].A, post[0].B, 600), Is.False);
            Assert.That(Capsule.Inside(p1, post[0].A, post[0].B, 600), Is.False);
            Assert.That(BallPath.First(p0, p1, post, 1, scratch, out var step), Is.EqualTo(0));
            Assert.That(step, Is.GreaterThan(0).And.LessThan(BallPath.Steps(p0, p1)));
        }

        [Test]
        public void NearerShapeWinsAndTiesGoToTheEarlierShape()
        {
            var scratch = new int[3];
            var p0 = new Vec2i(0, 0);
            var p1 = new Vec2i(1000, 0);
            var far = new Shape(new Vec2i(900, -500), new Vec2i(900, 500), 10, 0);
            var near = new Shape(new Vec2i(400, -500), new Vec2i(400, 500), 10, 1);
            Assert.That(BallPath.First(p0, p1, new[] { far, near }, 2, scratch, out var step), Is.EqualTo(1));
            Assert.That(step, Is.EqualTo(391));

            var twin = new Shape(new Vec2i(400, -500), new Vec2i(400, 500), 10, 2);
            Assert.That(BallPath.First(p0, p1, new[] { far, twin, near }, 3, scratch, out _), Is.EqualTo(1), "同じ点で入ったら並びの先");
        }

        [Test]
        public void StationaryBallOnlyTouchesWhenAlreadyInside()
        {
            var scratch = new int[1];
            var shape = new[] { new Shape(new Vec2i(0, 0), new Vec2i(100, 0), 50, 0) };
            Assert.That(BallPath.First(new Vec2i(50, 50), new Vec2i(50, 50), shape, 1, scratch, out _), Is.EqualTo(-1));
            Assert.That(BallPath.First(new Vec2i(50, 49), new Vec2i(50, 49), shape, 1, scratch, out var step), Is.EqualTo(0));
            Assert.That(step, Is.EqualTo(0));
        }

        private static int Naive(Vec2i p0, Vec2i p1, Shape[] shapes, int count, out int step)
        {
            var total = BallPath.Steps(p0, p1);
            for (var k = 0; k <= total; k++)
            {
                var point = BallPath.PointAt(p0, p1, k, total);
                for (var s = 0; s < count; s++)
                {
                    if (Capsule.Inside(point, shapes[s].A, shapes[s].B, shapes[s].Reach))
                    {
                        step = k;
                        return s;
                    }
                }
            }

            step = 0;
            return -1;
        }
    }
}
