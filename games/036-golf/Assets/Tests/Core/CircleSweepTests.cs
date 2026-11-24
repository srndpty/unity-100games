using System;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class CircleSweepTests
    {
        /// <summary>参照実装：全ての点を順に調べる。</summary>
        private static int Scan(Vec2i start, Vec2i delta, Vec2i center, int radius, int last)
        {
            for (var k = 0; k <= last; k++)
            {
                var x = ((long)start.X * Units.Samples) + ((long)delta.X * k) - ((long)center.X * Units.Samples);
                var y = ((long)start.Y * Units.Samples) + ((long)delta.Y * k) - ((long)center.Y * Units.Samples);
                var r = (long)radius * Units.Samples;
                if ((decimal)x * x + ((decimal)y * y) < (decimal)r * r) { return k; }
            }

            return -1;
        }

        [Test]
        public void MatchesFullScanOnRandomPaths()
        {
            var random = new SeededRandom(36);
            for (var i = 0; i < 20000; i++)
            {
                var radius = random.Range(1, Units.MaxCircleRadius);
                if (i % 3 == 0) { radius = random.Range(1000, 100_000); }
                var center = new Vec2i(random.Range(-400_000_000, 400_000_000), random.Range(-100_000_000, 100_000_000));
                var reach = radius + Units.MaxTickMove;
                var start = center + new Vec2i(random.Range(-reach, reach), random.Range(-reach, reach));
                var delta = new Vec2i(random.Range(-Units.MaxTickMove, Units.MaxTickMove), random.Range(-Units.MaxTickMove, Units.MaxTickMove));
                if (i % 5 == 0) { delta = new Vec2i(random.Range(-200, 200), random.Range(-200, 200)); }
                var last = i % 7 == 0 ? random.Range(0, Units.Samples) : Units.Samples;
                Assert.That(CircleSweep.First(start, delta, center, radius, last), Is.EqualTo(Scan(start, delta, center, radius, last)), $"例 {i}");
            }
        }

        [Test]
        public void TouchingExactlyAtTheRadiusIsOutside()
        {
            // 半径 100 の円の真横を、中心からちょうど 100 離れて通る。
            var hit = CircleSweep.First(new Vec2i(-1000, 100), new Vec2i(2000, 0), Vec2i.Zero, 100, Units.Samples);
            Assert.That(hit, Is.EqualTo(-1));
            Assert.That(CircleSweep.First(new Vec2i(-1000, 99), new Vec2i(2000, 0), Vec2i.Zero, 100, Units.Samples), Is.GreaterThan(0));
        }

        [Test]
        public void StartInsideReturnsZeroAndMovingAwayNeverTouches()
        {
            Assert.That(CircleSweep.First(new Vec2i(10, 0), new Vec2i(5000, 0), Vec2i.Zero, 100, Units.Samples), Is.EqualTo(0));
            Assert.That(CircleSweep.First(new Vec2i(200, 0), new Vec2i(5000, 0), Vec2i.Zero, 100, Units.Samples), Is.EqualTo(-1));
            Assert.That(CircleSweep.First(new Vec2i(200, 0), Vec2i.Zero, Vec2i.Zero, 100, Units.Samples), Is.EqualTo(-1));
        }

        [Test]
        public void FastPathAcrossTheCupIsFoundBetweenEndpoints()
        {
            // 1 Tick で 2 m 進む線が、半径 54 mm のカップの真ん中を通る（始めも終わりも円の外）。
            var start = new Vec2i(-1_000_000, 0);
            var hit = CircleSweep.First(start, new Vec2i(Units.MaxTickMove, 0), Vec2i.Zero, 54_000, Units.Samples);
            Assert.That(hit, Is.InRange(1, Units.Samples - 1));
            Assert.That(Vec2i.DistanceSquared(CircleSweep.Point(start, new Vec2i(Units.MaxTickMove, 0), hit), Vec2i.Zero), Is.LessThan(54_000L * 54_000));
        }

        [Test]
        public void RejectsValuesOutsideTheLimits()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CircleSweep.First(Vec2i.Zero, new Vec2i(Units.MaxTickMove + 1, 0), new Vec2i(10, 0), 5, Units.Samples));
            Assert.Throws<ArgumentOutOfRangeException>(() => CircleSweep.First(Vec2i.Zero, Vec2i.Zero, Vec2i.Zero, Units.MaxCircleRadius + 1, Units.Samples));
            Assert.Throws<ArgumentOutOfRangeException>(() => CircleSweep.First(Vec2i.Zero, Vec2i.Zero, Vec2i.Zero, 0, Units.Samples));
            Assert.Throws<ArgumentOutOfRangeException>(() => CircleSweep.First(Vec2i.Zero, Vec2i.Zero, Vec2i.Zero, 5, Units.Samples + 1));
        }
    }
}
