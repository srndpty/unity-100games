using System;
using System.Linq;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>円の掃引と整数の計算。弾が速くても敵をすり抜けない。</summary>
    public sealed class SweepTests
    {
        [Test]
        public void FastMover_HitsTargetBetweenEndpoints()
        {
            var touch = Sweep.First(At(0, 0), At(1000, 0), At(500, 0), At(500, 0), 50);
            Assert.That(touch.Hit, Is.True);
            Assert.That((double)touch.Numerator / touch.Denominator, Is.EqualTo(0.45).Within(0.001));
        }

        [TestCase(71, true)]
        [TestCase(70, false)]
        public void DiagonalGraze_IsDetectedOnlyWithinRadius(int radiusSum, bool hit)
        {
            // (100,0) から直線 y = x までの距離は 70.71…。端点ではどちらも 100 離れている。
            var touch = Sweep.First(At(0, 0), At(100, 100), At(100, 0), At(100, 0), radiusSum);
            Assert.That(touch.Hit, Is.EqualTo(hit));
        }

        [Test]
        public void ExactlyTouching_IsNotContact()
        {
            Assert.That(Sweep.First(At(0, -100), At(0, 100), At(50, 0), At(50, 0), 50).Hit, Is.False);
            Assert.That(Sweep.First(At(0, -100), At(0, 100), At(50, 0), At(50, 0), 51).Hit, Is.True);
        }

        [TestCase(100, false)]
        [TestCase(101, true)]
        public void StoppingShort_HitsOnlyIfEndOverlaps(int radiusSum, bool hit)
        {
            Assert.That(Sweep.First(At(0, 0), At(100, 0), At(200, 0), At(200, 0), radiusSum).Hit, Is.EqualTo(hit));
        }

        [Test]
        public void StartingOverlap_TouchesAtZero_EvenWhenSeparating()
        {
            var touch = Sweep.First(At(0, 0), At(-500, 0), At(10, 0), At(500, 0), 20);
            Assert.That(touch.Hit, Is.True);
            Assert.That(touch.Numerator, Is.Zero);
        }

        [Test]
        public void BothMoving_UseRelativePath()
        {
            // 両者とも端点では離れているが、すれ違う途中で重なる。
            Assert.That(Sweep.First(At(0, 0), At(200, 0), At(200, 10), At(0, 10), 20).Hit, Is.True);

            // 同じ向きに同じ速さで動けば、相対的には止まっている。
            Assert.That(Sweep.First(At(0, 0), At(200, 0), At(100, 0), At(300, 0), 20).Hit, Is.False);
        }

        [Test]
        public void EarlierTouch_IsOrderedFirst()
        {
            var near = Sweep.First(At(0, 0), At(1000, 0), At(300, 0), At(300, 0), 20);
            var far = Sweep.First(At(0, 0), At(1000, 0), At(700, 0), At(700, 0), 20);
            Assert.That(near.IsEarlierThan(far), Is.True);
            Assert.That(far.IsEarlierThan(near), Is.False);
            Assert.That(Touch.None.IsEarlierThan(near), Is.False);
        }

        /// <summary>整数の判定を、細かい刻みの素朴な判定と乱数の組で照合する（刻みの間のかすりは見逃しうるので、素朴な判定が当たれば必ず当たる、を見る）。</summary>
        [Test]
        public void Sweep_AgreesWithFineSampling()
        {
            var random = new SeededRandom(11);
            for (var i = 0; i < 2000; i++)
            {
                Vec2i Point() => At(random.Range(-600, 600), random.Range(-600, 600));
                var a0 = Point();
                var a1 = a0 + At(random.Range(-256, 256), random.Range(-256, 256));
                var b0 = Point();
                var b1 = b0 + At(random.Range(-256, 256), random.Range(-256, 256));
                var r = random.Range(1, 300);
                var sampled = Enumerable.Range(0, 1001).Any(k =>
                {
                    var t = k / 1000.0;
                    var dx = (a0.X - b0.X) + (t * ((a1.X - a0.X) - (b1.X - b0.X)));
                    var dy = (a0.Y - b0.Y) + (t * ((a1.Y - a0.Y) - (b1.Y - b0.Y)));
                    return (dx * dx) + (dy * dy) < ((double)r * r) - 1e-6;
                });
                var touch = Sweep.First(a0, a1, b0, b1, r);
                if (sampled) { Assert.That(touch.Hit, Is.True, $"{a0}→{a1} / {b0}→{b1} r={r}"); }
                if (touch.Hit) { Assert.That(touch.Numerator, Is.LessThanOrEqualTo(touch.Denominator)); }
            }
        }

        [Test]
        public void Sqrt_IsFloorForManyValues()
        {
            var random = new SeededRandom(3);
            for (var i = 0; i < 5000; i++)
            {
                long value = random.NextUInt();
                value *= random.Range(1, 1000);
                var root = IntMath.Sqrt(value);
                Assert.That(root * root, Is.LessThanOrEqualTo(value));
                Assert.That((root + 1) * (root + 1), Is.GreaterThan(value));
            }

            Assert.That(IntMath.Sqrt(0), Is.Zero);
            Assert.That(IntMath.SqrtCeiling(17), Is.EqualTo(5));
            Assert.That(IntMath.SqrtCeiling(16), Is.EqualTo(4));
            Assert.That(() => IntMath.Sqrt(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Scale_KeepsDirectionAndLength()
        {
            Assert.That(IntMath.Scale(At(3, 4), 100), Is.EqualTo(At(60, 80)));
            Assert.That(IntMath.Scale(At(-3, -4), 100), Is.EqualTo(At(-60, -80)));
            Assert.That(IntMath.Scale(At(1, 0), 1), Is.EqualTo(At(1, 0)));
            Assert.That(IntMath.Scale(Vec2i.Zero, 100), Is.EqualTo(Vec2i.Zero));
        }

        [Test]
        public void View_FollowsShipAndClampsAtArenaEdge()
        {
            var config = Config();
            var center = Center(config);
            var view = View.Of(center, config.ArenaWidth, config.ArenaHeight, config.ViewWidth, config.ViewHeight);
            Assert.That(view.MinX + (view.Width / 2), Is.EqualTo(center.X));
            Assert.That(view.Width, Is.EqualTo(config.ViewWidth));

            var corner = View.Of(At(0, 0), config.ArenaWidth, config.ArenaHeight, config.ViewWidth, config.ViewHeight);
            Assert.That(corner, Is.EqualTo(new Rect(0, 0, config.ViewWidth, config.ViewHeight)));
            var far = View.Of(At(config.ArenaWidth, config.ArenaHeight), config.ArenaWidth, config.ArenaHeight, config.ViewWidth, config.ViewHeight);
            Assert.That(far.MaxX, Is.EqualTo(config.ArenaWidth));
            Assert.That(far.MaxY, Is.EqualTo(config.ArenaHeight));
        }
    }
}
