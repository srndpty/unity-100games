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

        [Test]
        public void Defaults_CrossEveryRequiredStepAndGap()
        {
            var config = Config();
            foreach (var crossing in Rooms.Crossings)
            {
                Assert.That(Traversal.CanCross(config, crossing), Is.True, crossing.ToString());
            }

            Assert.That(Traversal.CanCross(config, new Crossing("二段なしの高い段", Rooms.GateRise, 0, doubleJump: false)), Is.False, "高い段は二段ジャンプでしか越えられない");
            Assert.That(Traversal.CanCross(config, new Crossing("届かない隙間", 0, 30, doubleJump: true)), Is.False);
        }

        [Test]
        public void Validate_RejectsTooSlowRunning()
        {
            // レビュー P1：MoveSpeed 1 は高さだけの検証を通り、拠点の足場の間（横 2 タイル）を越えられずに塔へ進めなかった。
            var config = Config();
            config.MoveSpeed = 1;
            Assert.That(() => config.Validate(), Throws.ArgumentException.With.Message.Contains("拠点の足場").And.Message.Contains("越えられません"));
        }

        [Test]
        public void SlowestAcceptedRunning_StillReachesTheTowerOnTheRealMap()
        {
            // 検証を通る最小の横の速さを探し、その 1 つ下は拒否されること、その値で本番の拠点を塔まで登れることを確かめる
            // （検証の越え方が実際の地図と食い違っていない）。
            var slowest = 0;
            for (var speed = 1; speed <= Config().MoveSpeed && slowest == 0; speed++)
            {
                var candidate = Config();
                candidate.MoveSpeed = speed;
                try
                {
                    candidate.Validate();
                    slowest = speed;
                }
                catch (ArgumentException)
                {
                }
            }

            TestContext.Out.WriteLine($"検証を通る最小の MoveSpeed：{slowest}");
            Assert.That(slowest, Is.GreaterThan(1));
            var below = Config();
            below.MoveSpeed = slowest - 1;
            Assert.Throws<ArgumentException>(below.Validate);

            var config = Config();
            config.MoveSpeed = slowest;
            var climb = Reachability.Run(At(Real(), Rooms.Hub, new Cell(4, 2), Ability.DoubleJump, config), e => e.RoomIndex == Rooms.Tower, (31 * T, 21 * T), 400_000, fine: true);
            Assert.That(climb.Found, Is.True, $"MoveSpeed {slowest} で塔へ登れる（{climb.Expanded} 手を展開）");
            var corridor = Reachability.Run(At(Real(), Rooms.Corridor, new Cell(2, 2), Ability.None, config), e => e.RoomIndex == Rooms.Cave, (39 * T, 7 * T), 400_000, fine: true);
            Assert.That(corridor.Found, Is.True, $"MoveSpeed {slowest} で回廊の段を登れる（{corridor.Expanded} 手を展開）");
        }

        [Test]
        public void Crossing_RejectsInvalidShapes()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Crossing("x", -1, 0, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Crossing("x", 0, Room.Columns, false));
            Assert.Throws<ArgumentException>(() => _ = new Crossing("x", 0, 0, false));
            Assert.Throws<ArgumentNullException>(() => _ = new Crossing(null!, 1, 0, false));
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
