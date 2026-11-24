using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>ドラッグ距離から初速が一貫する・弾道プレビュー。</summary>
    public sealed class LaunchTests
    {
        private static readonly SiegeConfig _config = new();

        [Test]
        public void VelocityPointsOppositeToThePullAndScalesWithItsLength()
        {
            var pull = new V2(-1.2f, -0.5f);
            var velocity = Launch.Velocity(pull, _config)!.Value;
            var expected = _config.MaxSpeed * pull.Length / _config.MaxPull;
            Assert.That(velocity.Length, Is.EqualTo(expected).Within(1e-4f));
            // 向きは引きの逆（外積 0・内積が負）。
            Assert.That(velocity.X * pull.Y - velocity.Y * pull.X, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(velocity.X * pull.X + velocity.Y * pull.Y, Is.LessThan(0f));
        }

        [Test]
        public void SpeedIsProportionalUpToMaxPullAndCappedBeyond()
        {
            var direction = new V2(-0.8f, -0.6f);
            var previous = 0f;
            for (var length = _config.MinPull + 0.01f; length <= _config.MaxPull; length += 0.1f)
            {
                var speed = Launch.Velocity(direction * length, _config)!.Value.Length;
                Assert.That(speed, Is.GreaterThan(previous));
                Assert.That(speed, Is.EqualTo(_config.MaxSpeed * length / _config.MaxPull).Within(1e-3f));
                previous = speed;
            }

            foreach (var length in new[] { _config.MaxPull, _config.MaxPull * 1.5f, 50f })
            {
                Assert.That(Launch.Velocity(direction * length, _config)!.Value.Length, Is.EqualTo(_config.MaxSpeed).Within(1e-3f));
            }
        }

        [Test]
        public void SameReleasePointGivesTheSameVelocityWhateverTheDragPath()
        {
            // 初速は離した位置（引き）だけの関数。経路は Flow が渡さないので、同じ引きに対して何度呼んでも、どの弾でも同じ。
            var pull = new V2(-1.7f, -0.9f);
            var first = Launch.Velocity(pull, _config);
            for (var i = 0; i < 10; i++) { Assert.That(Launch.Velocity(pull, _config), Is.EqualTo(first)); }

            foreach (var shots in new[] { "normal", "heavy", "bomb" })
            {
                var stage = TestKit.Small(shots);
                var run = new SiegeRun(_config, stage);
                Assert.That(run.Launch(pull), Is.True);
                Assert.That(run.BeginTick().Launch, Is.EqualTo(first), shots);
            }
        }

        [Test]
        public void PullShorterThanMinPullCancels()
        {
            Assert.That(Launch.Velocity(new V2(0f, -_config.MinPull), _config), Is.Not.Null);
            var below = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(_config.MinPull) - 1);
            Assert.That(Launch.Velocity(new V2(below, 0f), _config), Is.Null);
            Assert.That(Launch.Velocity(new V2(_config.MinPull, 0f), _config), Is.Not.Null);
            Assert.That(Launch.Velocity(V2.Zero, _config), Is.Null);
            Assert.That(Launch.Velocity(new V2(float.NaN, 1f), _config), Is.Null);
            Assert.That(Launch.Velocity(new V2(float.PositiveInfinity, 0f), _config), Is.Null);
        }

        [Test]
        public void ClampLimitsTheDrawnPullToMaxPull()
        {
            Assert.That(Launch.Clamp(new V2(-1f, 0f), _config), Is.EqualTo(new V2(-1f, 0f)));
            Assert.That(Launch.Clamp(new V2(0f, -10f), _config).Length, Is.EqualTo(_config.MaxPull).Within(1e-5f));
            Assert.That(Launch.Clamp(new V2(float.NaN, 0f), _config), Is.EqualTo(V2.Zero));
        }

        [Test]
        public void TrajectoryMatchesTheClosedFormOfSemiImplicitEuler()
        {
            var start = new V2(4f, 2.5f);
            var velocity = new V2(12f, 9f);
            var points = new V2[120];
            Trajectory.Fill(start, velocity, _config.Gravity, points);
            var dt = 1.0 / SiegeRun.TickRate;
            for (var n = 1; n <= points.Length; n++)
            {
                // v_k = v0 + g·k·dt、x_n = x0 + Σ_{k=1..n} v_k·dt = x0 + v0·n·dt + g·dt²·n(n+1)/2
                var x = start.X + velocity.X * n * dt;
                var y = start.Y + velocity.Y * n * dt + _config.Gravity * dt * dt * n * (n + 1) / 2.0;
                Assert.That(points[n - 1].X, Is.EqualTo(x).Within(1e-3), $"Tick {n}");
                Assert.That(points[n - 1].Y, Is.EqualTo(y).Within(1e-3), $"Tick {n}");
            }
        }

        [Test]
        public void TrajectoryDoesNotDependOnBufferLength()
        {
            var shortBuffer = new V2[10];
            var longBuffer = new V2[50];
            Trajectory.Fill(new V2(1f, 1f), new V2(5f, 5f), -9.81f, shortBuffer);
            Trajectory.Fill(new V2(1f, 1f), new V2(5f, 5f), -9.81f, longBuffer);
            for (var i = 0; i < shortBuffer.Length; i++) { Assert.That(longBuffer[i], Is.EqualTo(shortBuffer[i])); }
        }
    }
}
