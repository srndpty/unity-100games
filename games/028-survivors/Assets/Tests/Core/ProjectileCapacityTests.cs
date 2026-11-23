using System;
using IntGeometry;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>
    /// レビュー（028 P2-1）：弾の容量の検証は、強化をまたいで旧レベルの弾が残る場合も含める。
    /// 検証を通った設定では、どんな強化の時機でも弾の置き場があふれない（例外で止まらない）。
    /// </summary>
    public sealed class ProjectileCapacityTests
    {
        private static WeaponLevel Level(int count, int interval, int life) =>
            new() { Damage = 1, IntervalTicks = interval, Count = count, Range = 16 * T, Size = 8, Speed = 1, Pierce = 1, LifeTicks = life };

        /// <summary>Bolt 1 つだけの設定（弾は遅く、的まで届かずに寿命いっぱい残る）。的は止まった丈夫な Gnat。</summary>
        private static TideConfig Only(int maxProjectiles, params WeaponLevel[] levels)
        {
            var config = Quiet();
            Sturdy(config, Gnat);
            config.Weapons = new[] { new WeaponDef { Id = "bolt", Kind = WeaponKind.Bolt, Levels = levels } };
            config.StartingWeapon = 0;
            config.MaxProjectiles = maxProjectiles;
            return config;
        }

        /// <summary>経験値を置いて次の Tick でレベルを上げ、3 択の「強化」を選ぶ（その Tick の発射は強化の前のレベル）。</summary>
        private static void Upgrade(TideRun run)
        {
            run.PlaceGem(run.Player, (int)(run.ExperienceToNext - run.Experience));
            run.Step(PlayerCommand.Idle);
            for (var i = 0; i < run.ChoiceCount; i++)
            {
                if (run.ChoiceAt(i).Kind != ChoiceKind.Upgrade) { continue; }
                run.Choose(i);
                break;
            }

            while (run.IsChoosing) { run.Choose(0); }
        }

        /// <summary>レビューの例：Lv1（2 発・間隔 100・寿命 100）→ Lv2（2 発・間隔 1・寿命 1）。旧レベルの 2 発が残ったまま新レベルの 2 発を撃つ。</summary>
        [Test]
        public void UpgradeWhileOldShotsRemain_IsCountedByTheValidation()
        {
            var levels = new[] { Level(2, 100, 100), Level(2, 1, 1) };
            Assert.That(TideConfig.ProjectileCapacityOf(Only(4, levels).Weapons[0]), Is.EqualTo(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => Only(2, levels).Validate(), "レベルごとの最大（2 発）だけでは足りない");

            var run = new TideRun(Only(4, levels), 1);
            run.Place(Gnat, At(15 * T, 0));
            run.Step(PlayerCommand.Idle);
            Assert.That(run.Projectiles.Count, Is.EqualTo(2), "Lv1 で 2 発");
            Upgrade(run);
            Assert.That(run.WeaponLevelOf(0), Is.EqualTo(2));
            Assert.DoesNotThrow(() => run.Step(PlayerCommand.Idle), "Lv2 の発射で置き場があふれない（撃つ瞬間は旧 2 発 + 新 2 発）");
            Assert.That(run.Projectiles.Count, Is.EqualTo(2), "寿命 1 の新しい弾は、その Tick の終わりに消える");
        }

        /// <summary>寿命 2 なら Tick の終わりにも新旧が並ぶ。撃つ瞬間の最大は、旧 2 発 + 直前の新 2 発 + 今の 2 発。</summary>
        [Test]
        public void UpgradeWithLongerNewShots_KeepsOldAndNewTogether()
        {
            var levels = new[] { Level(2, 100, 100), Level(2, 1, 2) };
            Assert.That(TideConfig.ProjectileCapacityOf(Only(6, levels).Weapons[0]), Is.EqualTo(6));
            Assert.Throws<ArgumentOutOfRangeException>(() => Only(5, levels).Validate());
            var run = new TideRun(Only(6, levels), 1);
            run.Place(Gnat, At(15 * T, 0));
            run.Step(PlayerCommand.Idle);
            Upgrade(run);
            for (var i = 0; i < 20; i++)
            {
                Assert.DoesNotThrow(() => run.Step(PlayerCommand.Idle));
                Assert.That(run.Projectiles.Count, Is.EqualTo(4), "旧 2 発と新 2 発");
            }
        }

        [Test]
        public void DefaultBolt_FitsTheDefaultCapacity()
        {
            var config = new TideConfig();
            var capacity = TideConfig.ProjectileCapacityOf(config.Weapons[Bolt]);
            TestContext.WriteLine($"既定の Bolt の同時の弾数の上界 {capacity}（置き場 {config.MaxProjectiles}）");
            var simple = 0;
            foreach (var level in config.Weapons[Bolt].Levels) { simple = Math.Max(simple, level.Count * ((level.LifeTicks + level.IntervalTicks - 1) / level.IntervalTicks)); }
            Assert.That(capacity, Is.GreaterThanOrEqualTo(simple), "強化をまたがない場合の数以上");
            Assert.That(capacity, Is.LessThanOrEqualTo(config.MaxProjectiles));
            Assert.That(TideConfig.ProjectileCapacityOf(config.Weapons[Pulse]), Is.Zero);
        }

        /// <summary>
        /// 乱数の Bolt の表と、乱数の時機の強化（同じ Tick に 2 段上げることも含む）で、弾の数がいつも上界以下で例外にならない。
        /// 置き場をちょうど上界にした設定で確かめ、上界に届く場面があることも見る（上界が緩すぎないかの目安）。
        /// </summary>
        [Test]
        public void RandomTablesAndUpgradeTimings_NeverExceedTheBound()
        {
            var random = new SeededRandom(31);
            var reached = 0;
            for (var round = 0; round < 300; round++)
            {
                var levels = new WeaponLevel[random.Range(2, 4)];
                for (var l = 0; l < levels.Length; l++) { levels[l] = Level(random.Range(1, 3), random.Range(1, 20), random.Range(1, 40)); }
                var bound = TideConfig.ProjectileCapacityOf(new WeaponDef { Kind = WeaponKind.Bolt, Levels = levels });
                var run = new TideRun(Only(bound, levels), round);
                run.Place(Gnat, At(15 * T, 0));
                var most = 0;
                for (var tick = 0; tick < 400; tick++)
                {
                    if (run.WeaponLevelOf(0) < levels.Length && random.Next(12) == 0)
                    {
                        Upgrade(run);
                        if (run.WeaponLevelOf(0) < levels.Length && random.Next(3) == 0) { Upgrade(run); }
                    }
                    else
                    {
                        run.Step(PlayerCommand.Idle);
                    }

                    most = Math.Max(most, run.Projectiles.Count);
                    Assert.That(run.Projectiles.Count, Is.LessThanOrEqualTo(bound));
                }

                if (most == bound) { reached++; }
            }

            Assert.That(reached, Is.GreaterThan(30), "上界に届く場面もある");
        }

        [Test]
        public void SetWeaponLevel_IsRejectedWhileShotsRemain()
        {
            var run = new TideRun(Only(4, Level(2, 100, 100), Level(2, 1, 1)), 1);
            run.Place(Gnat, At(15 * T, 0));
            run.Step(PlayerCommand.Idle);
            Assert.Throws<InvalidOperationException>(() => run.SetWeaponLevel(0, 2));
        }
    }
}
