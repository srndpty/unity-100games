using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>球の 1 Tick（重力・ネット・バウンド）と着地点の予測。</summary>
    public sealed class BallTests
    {
        private static BallPhysics Physics => TestKit.Config().Physics;

        [Test]
        public void GravityIsAppliedBeforeMoving()
        {
            var ball = new Ball(new Vec3(0, -50000, 10000), new Vec3(100, 200, 0));
            Assert.That(ball.Step(Physics).Kind, Is.EqualTo(BallEventKind.None));
            Assert.That(ball.Velocity, Is.EqualTo(new Vec3(100, 200, -Physics.Gravity)));
            Assert.That(ball.Position, Is.EqualTo(new Vec3(100, -49800, 10000 - Physics.Gravity)));
        }

        [Test]
        public void BounceIsReportedWhereTheBallTouchesTheGround()
        {
            // 高さ 100 から縦 -200 - 重力 で落ちる：線分の途中で地面に触れる。
            var ball = new Ball(new Vec3(0, 50000, 100), new Vec3(0, 1000, -200 + Physics.Gravity));
            var e = ball.Step(Physics);
            Assert.That(e.Kind, Is.EqualTo(BallEventKind.Bounce));
            Assert.That(e.Point, Is.EqualTo(new Vec3(0, 50500, 0)), "高さ 100 を 200 で落ちる半分の地点");
            Assert.That(ball.Velocity.Z, Is.EqualTo(200 * Physics.RestitutionPercent / 100));
            Assert.That(ball.Velocity.Y, Is.EqualTo(1000 * Physics.FrictionPercent / 100));
            Assert.That(ball.Position.Z, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void BallReachingTheGroundExactlyAtTheEndOfTheTickBouncesInThatTick()
        {
            // 重力を足した後の縦の速さ -10g で、高さ 10g からちょうど地面に着く。
            var g = Physics.Gravity;
            var ball = new Ball(new Vec3(0, 50000, 10 * g), new Vec3(0, 0, (-10 * g) + g));
            var e = ball.Step(Physics);
            Assert.That(e.Kind, Is.EqualTo(BallEventKind.Bounce));
            Assert.That(e.Point, Is.EqualTo(new Vec3(0, 50000, 0)));
            Assert.That(ball.Position.Z, Is.Zero);
            Assert.That(ball.Velocity.Z, Is.EqualTo(10 * g * Physics.RestitutionPercent / 100));
            Assert.That(ball.Velocity.Z, Is.GreaterThanOrEqualTo(g), "前提：次の Tick は地面から上がる");
            Assert.That(ball.Step(Physics).Kind, Is.EqualTo(BallEventKind.None), "地面から上がる球は続けてバウンドしない");
        }

        [Test]
        public void BallBelowTheNetTopStopsAtTheNet()
        {
            var ball = new Ball(new Vec3(0, -1000, Court.NetHeight - 1), new Vec3(0, 2000, Physics.Gravity));
            var e = ball.Step(Physics);
            Assert.That(e.Kind, Is.EqualTo(BallEventKind.Net));
            Assert.That(ball.IsDead, Is.True);
            Assert.That(ball.Position.Y, Is.Zero);
            Assert.That(ball.Step(Physics).Kind, Is.EqualTo(BallEventKind.None), "止まった球は動かない");
        }

        [Test]
        public void BallAtTheNetTopClearsIt()
        {
            var ball = new Ball(new Vec3(0, -1000, Court.NetHeight), new Vec3(0, 2000, Physics.Gravity));
            Assert.That(ball.Step(Physics).Kind, Is.EqualTo(BallEventKind.None));
            Assert.That(ball.Position.Y, Is.EqualTo(1000));
        }

        [Test]
        public void NetIsCheckedAtTheCrossingHeightNotAtTheEnd()
        {
            // 始めはネットより高く、終わりはネットより低い。ネットの面を通るときの高さで決める。
            var high = new Ball(new Vec3(0, -100, Court.NetHeight + 300), new Vec3(0, 1000, -1000 + Physics.Gravity));
            Assert.That(high.Step(Physics).Kind, Is.EqualTo(BallEventKind.None), "面を通る高さはネット＋200");
            var low = new Ball(new Vec3(0, -900, Court.NetHeight + 300), new Vec3(0, 1000, -1000 + Physics.Gravity));
            Assert.That(low.Step(Physics).Kind, Is.EqualTo(BallEventKind.Net), "面を通る高さはネット − 600");
        }

        [Test]
        public void SolverLandsNearTheTarget()
        {
            var config = TestKit.Config();
            var random = new SeededRandom(5);
            for (var i = 0; i < 200; i++)
            {
                var from = new Vec3(random.Range(-40000, 40000), -random.Range(40000, 125000), random.Range(1000, 22000));
                var target = new Vec3(random.Range(-40000, 40000), random.Range(10000, 115000), 0);
                var speed = random.Range(2000, 9000);
                var clearance = random.Range(0, 15000);
                var ball = new Ball(from, ShotSolver.Solve(from, target.X, target.Y, speed, clearance, config.Gravity));
                var e = TestKit.FlyToFirstEvent(ball, config);
                Assert.That(e.Kind, Is.EqualTo(BallEventKind.Bounce), $"#{i} ネットを越える");
                Assert.That(System.Math.Abs(e.Point.X - target.X), Is.LessThan(1500), $"#{i} 横");
                Assert.That(System.Math.Abs(e.Point.Y - target.Y), Is.LessThan(1500), $"#{i} 縦");
            }
        }

        [Test]
        public void PredictionMatchesTheActualFlight()
        {
            var config = TestKit.Config();
            var random = new SeededRandom(11);
            foreach (var kind in new[] { ShotKind.Stroke, ShotKind.Lob })
            {
                for (var i = 0; i < 100; i++)
                {
                    var contact = new Vec3(random.Range(-40000, 40000), -random.Range(20000, 125000), random.Range(config.MinHitZ, config.MaxHitZ));
                    var timing = random.Range(-HitWindow.MaxTiming, HitWindow.MaxTiming);
                    var ball = new Ball(contact, Shots.Plan(Side.Near, kind, random.Range(-1, 1), timing, contact, ServeCourt.Deuce, config));
                    var predicted = BallPredictor.NextEvent(ball, config.Physics, 600);
                    Assert.That(predicted.HasValue, Is.True);

                    var ticks = 0;
                    BallEvent actual;
                    do
                    {
                        actual = ball.Step(config.Physics);
                        ticks++;
                    }
                    while (actual.Kind == BallEventKind.None);

                    Assert.That(ticks, Is.EqualTo(predicted!.Value.Ticks), $"{kind} #{i}");
                    Assert.That(actual.Kind, Is.EqualTo(predicted.Value.Event.Kind), $"{kind} #{i}");
                    Assert.That(actual.Point, Is.EqualTo(predicted.Value.Event.Point), $"{kind} #{i}");
                }
            }
        }

        [Test]
        public void PredictorSkipsBallsThatWillBeCalledOut()
        {
            var config = TestKit.Config();
            var from = new Vec3(0, 100000, 8000);
            var rally = Rally.Staged(Side.Far, ServeCourt.Deuce, Side.Far);
            var outBall = new Ball(from, TestKit.VelocityTo(from, Court.HalfWidth + 15000, -80000, config));
            Assert.That(BallPredictor.FindContact(outBall, rally, Side.Near, new Vec3(0, -110000, 0), config.MoveSpeed, config, 400), Is.Null);

            var inBall = new Ball(from, TestKit.VelocityTo(from, 10000, -80000, config));
            var contact = BallPredictor.FindContact(inBall, rally, Side.Near, new Vec3(0, -110000, 0), config.MoveSpeed, config, 400);
            Assert.That(contact.HasValue, Is.True);
            Assert.That(contact!.Value.Position.Z, Is.InRange(config.MinHitZ, config.MaxHitZ));
            Assert.That(contact.Value.Position.Y, Is.LessThan(-80000), "弾んだ後");
        }
    }
}
