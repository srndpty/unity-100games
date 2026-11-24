using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class GolfConfigTests
    {
        [Test]
        public void DefaultsAreValid() => Assert.DoesNotThrow(() => new GolfConfig().Validate());

        private static void Rejects(Action<GolfConfig> change)
        {
            var config = new GolfConfig();
            change(config);
            Assert.Throws(Is.InstanceOf<ArgumentException>(), config.Validate);
        }

        [Test]
        public void RejectsValuesOutOfRange()
        {
            Rejects(c => c.Gravity = 0);
            Rejects(c => c.Drag = 0);
            Rejects(c => c.MeterHalfTicks = 9);
            Rejects(c => c.ImpactWindow = 0);
            Rejects(c => c.ImpactWindow = c.MeterHalfTicks);
            Rejects(c => c.CupRadius = 0);
            Rejects(c => c.CupMaxSpeed = 0);
            Rejects(c => c.MaxShotTicks = 10);
            Rejects(c => c.MaxOver = 0);
            Rejects(c => c.AimStep = 1);
            Rejects(c => c.BounceRestitution[(int)Surface.Green] = Units.Ratio);
            Rejects(c => c.RollDecel[(int)Surface.Fairway] = 0);
            Rejects(c => c.ClubBite[Clubs.Wedge] = Units.Ratio + 1);
        }

        [Test]
        public void RejectsSpeedsThatWouldBreakTheSweepLimits()
        {
            Rejects(c => c.ClubSpeed[Clubs.Wood] = Units.MaxTickMove);
            Rejects(c => c.MaxWindSteps = 20);
            var config = new GolfConfig();
            var windTerminal = (long)config.MaxWindSteps * config.WindStep * config.WindPush / config.Drag;
            config.ClubSpeed[Clubs.Wood] = (int)((Units.MaxTickMove * 3 / 4) - windTerminal);
            Assert.DoesNotThrow(config.Validate);
            config.ClubSpeed[Clubs.Wood]++;
            Assert.Throws<ArgumentOutOfRangeException>(config.Validate);
        }

        [Test]
        public void RejectsMalformedArraysAndAPutterWithLoft()
        {
            Rejects(c => c.ClubSpeed = new int[Clubs.Count - 1]);
            Rejects(c => c.LiePower = new int[GolfConfig.SurfaceCount + 1]);
            Rejects(c => c.RollDecel = null!);
            Rejects(c => c.ClubLoft[Clubs.Putter] = 100);
            Rejects(c => c.ClubLoft[Clubs.Wedge] = 13000);
        }

        [Test]
        public void CopyIsDeep()
        {
            var config = new GolfConfig();
            var copy = config.Copy();
            copy.ClubSpeed[0] = 1;
            copy.RollDecel[0] = 1;
            Assert.That(config.ClubSpeed[0], Is.Not.EqualTo(1));
            Assert.That(config.RollDecel[0], Is.Not.EqualTo(1));
        }

        [Test]
        public void LaunchRejectsHazardLiesAndKeepsThePutterOnTheGround()
        {
            var config = new GolfConfig();
            var setup = new ShotSetup(Clubs.Putter, 0, 2, -2, 1000, 0, false);
            Assert.Throws<ArgumentOutOfRangeException>(() => Launch.From(config, setup, IntGeometry.Vec2i.Zero, Surface.Water));
            var ball = Launch.From(config, setup, IntGeometry.Vec2i.Zero, Surface.Green);
            Assert.That(ball.Phase, Is.EqualTo(BallPhase.Rolling));
            Assert.That(ball.VelocityZ, Is.EqualTo(0));
            Assert.That(ball.Spin, Is.EqualTo(0), "パターは打点が効かない");
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ShotSetup(Clubs.Count, 0, 0, 0, 0, 0, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ShotSetup(0, 0, 3, 0, 0, 0, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ShotSetup(0, 0, 0, 0, 1001, 0, false));
        }

        [Test]
        public void ImpactPointChangesLaunchAndBite()
        {
            var config = new GolfConfig();
            var low = Launch.From(config, new ShotSetup(Clubs.Iron, 0, 0, -2, 1000, 0, false), IntGeometry.Vec2i.Zero, Surface.Fairway);
            var center = Launch.From(config, new ShotSetup(Clubs.Iron, 0, 0, 0, 1000, 0, false), IntGeometry.Vec2i.Zero, Surface.Fairway);
            var right = Launch.From(config, new ShotSetup(Clubs.Iron, 0, 2, 0, 1000, 0, false), IntGeometry.Vec2i.Zero, Surface.Fairway);
            Assert.That(low.VelocityZ, Is.GreaterThan(center.VelocityZ), "下を打つと高く出る");
            Assert.That(low.FirstBite, Is.LessThan(center.FirstBite), "下を打つとバックスピンで止まる");
            Assert.That(right.Spin, Is.EqualTo(2 * config.SpinPerImpact));
            var rough = Launch.From(config, new ShotSetup(Clubs.Iron, 0, 0, 0, 1000, 0, false), IntGeometry.Vec2i.Zero, Surface.Rough);
            Assert.That(rough.Velocity.X, Is.LessThan(center.Velocity.X), "ラフからは飛ばない");
        }
    }
}
