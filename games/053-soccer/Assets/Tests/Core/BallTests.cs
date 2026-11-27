using System.Linq;
using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class BallTests
    {
        private static SoccerConfig C => Lab.Exact;

        private static Ball Rolling(Vec2i at, Vec2i velocity, int height = 0, int vz = 0)
        {
            var b = new Ball();
            b.Place(at, height);
            b.Launch(velocity, vz);
            return b;
        }

        [Test]
        public void RollingBall_SlowsDown_AndStops()
        {
            var b = Rolling(Vec2i.Zero, new Vec2i(300, 0));
            var last = 300;
            for (int i = 0; i < 2000 && !b.IsStopped; i++)
            {
                b.Step(C);
                Assert.That(b.Velocity.X, Is.LessThanOrEqualTo(last));
                last = b.Velocity.X;
            }

            Assert.That(b.IsStopped, Is.True);
            Assert.That(b.Height, Is.EqualTo(0));
        }

        [Test]
        public void LoftedBall_Bounces_ThenRolls()
        {
            var b = Rolling(Vec2i.Zero, new Vec2i(100, 0), 0, 100);
            var bounces = 0;
            var landed = false;
            for (int i = 0; i < 1000 && !landed; i++)
            {
                var hit = b.Step(C);
                Assert.That(b.Height, Is.GreaterThanOrEqualTo(0));
                if ((hit & BallHit.Bounce) != 0) { bounces++; }
                if ((hit & BallHit.Landed) != 0) { landed = true; }
            }

            Assert.That(bounces, Is.GreaterThanOrEqualTo(2), "反発で何度か弾む");
            Assert.That(landed, Is.True);
            Assert.That(b.IsRolling, Is.True);
        }

        [Test]
        public void BallHittingAPost_BouncesBack_AndAboveTheBarPassesOver()
        {
            var post = Pitch.Posts[3];
            var low = Rolling(post - new Vec2i(2000, 0), new Vec2i(300, 0));
            var hits = 0;
            for (int i = 0; i < 20; i++) { if ((low.Step(C) & BallHit.Post) != 0) { hits++; } }
            Assert.That(hits, Is.EqualTo(1));
            Assert.That(low.Velocity.X, Is.LessThan(0), "ポストで跳ね返る");

            var high = Rolling(post - new Vec2i(2000, 0), new Vec2i(300, 0), Pitch.CrossbarHeight + 500, 0);
            for (int i = 0; i < 20; i++) { Assert.That(high.Step(C) & BallHit.Post, Is.EqualTo(BallHit.None)); }
            Assert.That(high.Position.X, Is.GreaterThan(post.X));
        }

        [Test]
        public void Prediction_MatchesTheRealTrajectory_TickByTick()
        {
            var cases = new[]
            {
                Rolling(new Vec2i(-3000, 200), new Vec2i(260, 40)),
                Rolling(Vec2i.Zero, new Vec2i(180, -90), 0, 100),
                Rolling(Pitch.Posts[2] - new Vec2i(3000, -300), new Vec2i(400, -40), 0, 20),
            };
            foreach (var ball in cases)
            {
                var predicted = BallPredictor.Trajectory(ball, C, 400);
                var real = ball.Clone();
                foreach (var s in predicted)
                {
                    real.Step(C);
                    Assert.That(real.Position, Is.EqualTo(s.Position), $"tick {s.Tick}");
                    Assert.That(real.Height, Is.EqualTo(s.Height), $"tick {s.Tick}");
                }

                Assert.That(predicted.Count, Is.GreaterThan(10));
            }
        }

        [Test]
        public void Reach_ClosedForm_MatchesTheSum()
        {
            foreach (var (max, accel) in new[] { (100, 8), (85, 85), (120, 7), (50, 100) })
            {
                long sum = 0;
                for (int t = 1; t <= 300; t++)
                {
                    sum += System.Math.Min((long)t * accel, max);
                    Assert.That(BallPredictor.Reach(t, max, accel), Is.EqualTo(sum), $"t {t} max {max} accel {accel}");
                }
            }
        }

        [Test]
        public void PassTable_IsMonotone_AndMatchesTheRealBall()
        {
            var c = C;
            var table = new PassTable(c);
            for (int v = table.MinSpeed + 1; v <= table.MaxSpeed; v++) { Assert.That(table.Reach(v), Is.GreaterThanOrEqualTo(table.Reach(v - 1))); }
            foreach (var distance in new[] { 1000L, 5000L, 12000L })
            {
                var speed = table.SpeedFor(distance);
                Assert.That(table.Reach(speed), Is.GreaterThanOrEqualTo(distance));
                if (speed > table.MinSpeed) { Assert.That(table.Reach(speed - 1), Is.LessThan(distance), "いちばん遅い速さ"); }

                // 本番の球を同じ速さで転がすと、受け手の速さに落ちたところで表の距離だけ進んでいる。
                var b = Rolling(Vec2i.Zero, new Vec2i(speed, 0));
                for (int i = 0; i < 2000 && b.Velocity.X > c.PassArriveSpeed; i++) { b.Step(c); }
                Assert.That(b.Position.X, Is.EqualTo(table.Reach(speed)));
            }
        }

        [Test]
        public void ArrivalTick_MatchesAPlayerRunningFromRest()
        {
            // 止まった球まで、止まった選手がまっすぐ走る。予測の Tick に届き、その 1 Tick 前には届かない。
            var m = Lab.Open();
            var c = C;
            m.PlaceBall(new Vec2i(6000, 0), Vec2i.Zero);
            m.PlacePlayer(1, new Vec2i(-2000, 0));
            var trajectory = BallPredictor.Trajectory(m.Ball, c, 10);
            var expected = BallPredictor.ArrivalTick(trajectory, m.Players[1].Position, c.PlayerMaxSpeed, c.PlayerAcceleration, c.ControlRadius, c.KneeHeight, 600);
            Assert.That(expected, Is.GreaterThan(0));
            var arrived = -1;
            for (int t = 1; t <= 600 && arrived < 0; t++)
            {
                var cmd = Lab.Idle();
                cmd[1] = new PlayerCommand(new Vec2i(1000, 0));
                m.Step(cmd);
                if (m.Owner == 1) { arrived = t; }
            }

            Assert.That(arrived, Is.EqualTo(expected));
        }
    }
}
