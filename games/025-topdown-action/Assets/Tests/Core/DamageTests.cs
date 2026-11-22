using NUnit.Framework;
using TileMotion;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>題材固有テスト：無敵中に二重ダメージなし（自機と敵の両方）。</summary>
    public sealed class DamageTests
    {
        /// <summary>左と下の射手が、自機に向けて同じ距離から同時に撃つ。</summary>
        private static readonly string[] _crossfire =
        {
            "#######",
            "#.....#",
            "#a..S.#",
            "#.....#",
            "#.....#",
            "#...a.#",
            "#######",
        };

        /// <summary>射手 1 体（自機をその上に置いて、触れたままにする）。</summary>
        private static readonly string[] _post =
        {
            "#######",
            "#.....#",
            "#Sa...#",
            "#.....#",
            "#######",
        };

        [Test]
        public void TwoHitsInTheSameTick_CountOnce()
        {
            var dungeon = At(Worlds(Map(0, 0, _crossfire)), 0, new Cell(4, 4));
            var hurtTicks = 0;
            for (var i = 0; i < 40; i++)
            {
                dungeon.Tick(Idle);
                if ((dungeon.TakeEvents() & DungeonEvents.Hurt) != 0) { hurtTicks++; }
            }

            Assert.That(hurtTicks, Is.EqualTo(1));
            Assert.That(dungeon.Hero.Health, Is.EqualTo(Config().MaxHealth - 1), "2 発が同じ Tick に当たっても 1 回");
        }

        [Test]
        public void StayingInContact_HurtsAgainExactlyWhenInvulnerabilityEnds()
        {
            var config = new VaultConfig { KnockbackTicks = 0, KnockbackSpeed = 0 };
            var dungeon = At(Worlds(Map(0, 0, _post)), 0, new Cell(2, 2), config);
            dungeon.Tick(Idle);
            Assert.That(dungeon.Hero.Health, Is.EqualTo(config.MaxHealth - 1), "触れた Tick に 1 回");

            for (var tick = 2; tick <= 1 + config.InvulnTicks; tick++)
            {
                dungeon.Tick(Idle);
                Assert.That(dungeon.Hero.Health, Is.EqualTo(config.MaxHealth - 1), $"{tick} Tick 目は無敵");
            }

            dungeon.Tick(Idle);
            Assert.That(dungeon.Hero.Health, Is.EqualTo(config.MaxHealth - 2), "無敵が切れた Tick に再び受ける");
        }

        [Test]
        public void Hero_InvulnerableForExactlyInvulnTicks_AfterTheHurtTick()
        {
            var config = Config();
            var terrain = Open();
            var hero = new Hero(config, new Cell(5, 4).Centered(Hero.Size));
            Assert.That(hero.Hurt(1, 0, 0), Is.True);
            Assert.That(hero.Hurt(1, 0, 0), Is.False, "同じ Tick の 2 回目は受けない");
            hero.EndTick();
            for (var tick = 1; tick <= config.InvulnTicks; tick++)
            {
                hero.Step(terrain, Idle);
                Assert.That(hero.IsInvulnerable, Is.True, $"被弾の {tick} Tick 後");
                Assert.That(hero.Hurt(1, 0, 0), Is.False);
                hero.EndTick();
            }

            hero.Step(terrain, Idle);
            Assert.That(hero.IsInvulnerable, Is.False);
            Assert.That(hero.Hurt(1, 0, 0), Is.True);
            Assert.That(hero.Health, Is.EqualTo(config.MaxHealth - 2));
        }

        [Test]
        public void Roll_IsInvulnerableOnlyForItsFirstTicks()
        {
            var config = Config();
            var terrain = Open(30, 8);
            var hero = new Hero(config, new Cell(2, 4).Centered(Hero.Size));
            hero.Step(terrain, Idle);
            hero.EndTick();
            var actions = hero.Step(terrain, In(x: 1, roll: true));
            Assert.That(actions & HeroActions.Rolled, Is.EqualTo(HeroActions.Rolled));
            for (var tick = 1; tick <= config.RollInvulnTicks; tick++)
            {
                Assert.That(hero.IsInvulnerable, Is.True, $"ロールの {tick} Tick 目");
                Assert.That(hero.Hurt(1, 0, 0), Is.False);
                hero.EndTick();
                hero.Step(terrain, In(x: 1, roll: true));
            }

            Assert.That(hero.RollTick, Is.EqualTo(config.RollInvulnTicks + 1));
            Assert.That(hero.IsInvulnerable, Is.False, "無敵の Tick を過ぎたロールは当たる");
            Assert.That(hero.Hurt(1, 0, 0), Is.True);
            Assert.That(hero.IsRolling, Is.False, "被弾でロールは止まる");
            Assert.That(hero.Health, Is.EqualTo(config.MaxHealth - 1));
        }

        [Test]
        public void Enemy_OneSwingHitsOnce_EvenIfItsInvulnerabilityIsShort()
        {
            var config = new VaultConfig { EnemyInvulnTicks = 1, EnemyKnockbackTicks = 0, ChargerWindupTicks = VaultConfig.MaxStatusTicks, ChargerHealth = 2 };
            var dungeon = At(Worlds(Map(0, 0, "#######", "#.....#", "#S..c.#", "#.....#", "#######")), 0, new Cell(3, 2), config);
            Hold(dungeon, Right, 1);
            var first = Press(dungeon, In(attack: true));
            first |= Hold(dungeon, Idle, config.SwingTicks);
            var charger = dungeon.Run.Enemies[0];
            Assert.That(first & DungeonEvents.EnemyHit, Is.EqualTo(DungeonEvents.EnemyHit));
            Assert.That(charger.Health, Is.EqualTo(1), "当たる Tick が 8 回あっても 1 回の振りでは 1 回");

            var second = Press(dungeon, In(attack: true));
            second |= Hold(dungeon, Idle, config.SwingTicks);
            Assert.That(charger.IsAlive, Is.False, "2 回目の振りでまた当たる");
            Assert.That(second & DungeonEvents.EnemyDefeated, Is.EqualTo(DungeonEvents.EnemyDefeated));
        }

        [Test]
        public void Enemy_InvulnerableForExactlyEnemyInvulnTicks()
        {
            var config = Config();
            var enemy = new Enemy(EnemyKind.Slime, new Cell(3, 3).Centered(Enemy.SizeOf(EnemyKind.Slime)), 5);
            Assert.That(enemy.Hit(1, Facing.Right, 1, config), Is.True);
            Assert.That(enemy.Hit(2, Facing.Right, 1, config), Is.False, "同じ Tick の別の振り");
            enemy.EndTick();
            for (var tick = 1; tick <= config.EnemyInvulnTicks; tick++)
            {
                Assert.That(enemy.Hit(100 + tick, Facing.Right, 1, config), Is.False, $"{tick} Tick 後は無敵");
                enemy.EndTick();
            }

            Assert.That(enemy.Hit(999, Facing.Right, 1, config), Is.True);
            Assert.That(enemy.Health, Is.EqualTo(3));
        }

        [Test]
        public void HurtWithNonPositiveDamage_IsRejected()
        {
            var hero = new Hero(Config(), new Cell(2, 2).Centered(Hero.Size));
            Assert.That(() => hero.Hurt(0, 0, 0), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(hero.Health, Is.EqualTo(Config().MaxHealth));
        }

        [Test]
        public void Knockback_IgnoresInput_ForKnockbackTicks()
        {
            var config = Config();
            var terrain = Open(30, 8);
            var hero = new Hero(config, new Cell(10, 4).Centered(Hero.Size));
            hero.Hurt(1, hero.Body.CenterX + Units.Tile, Boxes.CenterY(hero.Body));
            hero.EndTick();
            var x = hero.Body.X;
            for (var tick = 0; tick < config.KnockbackTicks; tick++) { hero.Step(terrain, Right); }
            Assert.That(hero.Body.X, Is.EqualTo(x - (config.KnockbackTicks * config.KnockbackSpeed)), "右を押していても左へ押し戻される");
            hero.Step(terrain, Right);
            Assert.That(hero.Body.X, Is.EqualTo(x - (config.KnockbackTicks * config.KnockbackSpeed) + config.HeroSpeed));
        }
    }
}
