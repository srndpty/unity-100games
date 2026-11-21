using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>設定の検証：既定値・各値の境界と範囲外。</summary>
    public sealed class ConfigTests
    {
        /// <summary>（名前、設定する処理、許される下限、上限）。上下限は既定値の他の項目を前提にする。</summary>
        private static IEnumerable<TestCaseData> Bounds()
        {
            var d = new PrecisionConfig();
            const int maxSpeed = PrecisionConfig.MaxSpeed;
            yield return Case("MoveSpeed", (c, v) => c.MoveSpeed = v, 1, maxSpeed);
            yield return Case("GroundAccel", (c, v) => c.GroundAccel = v, 1, maxSpeed);
            yield return Case("AirAccel", (c, v) => c.AirAccel = v, 1, maxSpeed);
            yield return Case("GroundDecel", (c, v) => c.GroundDecel = v, 1, maxSpeed);
            yield return Case("AirDecel", (c, v) => c.AirDecel = v, 1, maxSpeed);
            yield return Case("Gravity", (c, v) => c.Gravity = v, 1, d.WallJumpSpeedY);
            yield return Case("MaxFallSpeed", (c, v) => c.MaxFallSpeed = v, d.WallSlideMaxFall, maxSpeed);
            yield return Case("JumpSpeed", (c, v) => c.JumpSpeed = v, d.JumpCutSpeed + 1, maxSpeed);
            yield return Case("JumpCutSpeed", (c, v) => c.JumpCutSpeed = v, 0, d.JumpSpeed - 1);
            yield return Case("CoyoteTicks", (c, v) => c.CoyoteTicks = v, 0, PrecisionConfig.MaxGraceTicks);
            yield return Case("JumpBufferTicks", (c, v) => c.JumpBufferTicks = v, 0, PrecisionConfig.MaxGraceTicks);
            yield return Case("WallReach", (c, v) => c.WallReach = v, 1, Units.Tile - 1);
            yield return Case("WallJumpSpeedX", (c, v) => c.WallJumpSpeedX = v, 1, maxSpeed);
            yield return Case("WallJumpSpeedY", (c, v) => c.WallJumpSpeedY = v, d.Gravity, maxSpeed);
            yield return Case("WallJumpLockTicks", (c, v) => c.WallJumpLockTicks = v, 0, PrecisionConfig.MaxLockTicks);
            yield return Case("WallSlideMaxFall", (c, v) => c.WallSlideMaxFall = v, 1, d.MaxFallSpeed);
            yield return Case("DashSpeed", (c, v) => c.DashSpeed = v, d.DashEndSpeed, maxSpeed);
            yield return Case("DashTicks", (c, v) => c.DashTicks = v, 1, PrecisionConfig.MaxDashTicks);
            yield return Case("DashEndSpeed", (c, v) => c.DashEndSpeed = v, 0, d.DashSpeed);
            yield return Case("DashCount", (c, v) => c.DashCount = v, 1, PrecisionConfig.MaxDashCount);
            yield return Case("DeathTicks", (c, v) => c.DeathTicks = v, 1, PrecisionConfig.MaxDeathTicks);
        }

        private static TestCaseData Case(string name, Action<PrecisionConfig, int> set, int min, int max)
        {
            return new TestCaseData(set, min, max).SetName($"Config_Bounds_{name}");
        }

        [Test]
        public void Config_DefaultsAreValid()
        {
            Assert.DoesNotThrow(() => new PrecisionConfig().Validate());
        }

        [TestCaseSource(nameof(Bounds))]
        public void Config_Bounds(Action<PrecisionConfig, int> set, int min, int max)
        {
            foreach (var value in new[] { min, max })
            {
                var ok = new PrecisionConfig();
                set(ok, value);
                Assert.DoesNotThrow(ok.Validate, $"{value} は許す");
            }

            foreach (var value in new[] { min - 1, max + 1, int.MinValue, int.MaxValue })
            {
                var bad = new PrecisionConfig();
                set(bad, value);
                Assert.Throws<ArgumentOutOfRangeException>(bad.Validate, $"{value} は拒否する");
            }
        }

        [Test]
        public void Config_DiagonalDashStaysWithinBounds()
        {
            Assert.That(PrecisionConfig.DiagonalOf(PrecisionConfig.MaxSpeed), Is.LessThan(PrecisionConfig.MaxSpeed));
            Assert.That(PrecisionConfig.DiagonalOf(72), Is.EqualTo(50));
            Assert.That(PrecisionConfig.DiagonalOf(0), Is.Zero);
        }

        [Test]
        public void Config_CopyIsIndependent()
        {
            var config = new PrecisionConfig();
            var copy = config.Copy();
            config.DashTicks = 1;
            Assert.That(copy.DashTicks, Is.EqualTo(new PrecisionConfig().DashTicks));
        }
    }
}
