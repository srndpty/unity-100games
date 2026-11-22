using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>設定の検証と、ゲートが成り立つ跳躍の高さ。</summary>
    public sealed class ConfigTests
    {
        [Test]
        public void Defaults_AreValid_AndGateHolds()
        {
            var config = Config();
            Assert.DoesNotThrow(config.Validate);
            var single = JumpReach.Measure(config, doubleJump: false);
            var both = JumpReach.Measure(config, doubleJump: true);
            TestContext.Out.WriteLine($"1 回 {single}（{single / (double)T:F2} タイル）、二段 {both}（{both / (double)T:F2} タイル）");
            Assert.That(single, Is.GreaterThanOrEqualTo(Rooms.StepRise * T), "1 回で足場に届く");
            Assert.That(single, Is.LessThan(Rooms.GateRise * T), "1 回では高い段に届かない");
            Assert.That(both, Is.GreaterThanOrEqualTo(Rooms.GateRise * T), "二段なら届く");
        }

        [Test]
        public void JumpReach_MatchesTheAnalyticPeak()
        {
            // 押し続けたジャンプの頂点は、速さ v から重力 g ずつ減る和（減った後に動く）。
            var config = Config();
            var expected = 0;
            for (var v = config.JumpSpeed - config.Gravity; v > 0; v -= config.Gravity) { expected += v; }
            Assert.That(JumpReach.Measure(config, doubleJump: false), Is.EqualTo(expected));
        }

        [Test]
        public void Validate_RejectsJumpsThatBreakTheGate()
        {
            var high = Config();
            high.JumpSpeed = 120;
            Assert.That(() => high.Validate(), Throws.ArgumentException.With.Message.Contains("ゲートが成り立ちません"));

            var weakDouble = Config();
            weakDouble.DoubleJumpSpeed = 30;
            Assert.That(() => weakDouble.Validate(), Throws.ArgumentException.With.Message.Contains("二段ジャンプでも"));

            var low = Config();
            low.JumpSpeed = 60;
            Assert.That(() => low.Validate(), Throws.ArgumentException.With.Message.Contains("足場に届きません"));
        }

        [TestCase(nameof(ExplorerConfig.MoveSpeed), 0)]
        [TestCase(nameof(ExplorerConfig.MoveSpeed), ExplorerConfig.MaxSpeed + 1)]
        [TestCase(nameof(ExplorerConfig.Gravity), 0)]
        [TestCase(nameof(ExplorerConfig.JumpCutSpeed), -1)]
        [TestCase(nameof(ExplorerConfig.CoyoteTicks), ExplorerConfig.MaxGraceTicks + 1)]
        [TestCase(nameof(ExplorerConfig.MaxHealth), 0)]
        [TestCase(nameof(ExplorerConfig.MaxHealth), ExplorerConfig.MaxHealthLimit + 1)]
        [TestCase(nameof(ExplorerConfig.InvincibleTicks), 0)]
        [TestCase(nameof(ExplorerConfig.MaxShots), 0)]
        [TestCase(nameof(ExplorerConfig.MaxShots), ExplorerConfig.MaxShotsLimit + 1)]
        [TestCase(nameof(ExplorerConfig.BossHealth), 0)]
        [TestCase(nameof(ExplorerConfig.FruitHeal), 0)]
        public void Validate_RejectsOutOfRange(string field, int value)
        {
            var config = Config();
            typeof(ExplorerConfig).GetField(field)!.SetValue(config, value);
            Assert.Throws<ArgumentOutOfRangeException>(config.Validate);
        }

        [Test]
        public void Validate_KnockbackCannotOutlastInvincibility()
        {
            var config = Config();
            config.KnockbackTicks = config.InvincibleTicks + 1;
            Assert.Throws<ArgumentOutOfRangeException>(config.Validate);
            config.KnockbackTicks = config.InvincibleTicks;
            Assert.DoesNotThrow(config.Validate);
        }

        [Test]
        public void Expedition_CopiesTheConfig()
        {
            var config = Config();
            var expedition = new Expedition(config, Real());
            config.MaxHealth = 1;
            Assert.That(expedition.Player.Health, Is.EqualTo(5), "作った後の設定の書き換えは影響しない");
            Assert.Throws<ArgumentNullException>(() => _ = new Expedition(null!, Real()));
            config.Gravity = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Expedition(config, Real()));
        }

        [Test]
        public void DiagonalOf_KeepsPositiveSpeedsMoving()
        {
            Assert.That(ExplorerConfig.DiagonalOf(1), Is.EqualTo(1));
            Assert.That(ExplorerConfig.DiagonalOf(56), Is.EqualTo(39));
            Assert.That(ExplorerConfig.DiagonalOf(0), Is.Zero);
        }
    }
}
