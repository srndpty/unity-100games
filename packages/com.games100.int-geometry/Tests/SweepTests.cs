using System;
using System.Linq;
using NUnit.Framework;

namespace IntGeometry.Tests
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

            // 境界：最大値（(x + 1) / 2 の初期値は桁あふれする。028 P3-1）、小さな値、平方数の前後。
            Assert.That(IntMath.Sqrt(long.MaxValue), Is.EqualTo(3_037_000_499L));
            Assert.That(IntMath.SqrtCeiling(long.MaxValue), Is.EqualTo(3_037_000_500L));
            Assert.That(IntMath.Sqrt(long.MaxValue - 1), Is.EqualTo(3_037_000_499L));
            Assert.That(IntMath.Sqrt(3_037_000_499L * 3_037_000_499L), Is.EqualTo(3_037_000_499L));
            Assert.That(IntMath.Sqrt((3_037_000_499L * 3_037_000_499L) - 1), Is.EqualTo(3_037_000_498L));
            for (long v = 0; v < 2000; v++)
            {
                var root = IntMath.Sqrt(v);
                Assert.That(root * root <= v && (root + 1) * (root + 1) > v, Is.True, $"Sqrt({v}) = {root}");
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
            Assert.That(IntMath.Scale(At(1, 1), 96), Is.EqualTo(At(68, 68)), "短い向きでも長さ 96 のまま（96/√2 = 67.88）");
            Assert.That(IntMath.Scale(At(int.MaxValue, int.MinValue + 1), 96), Is.EqualTo(At(68, -68)), "大きすぎる向きも桁あふれしない");
        }

        /// <summary>
        /// 向きの長さにかかわらず、結果は指定の長さで向きも保つ（照準点が自機のすぐ近くでも弾速が変わらない）。
        /// 各成分の誤差は四捨五入の 0.5 と、2^10 倍した長さの平方根の切り捨ての分（長さ 256 でも 0.2 未満）以内。
        /// </summary>
        [TestCase(1)]
        [TestCase(4)]
        [TestCase(300)]
        [TestCase(20000)]
        public void Scale_LengthDoesNotDependOnDirectionMagnitude(int range)
        {
            var random = new SeededRandom(range);
            for (var i = 0; i < 2000; i++)
            {
                var direction = At(random.Range(-range, range), random.Range(-range, range));
                if (direction.IsZero) { continue; }
                foreach (var length in new[] { 1, 32, 96, 256 })
                {
                    var scaled = IntMath.Scale(direction, length);
                    var actual = Math.Sqrt(scaled.LengthSquared);
                    Assert.That(actual, Is.EqualTo(length).Within(0.75), $"{direction} → {scaled}");
                    var expectedX = direction.X * length / Math.Sqrt(direction.LengthSquared);
                    var expectedY = direction.Y * length / Math.Sqrt(direction.LengthSquared);
                    Assert.That(scaled.X, Is.EqualTo(expectedX).Within(0.75), $"{direction} → {scaled}");
                    Assert.That(scaled.Y, Is.EqualTo(expectedY).Within(0.75), $"{direction} → {scaled}");
                }
            }
        }

        [Test]
        public void First_FarApartPairs_AreRejectedWithoutTheLimit()
        {
            // 遠い組は値が上限を超えていても計算せずに「触れない」。
            Assert.That(Sweep.First(At(0, 0), At(100, 0), At(int.MaxValue / 2, 0), At(int.MaxValue / 2, 0), 50).Hit, Is.False);
            Assert.That(Sweep.First(At(-1_000_000, 0), At(-999_000, 0), At(1_000_000, 0), At(1_000_000, 0), 1).Hit, Is.False);
        }

        [Test]
        public void First_NearPairBeyondTheLimit_Throws()
        {
            // 近い組で 1 Tick の移動が上限を超えるなら、桁あふれさせずに例外にする（大きな移動は Touches を使う）。
            var far = Sweep.FirstLimit + 1;
            Assert.Throws<ArgumentOutOfRangeException>(() => Sweep.First(At(0, 0), At(far, 0), At(far / 2, 0), At(far / 2, 0), 10));
            Assert.That(Sweep.Touches(At(0, 0), At(far, 0), At(far / 2, 0), At(far / 2, 0), 10), Is.True);
            Assert.That(Sweep.First(At(0, 0), At(Sweep.FirstLimit, 0), At(100, 0), At(100, 0), 10).Hit, Is.True);
        }

        [Test]
        public void NonPositiveRadius_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Sweep.First(At(0, 0), At(1, 0), At(0, 0), At(0, 0), 0));
        }

        [Test]
        public void Rect_ContainsAndOverlaps()
        {
            var rect = new Rect(0, 0, 100, 50);
            Assert.That(rect.Contains(At(100, 50)), Is.True, "両端を含む");
            Assert.That(rect.Contains(At(101, 0)), Is.False);
            Assert.That(rect.Overlaps(At(110, 25), 11), Is.True);
            Assert.That(rect.Overlaps(At(110, 25), 10), Is.False, "ちょうど接するだけでは重ならない");
            Assert.Throws<ArgumentException>(() => _ = new Rect(10, 0, 9, 0));
        }

        private static Vec2i At(int x, int y) => new(x, y);
    }
}
