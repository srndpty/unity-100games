using IntGeometry;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>カップイン・木・着地・転がり（題材固有テストの「カップイン判定が球速度条件を満たす」「障害物衝突で再計算」）。</summary>
    public sealed class BallSimTests
    {
        private static readonly GolfConfig _config = new();

        private static int GreenDecel => _config.RollDecel[(int)Surface.Green];

        private static BallEvents StepOnce(ref BallState ball, Hole hole, Vec2i wind = default, GolfConfig? config = null) =>
            BallSim.Step(ref ball, hole, wind, config ?? _config);

        [Test]
        public void RollingIntoTheCupAtExactlyTheSpeedLimitDrops()
        {
            var hole = Lab();

            // 減速の後の速さがちょうど上限（1 Tick に 20 mm）。カップの中心の 60 mm 手前から入る。
            var ball = Rolling(At(169.94, 30), new Vec2i(_config.CupMaxSpeed + GreenDecel, 0));
            var events = StepOnce(ref ball, hole);
            Assert.That(events & BallEvents.Holed, Is.EqualTo(BallEvents.Holed));
            Assert.That(ball.Phase, Is.EqualTo(BallPhase.Holed));
            Assert.That(ball.Position, Is.EqualTo(hole.Cup));
        }

        [Test]
        public void RollingOneUnitTooFastPassesOverTheCup()
        {
            var hole = Lab();
            var ball = Rolling(At(169.94, 30), new Vec2i(_config.CupMaxSpeed + GreenDecel + 1, 0));
            var events = StepOnce(ref ball, hole);
            Assert.That(events & BallEvents.LipOut, Is.EqualTo(BallEvents.LipOut));
            Assert.That(ball.Phase, Is.EqualTo(BallPhase.Rolling));

            // 円の中で遅くなっても、通り過ぎている途中は落ちない。
            var end = Run(ball, hole);
            Assert.That(end.Phase, Is.EqualTo(BallPhase.Resting));
            Assert.That(end.Position.X, Is.GreaterThan(hole.Cup.X + _config.CupRadius));
        }

        [Test]
        public void PathCrossingTheCupWithinOneTickDropsWhenSlowEnough()
        {
            // 1 Tick に 200 mm 進む（始めは 100 mm 手前、終わりは 100 mm 先。どちらもカップの外）。
            var config = new GolfConfig { CupMaxSpeed = 300_000 };
            var hole = Lab(config: config);
            var ball = Rolling(At(169.9, 30), new Vec2i(200_000 + GreenDecel, 0));
            var events = StepOnce(ref ball, hole, config: config);
            Assert.That(events & BallEvents.Holed, Is.EqualTo(BallEvents.Holed));

            // 既定の上限では同じ球は通り過ぎる。
            var fast = Rolling(At(169.9, 30), new Vec2i(200_000 + GreenDecel, 0));
            Assert.That(StepOnce(ref fast, hole) & BallEvents.LipOut, Is.EqualTo(BallEvents.LipOut));
            Assert.That(fast.Phase, Is.EqualTo(BallPhase.Rolling));
            Assert.That(fast.Position.X, Is.GreaterThan(hole.Cup.X + _config.CupRadius));
        }

        [Test]
        public void GrazingTheRimAtExactlyTheRadiusDoesNotDrop()
        {
            var hole = Lab();
            var rim = Run(Rolling(new Vec2i(At(169.9, 30).X, hole.Cup.Y + _config.CupRadius), new Vec2i(15_000, 0)), hole);
            Assert.That(rim.Phase, Is.EqualTo(BallPhase.Resting));
            Assert.That(rim.Position.X, Is.GreaterThan(hole.Cup.X));

            var inside = Run(Rolling(new Vec2i(At(169.9, 30).X, hole.Cup.Y + _config.CupRadius - 1), new Vec2i(15_000, 0)), hole);
            Assert.That(inside.Phase, Is.EqualTo(BallPhase.Holed));
        }

        [Test]
        public void FlyingBallPassesOverTheCup()
        {
            var hole = Lab();
            var ball = Flying(At(169.5, 30), 500_000, new Vec2i(60_000, 0), 0);
            var passedOver = false;
            while (ball.IsMoving)
            {
                var before = ball;
                StepOnce(ref ball, hole);
                if (before.Position.X < hole.Cup.X && ball.Position.X >= hole.Cup.X) { passedOver = ball.Z > 0; }
            }

            Assert.That(passedOver, Is.True, "カップの真上を飛んでいるうちに通り過ぎる");
            Assert.That(ball.Phase, Is.EqualTo(BallPhase.Resting));
        }

        [Test]
        public void LandingInsideTheCupDropsOnlyWhenSlow()
        {
            var hole = Lab();
            var slow = Flying(hole.Cup, 300_000, new Vec2i(1000, 0), 0);
            var end = Run(slow, hole);
            Assert.That(end.Phase, Is.EqualTo(BallPhase.Holed));
            Assert.That(end.Landed, Is.True);

            var fast = Flying(At(169.9, 30), 300_000, new Vec2i(60_000, 0), 0);
            var events = BallEvents.None;
            while (fast.IsMoving && (events & BallEvents.Landed) == 0) { events = StepOnce(ref fast, hole); }
            Assert.That(fast.Phase, Is.Not.EqualTo(BallPhase.Holed));
        }

        [Test]
        public void StepAfterHoledChangesNothing()
        {
            var hole = Lab();
            var ball = Rolling(At(169.9, 30), new Vec2i(5000, 0));
            ball = Run(ball, hole);
            Assert.That(ball.Phase, Is.EqualTo(BallPhase.Holed));
            var copy = ball;
            Assert.That(StepOnce(ref ball, hole), Is.EqualTo(BallEvents.None));
            Assert.That(ball, Is.EqualTo(copy));
        }

        [Test]
        public void TreeSideHitReflectsAndRecomputesTheRestOfTheFlight()
        {
            var withTree = Lab("tree 100 30 1 5\n");
            var withoutTree = Lab();
            var start = Flying(At(90, 30), 2_000_000, new Vec2i(500_000, 0), 0);
            var ball = start;
            var events = BallEvents.None;
            while (ball.IsMoving) { events |= StepOnce(ref ball, withTree); }
            Assert.That(events & BallEvents.TreeHit, Is.EqualTo(BallEvents.TreeHit));
            Assert.That(ball.Position.X, Is.LessThan(At(99, 30).X), "跳ね返って手前に落ちる");

            var free = Run(start, withoutTree);
            Assert.That(free.Position.X, Is.GreaterThan(At(101, 30).X));
        }

        [TestCase(0, true)]
        [TestCase(-1, true)]
        [TestCase(1, false)]
        public void TreeHeightBoundary(int aboveHeight, bool hits)
        {
            // 抵抗を最小にし、鉛直の速さが重力と打ち消して、最初の Tick は高さが変わらない。
            var config = new GolfConfig { Drag = 1, WindPush = 0 };
            var hole = Lab("tree 100 30 1 5\n", config);
            var ball = Flying(At(98.9, 30), 5_000_000 + aboveHeight, new Vec2i(500_000, 0), config.Gravity);
            var events = StepOnce(ref ball, hole, config: config);
            Assert.That((events & BallEvents.TreeHit) != 0, Is.EqualTo(hits));
            Assert.That(ball.Velocity.X < 0, Is.EqualTo(hits));
        }

        [Test]
        public void FastBallDoesNotTunnelThroughAThinTree()
        {
            var hole = Lab("tree 100 30 0.3 10\n");
            for (var offset = -400; offset <= 400; offset += 10)
            {
                // 1 Tick に 1.9 m 進む。木（直径 0.6 m）は Tick の始めと終わりの位置の間にある。
                var ball = Flying(new Vec2i(At(99, 30).X, At(99, 30).Y + (offset * 1000)), 1_000_000, new Vec2i(1_900_000, 0), 0);
                var events = StepOnce(ref ball, hole);
                var expected = System.Math.Abs(offset) < 300;
                Assert.That((events & BallEvents.TreeHit) != 0, Is.EqualTo(expected), $"横のずれ {offset} mm");
            }
        }

        [Test]
        public void BallEnteringTheCrownFromAboveDropsStraightDown()
        {
            var hole = Lab("tree 60 30 3 5\n");
            var ball = Flying(At(59, 30), 10_000_000, new Vec2i(5000, 0), 0);
            var events = BallEvents.None;
            while (ball.IsMoving) { events |= StepOnce(ref ball, hole); }
            Assert.That(events & BallEvents.TreeHit, Is.EqualTo(BallEvents.TreeHit));
            Assert.That(Vec2i.DistanceSquared(ball.Position, At(60, 30)), Is.LessThan(3L * M * 3 * M), "木の下に落ちる");
        }

        [Test]
        public void WindPushesTheFlight()
        {
            var hole = GolfRound.OpenField();
            var start = Launch.From(_config, new ShotSetup(Clubs.Iron, 0, 0, 0, 1000, 0, false), hole.Tee, Surface.Tee);
            var calm = Run(start, hole).Landing;
            var head = Run(start, hole, new Vec2i(-5 * _config.WindStep, 0)).Landing;
            var tail = Run(start, hole, new Vec2i(5 * _config.WindStep, 0)).Landing;
            var cross = Run(start, hole, new Vec2i(0, 5 * _config.WindStep)).Landing;
            Assert.That(head.X, Is.LessThan(calm.X - (5 * M)));
            Assert.That(tail.X, Is.GreaterThan(calm.X + (5 * M)));
            Assert.That(cross.Y, Is.GreaterThan(calm.Y + (5 * M)), "横風で風下（+y）へ流れる");
        }

        [Test]
        public void SlopeBendsARollingBallDownhill()
        {
            var flat = Lab();
            var sloped = Lab("slope 155 18 185 42 0 30\n");
            var start = Rolling(At(158, 30), new Vec2i(50_000, 0));
            var straight = Run(start, flat);
            var bent = Run(start, sloped);
            Assert.That(straight.Position.Y, Is.EqualTo(start.Position.Y));
            Assert.That(bent.Position.Y, Is.GreaterThan(start.Position.Y + (M / 10)));
        }

        [Test]
        public void BallDoesNotStopOnASlopeSteeperThanTheGreenCanHold()
        {
            // 80‰ の加速（155）は グリーンの減速（153）より大きい。
            var steep = Lab("slope 155 18 185 42 0 80\n");
            Assert.That(BallSim.SlopeAcceleration(new Vec2i(0, 80), _config).Y, Is.GreaterThan(GreenDecel));
            var end = Run(Rolling(At(165, 30), Vec2i.Zero), steep);
            Assert.That(end.Phase, Is.EqualTo(BallPhase.Resting));
            Assert.That(steep.SurfaceAt(end.Position), Is.Not.EqualTo(Surface.Green), "グリーンの外の平らな所で止まる");

            // 70‰（136）なら置いた所で止まる。
            var gentle = Lab("slope 155 18 185 42 0 70\n");
            var stays = Run(Rolling(At(165, 30), Vec2i.Zero), gentle);
            Assert.That(stays.Position, Is.EqualTo(At(165, 30)));
        }

        [Test]
        public void RollingIntoOutOfBoundsEndsTheShot()
        {
            var hole = Lab("paint ob rect 100 0 120 60\n");
            var end = Run(Rolling(At(95, 30), new Vec2i(200_000, 0)), hole);
            Assert.That(end.Phase, Is.EqualTo(BallPhase.Out));
            Assert.That(end.OutSurface, Is.EqualTo(Surface.OutOfBounds));
        }

        [Test]
        public void LandingInWaterEndsTheShotAndRecordsTheLanding()
        {
            var hole = Lab("paint water rect 100 0 120 60\n");
            var end = Run(Flying(At(105, 30), 2 * M, new Vec2i(10_000, 0), 0), hole);
            Assert.That(end.Phase, Is.EqualTo(BallPhase.Out));
            Assert.That(end.OutSurface, Is.EqualTo(Surface.Water));
            Assert.That(end.Landed, Is.True);
            Assert.That(hole.SurfaceAt(end.Landing), Is.EqualTo(Surface.Water));
        }

        [Test]
        public void LongShotsAlwaysEndWithinTheTickLimit()
        {
            var config = new GolfConfig();
            var random = new SeededRandom(7);
            var course = Production();
            for (var i = 0; i < 300; i++)
            {
                var hole = course.Holes[i % 3];
                var setup = new ShotSetup(random.Next(Clubs.Count), random.Range(0, 65535), random.Range(-2, 2), random.Range(-2, 2), random.Range(0, 1000), random.Range(-8, 8), random.Next(5) == 0);
                var wind = GolfRound.WindFor(config, unchecked((int)random.NextUInt()), 0);
                var end = Run(Launch.From(config, setup, hole.Tee, Surface.Tee), hole, wind, config);
                Assert.That(end.IsMoving, Is.False);
                Assert.That(end.Ticks, Is.LessThanOrEqualTo(config.MaxShotTicks));
            }
        }
    }
}
