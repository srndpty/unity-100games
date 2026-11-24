using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class WallTests
    {
        [Test]
        public void HeadOnStopsAtTheRadiusAndKeepsTheTangent()
        {
            var wall = new WallSeg(new Vec2i(-4000, 0), new Vec2i(4000, 0), new Vec2i(0, Trig.One));
            var walls = new[] { wall };
            var start = new Vec2i(0, 2000);
            var delta = new Vec2i(0, -3000);
            var hit = WallContact.First(start, delta, walls, 450);
            Assert.That(hit.IsHit, Is.True);
            var stopped = start + new Vec2i(
                (int)(((long)delta.X * hit.Numerator) / hit.Denominator),
                (int)(((long)delta.Y * hit.Numerator) / hit.Denominator));
            var pushed = WallContact.PushOut(stopped, wall, 450);
            Assert.That(pushed.Y, Is.EqualTo(450).Within(2));
            var slide = WallContact.Slide(new Vec2i(800, -500), wall.Inward);
            Assert.That(slide.X, Is.EqualTo(800));
            Assert.That(slide.Y, Is.EqualTo(0).Within(1));
        }

        [Test]
        public void ExactTouchIsNotAHit()
        {
            var wall = new WallSeg(new Vec2i(-1000, 0), new Vec2i(1000, 0), new Vec2i(0, Trig.One));
            var point = new Vec2i(0, 450);
            Assert.That(WallContact.CloserThan(point, wall.A, wall.B, 450), Is.False);
            Assert.That(WallContact.CloserThan(new Vec2i(0, 449), wall.A, wall.B, 450), Is.True);
        }

        [Test]
        public void FastMoveDoesNotTunnelAThinWall()
        {
            var wall = new WallSeg(new Vec2i(-2000, 0), new Vec2i(2000, 0), new Vec2i(0, Trig.One));
            var start = new Vec2i(-300, 800);
            var delta = new Vec2i(2500, -1600);
            var hit = WallContact.First(start, delta, new[] { wall }, 450);
            var reference = SampledHit(start, delta, wall, 450, 8);
            Assert.That(hit.IsHit, Is.EqualTo(reference));
        }

        [Test]
        public void PushingOutOfTheLeftWallMovesTowardTheCenter()
        {
            var track = TestKit.Circuit();
            var outside = track.Left(0);
            var inward = track.Walls[0].Inward;
            var buried = outside - IntMath.Scale(inward, 200);
            var pushed = WallContact.PushOut(buried, track.Walls[0], 450);
            var before = (buried - track.StationPosition(0)).LengthSquared;
            var after = (pushed - track.StationPosition(0)).LengthSquared;
            Assert.That(after, Is.LessThan(before));
        }

        [Test]
        public void DrivingTheCenterlineForALapDoesNotEnterAWall()
        {
            var config = new KartConfig { DriftCorner = 20000 };
            var race = new Race(config, TestKit.Circuit());
            var guard = 0;
            while (race.KartAt(0).Laps < 1 && guard < config.MaxRaceTicks)
            {
                race.Step(CpuPlan.Intent(race.KartAt(0), race.Track, config, 1000));
                var kart = race.KartAt(0);
                for (var w = 0; w < race.Track.Walls.Length; w++)
                {
                    Assert.That(
                        WallContact.CloserThan(kart.Position, race.Track.Walls[w].A, race.Track.Walls[w].B, config.Radius),
                        Is.False,
                        $"Tick {race.Tick} で壁 {w} に食い込んでいます。");
                }

                guard++;
            }

            Assert.That(race.KartAt(0).Laps, Is.GreaterThanOrEqualTo(1), "中心線の計画で 1 周できない");
        }

        private static bool SampledHit(Vec2i start, Vec2i delta, WallSeg wall, int radius, int subdivisions)
        {
            var samples = System.Math.Max(System.Math.Abs(delta.X), System.Math.Abs(delta.Y)) * subdivisions;
            for (var k = 0; k <= samples; k++)
            {
                var point = new Vec2i(
                    start.X + (int)(((long)delta.X * k) / samples),
                    start.Y + (int)(((long)delta.Y * k) / samples));
                if (WallContact.CloserThan(point, wall.A, wall.B, radius)) { return true; }
            }

            return false;
        }
    }
}
