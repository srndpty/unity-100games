using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>設定の検証（範囲外は例外）。</summary>
    public sealed class ConfigTests
    {
        [Test]
        public void Defaults_AreValid()
        {
            Assert.That(() => Config().Validate(), Throws.Nothing);
        }

        [TestCase(nameof(VaultConfig.HeroSpeed), 0)]
        [TestCase(nameof(VaultConfig.HeroSpeed), VaultConfig.MaxSpeed + 1)]
        [TestCase(nameof(VaultConfig.MaxHealth), 0)]
        [TestCase(nameof(VaultConfig.SwingActiveFrom), 0)]
        [TestCase(nameof(VaultConfig.SwingActiveTo), 17)]
        [TestCase(nameof(VaultConfig.RollInvulnTicks), 19)]
        [TestCase(nameof(VaultConfig.InvulnTicks), 0)]
        [TestCase(nameof(VaultConfig.KnockbackTicks), 61)]
        [TestCase(nameof(VaultConfig.EnemyInvulnTicks), 0)]
        [TestCase(nameof(VaultConfig.SlimeMaxTicks), 19)]
        [TestCase(nameof(VaultConfig.BossHealth), VaultConfig.MaxEnemyHealth + 1)]
        [TestCase(nameof(VaultConfig.TransitionTicks), 0)]
        [TestCase(nameof(VaultConfig.SwordReach), 0)]
        public void OutOfRange_IsRejected(string field, int value)
        {
            var config = Config();
            typeof(VaultConfig).GetField(field)!.SetValue(config, value);
            Assert.That(() => config.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Dungeon(config, Real(), 1), Throws.TypeOf<ArgumentOutOfRangeException>(), "試合を作るときも検証する");
        }

        [Test]
        public void Boundaries_AreAccepted()
        {
            var config = new VaultConfig
            {
                HeroSpeed = VaultConfig.MaxSpeed,
                SwingActiveFrom = 16,
                SwingActiveTo = 16,
                RollInvulnTicks = 18,
                KnockbackTicks = 60,
                SlimeMaxTicks = 20,
                SwingMoveSpeed = 0,
            };
            Assert.That(() => config.Validate(), Throws.Nothing);
        }

        [Test]
        public void EveryIntField_IsChecked()
        {
            // 公開されたすべての int の設定に、負の値を入れると拒否される（検証の書き忘れを防ぐ）。
            foreach (var field in typeof(VaultConfig).GetFields())
            {
                if (field.IsLiteral || field.FieldType != typeof(int)) { continue; }
                var config = Config();
                field.SetValue(config, -1);
                Assert.That(() => config.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>(), field.Name);
            }
        }

        [Test]
        public void Dungeon_CopiesTheConfig()
        {
            var config = new VaultConfig { MaxHealth = 3 };
            var dungeon = new Dungeon(config, Real(), 1);
            config.MaxHealth = 9;
            dungeon.Hero.Hurt(1, 0, 0);
            Assert.That(dungeon.Hero.Health, Is.EqualTo(2));
            dungeon.Tick(Idle);
            Assert.That(dungeon.Status, Is.EqualTo(DungeonStatus.Playing));
        }
    }
}
