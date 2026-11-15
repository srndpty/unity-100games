using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class CpuControllerTests
    {
        [Test]
        public void ApproachingBall_MovesTowardBall()
        {
            var match = TestCourt.NewMatch();
            var cpu = new CpuController(new CpuConfig { AimError = 0f }, Side.Right, 1);
            match.Ball.Launch(new Vec2(0f, 3f), new Vec2(5f, 0f));

            Assert.That(cpu.Decide(match), Is.GreaterThan(0f));
        }

        [Test]
        public void RecedingBall_ReturnsToCenter()
        {
            var match = TestCourt.NewMatch();
            var cpu = new CpuController(new CpuConfig { AimError = 0f }, Side.Right, 1);
            match.Right.SetY(2f);
            match.Ball.Launch(new Vec2(0f, 3f), new Vec2(-5f, 0f));

            Assert.That(cpu.Decide(match), Is.LessThan(0f));
        }

        [Test]
        public void Input_IsCappedByCpuMaxSpeed()
        {
            var match = TestCourt.NewMatch();
            var config = new CpuConfig { AimError = 0f };
            var cpu = new CpuController(config, Side.Right, 1);
            match.Ball.Launch(new Vec2(0f, 4f), new Vec2(5f, 0f));

            var input = cpu.Decide(match);

            Assert.That(input, Is.EqualTo(config.MaxSpeed / match.Config.PaddleSpeed).Within(1e-5f));
        }

        [Test]
        public void WithinDeadZone_StaysStill()
        {
            var match = TestCourt.NewMatch();
            var cpu = new CpuController(new CpuConfig { AimError = 0f }, Side.Right, 1);
            match.Ball.Launch(new Vec2(0f, 0.05f), new Vec2(5f, 0f));

            Assert.That(cpu.Decide(match), Is.Zero);
        }

        [Test]
        public void NonFiniteConfig_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => new CpuController(new CpuConfig { MaxSpeed = float.PositiveInfinity }, Side.Right, 1));
            Assert.Throws<ArgumentException>(() => new CpuController(new CpuConfig { DeadZone = float.PositiveInfinity }, Side.Right, 1));
            Assert.Throws<ArgumentException>(() => new CpuController(new CpuConfig { AimError = float.NaN }, Side.Right, 1));
        }
    }
}
