using System.Collections.Generic;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有テスト「死亡敵の二重ドロップなし」。</summary>
    public sealed class DropTests
    {
        [Test]
        public void SwingAndSpinLandingOnTheSameTickDropOnce()
        {
            var run = Lab.NewRun(3);
            Lab.Center(run);
            var foe = Lab.Isolate(run, FoeKind.Charger, 300, life: 1);
            var aim = foe.Position;
            var config = run.Config;

            // 薙ぎ払い（構え 10）を先に、基本攻撃（構え 6）を 4 Tick 後に押すと同じ Tick に当たる。
            run.Step(new HeroCommand(0, 0, aim, false, skill: true));
            long spinTick = run.Hero.SpinHitTick;
            for (int i = 0; i < config.SkillWindup - config.AttackWindup - 1; i++) { run.Step(new HeroCommand(0, 0, aim, false)); }
            run.Step(new HeroCommand(0, 0, aim, true));
            Assert.That(run.Hero.SwingHitTick, Is.EqualTo(spinTick), "2 つの攻撃が同じ Tick に当たる場面");
            while (run.Tick < spinTick) { run.Step(new HeroCommand(0, 0, aim, false)); }

            Assert.That(foe.Dead, Is.True);
            Assert.That(run.Record.DropRolls, Is.EqualTo(1));
            Assert.That(run.Record.Kills, Is.EqualTo(1));
        }

        [Test]
        public void HittingADeadFoeAgainDoesNotDrop()
        {
            var run = Lab.NewRun(3);
            Lab.Center(run);
            var foe = Lab.Isolate(run, FoeKind.Grunt, 300, life: 1);
            run.HitFoe(foe, 100);
            Assert.That(foe.Dead, Is.True);
            int ground = run.Ground.Count;

            run.HitFoe(foe, 100);
            run.HitFoe(foe, 1000);
            for (int i = 0; i < 120; i++) { run.Step(new HeroCommand(0, 0, foe.Position, true, skill: true)); }

            Assert.That(run.Record.DropRolls, Is.EqualTo(1));
            Assert.That(run.Record.Kills, Is.EqualTo(1));
            Assert.That(run.Ground.Count, Is.EqualTo(ground));
        }

        [Test]
        public void DyingAndRespawningDoesNotReviveOrRedropKilledFoes()
        {
            var run = Lab.NewRun(5);
            Lab.Center(run);
            var foe = Lab.Isolate(run, FoeKind.Grunt, 300, life: 1);
            run.HitFoe(foe, 100);
            Assert.That(run.Record.DropRolls, Is.EqualTo(1));

            run.HurtHero(100000, Element.Physical);
            Assert.That(run.Hero.Dead, Is.True);
            Lab.Wait(run, run.Config.DeathTicks + 1);
            Assert.That(run.Hero.Dead, Is.False);
            Assert.That(foe.Dead, Is.True, "倒した敵は復活しない");
            Assert.That(run.Record.DropRolls, Is.EqualTo(1));
        }

        [Test]
        public void ClonesDropTheSameItemsOnceEach()
        {
            var run = Lab.NewRun(8);
            Lab.Center(run);
            var foe = Lab.Isolate(run, FoeKind.Charger, 300, life: 1);
            int index = IndexOf(run, foe);
            var copy = run.Clone();
            run.HitFoe(foe, 100);
            copy.HitFoe((Foe)copy.Foes[index], 100);
            Assert.That(run.Record.DropRolls, Is.EqualTo(1));
            Assert.That(copy.Record.DropRolls, Is.EqualTo(1));
            Assert.That(Keys(copy), Is.EqualTo(Keys(run)));

            var after = run.Clone();
            after.HitFoe((Foe)after.Foes[index], 100);
            Assert.That(after.Record.DropRolls, Is.EqualTo(1), "倒れた後に複製しても印が残る");
            Assert.That(run.Record.DropRolls, Is.EqualTo(1));
        }

        [Test]
        public void DropsDoNotDependOnKillOrder()
        {
            var a = Lab.NewRun(11);
            var b = Lab.NewRun(11);
            var foesA = new List<Foe>(a.Foes);
            var foesB = new List<Foe>(b.Foes);
            foreach (var f in foesA) { f.Life = 1; }
            foreach (var f in foesB) { f.Life = 1; }
            for (int i = 0; i < foesA.Count; i++) { a.HitFoe(foesA[i], 100); }
            for (int i = foesB.Count - 1; i >= 0; i--) { b.HitFoe(foesB[i], 100); }

            Assert.That(a.Record.DropRolls, Is.EqualTo(foesA.Count));
            Assert.That(b.Record.DropRolls, Is.EqualTo(foesB.Count));
            var byFoeA = new SortedSet<string>(Keys(a));
            var byFoeB = new SortedSet<string>(Keys(b));
            Assert.That(byFoeB, Is.EqualTo(byFoeA));
        }

        [Test]
        public void GroundCapDiscardsExtraDropsAndCountsThem()
        {
            var config = new DelveConfig { GroundCap = 2 };
            var run = Lab.NewRun(2, config);
            foreach (var f in new List<Foe>(run.Foes))
            {
                f.Life = 1;
                run.HitFoe(f, 100);
            }

            Assert.That(run.Ground.Count, Is.LessThanOrEqualTo(2));
            Assert.That(run.Ground.Count + run.Record.ItemsLost, Is.GreaterThan(2), "上限を超えた分が数えられている");
        }

        private static int IndexOf(DelveRun run, Foe foe)
        {
            for (int i = 0; i < run.Foes.Count; i++)
            {
                if (ReferenceEquals(run.Foes[i], foe)) { return i; }
            }

            return -1;
        }

        private static List<string> Keys(DelveRun run)
        {
            var keys = new List<string>();
            foreach (var g in run.Ground)
            {
                keys.Add(g.Position + (g.IsPotion ? "P" : g.Item!.Name + string.Join(",", Values(g.Item))));
            }

            return keys;
        }

        private static IEnumerable<int> Values(Item item)
        {
            foreach (var a in item.Affixes)
            {
                foreach (var v in a.Values) { yield return v; }
            }
        }
    }

    /// <summary>戦闘の式と時間の境界。</summary>
    public sealed class CombatTests
    {
        [Test]
        public void ProjectileHitsAlongTheHeroMovementPath()
        {
            // 自機が (0, 0) → (24, 0)、弾が (-118, -83) → (-118, -43)。半径の和 144 に対し、相対軌跡の最短距離は約 143.89 で触れる。
            // 移動後の位置だけで掃引すると最短距離は約 148.37 で外れる（レビューの再現例）。
            var run = Lab.NewRun();
            Lab.Center(run);
            Lab.Isolate(run, FoeKind.Grunt, 20 * 256);
            var origin = run.Hero.Position;
            int radiusSum = 48 + run.Config.HeroRadius;
            Assert.That(radiusSum, Is.EqualTo(144));
            Assert.That(run.StepLength, Is.EqualTo(24));
            var from = origin + new Vec2i(-118, -83);
            var to = origin + new Vec2i(-118, -43);
            var after = origin + new Vec2i(24, 0);
            Assert.That(Sweep.Touches(from, to, after, after, radiusSum), Is.False, "移動後の位置だけでは外れる場面");
            run.AddProjectileForTest(new Projectile(from, to - from, 48, 9, Element.Cold, FoeKind.Archer));
            int life = run.Hero.Life;
            run.Step(new HeroCommand(1, 0, after + new Vec2i(256, 0), false));
            Assert.That(run.Hero.Position, Is.EqualTo(after));
            Assert.That(run.Hero.Life, Is.LessThan(life), "移動の経路でかすめた弾が当たる");
            Assert.That(run.Projectiles, Is.Empty);
        }

        [Test]
        public void PhysicalDamageIsReducedByArmorWithMinimumOne()
        {
            var run = Lab.NewRun();
            int life = run.Hero.Life;
            run.HurtHero(10, Element.Physical);
            Assert.That(run.Hero.Life, Is.EqualTo(life - 10), "防御 0 ならそのまま");

            var armored = Lab.NewRun();
            GiveArmor(armored, 100);
            int before = armored.Hero.Life;
            armored.HurtHero(10, Element.Physical);
            Assert.That(armored.Hero.Life, Is.EqualTo(before - 5), "10 × 100 / (100 + 100)");

            var tiny = Lab.NewRun();
            GiveArmor(tiny, 9999);
            int tinyLife = tiny.Hero.Life;
            tiny.HurtHero(1, Element.Physical);
            Assert.That(tiny.Hero.Life, Is.EqualTo(tinyLife - 1), "最低 1");
        }

        [Test]
        public void ElementalDamageUsesResistanceUpToSeventyFivePercent()
        {
            var run = Lab.NewRun();
            var ring = new Item(run.Loot.Base("emberring"), Rarity.Magic, new[]
            {
                new RolledAffix(run.Loot.Affix("ember"), 2, new List<int> { 30 }),
            });
            var amulet = new Item(run.Loot.Base("boneamulet"), Rarity.Magic, new[]
            {
                new RolledAffix(run.Loot.Affix("ember"), 2, new List<int> { 30 }),
            });
            run.EquipForTest(ring);
            run.EquipForTest(amulet);
            Assert.That(run.Stats.FireResist, Is.EqualTo(75), "15 + 30 + 30 = 75（上限）");
            int life = run.Hero.Life;
            run.HurtHero(40, Element.Fire);
            Assert.That(run.Hero.Life, Is.EqualTo(life - 10));
            run.Hero.Invuln = 0;
            run.HurtHero(40, Element.Cold);
            Assert.That(run.Hero.Life, Is.EqualTo(life - 10 - 40), "冷気耐性は 0");
        }

        [Test]
        public void InvulnerabilityBlocksHitsUntilItRunsOut()
        {
            var run = Lab.NewRun();
            Lab.Center(run);
            Lab.Isolate(run, FoeKind.Grunt, 20 * 256);
            int ticks = run.Config.InvulnTicks;
            run.HurtHero(5, Element.Physical);
            int life = run.Hero.Life;
            for (int i = 0; i < ticks - 1; i++)
            {
                run.Step(Lab.Idle(run));
                run.HurtHero(5, Element.Physical);
                Assert.That(run.Hero.Life, Is.EqualTo(life), $"{i + 1} Tick 後はまだ無敵");
            }

            run.Step(Lab.Idle(run));
            run.HurtHero(5, Element.Physical);
            Assert.That(run.Hero.Life, Is.EqualTo(life - 5), "無敵が切れた Tick から当たる");
        }

        [Test]
        public void AttackIntervalFollowsAttackSpeed()
        {
            var run = Lab.NewRun();
            Assert.That(run.AttackInterval, Is.EqualTo(30));
            var dagger = new Item(run.Loot.Base("dagger"), Rarity.Normal, System.Array.Empty<RolledAffix>());
            run.EquipForTest(dagger);
            Assert.That(run.Stats.AttackSpeed, Is.EqualTo(120));
            Assert.That(run.AttackInterval, Is.EqualTo(25), "30 × 100 / 120");

            Lab.Center(run);
            Lab.Isolate(run, FoeKind.Grunt, 20 * 256);
            var swings = new List<long>();
            for (int i = 0; i < 100; i++)
            {
                run.Step(new HeroCommand(0, 0, run.Hero.Position + new Vec2i(256, 0), true));
                if ((run.TakeEvents() & RunEvents.Swing) != 0) { swings.Add(run.Tick); }
            }

            for (int i = 1; i < swings.Count; i++) { Assert.That(swings[i] - swings[i - 1], Is.EqualTo(25)); }
        }

        [Test]
        public void LeechHealsButNeverAboveMaxLife()
        {
            var run = Lab.NewRun();
            Lab.Center(run);
            var weapon = new Item(run.Loot.Base("shortsword"), Rarity.Magic, new[]
            {
                new RolledAffix(run.Loot.Affix("thirst"), 2, new List<int> { 5 }),
            });
            run.EquipForTest(weapon);
            var foe = Lab.Isolate(run, FoeKind.Grunt, 300, life: 100000);
            run.Hero.Life = run.Stats.Life - 1;
            for (int i = 0; i < 50; i++) { run.HitFoe(foe, 1000); }
            Assert.That(run.Hero.Life, Is.EqualTo(run.Stats.Life));
        }

        [Test]
        public void MovingDiagonallyUsesAtLeastOneUnit()
        {
            var config = new DelveConfig { HeroSpeed = 1 };
            var run = Lab.NewRun(1, config);
            Lab.Center(run);
            Lab.Isolate(run, FoeKind.Grunt, 20 * 256);
            var start = run.Hero.Position;
            run.Step(new HeroCommand(1, -1, start, false));
            Assert.That(run.Hero.Position, Is.EqualTo(start + new Vec2i(1, -1)));
        }

        [Test]
        public void HeroStopsAtWalls()
        {
            var run = Lab.NewRun();
            run.Hero.Position = run.Map.Entrance;
            for (int i = 0; i < 200; i++) { run.Step(new HeroCommand(-1, 1, run.Hero.Position, false)); }
            var box = run.HeroBox();
            Assert.That(box.Left, Is.EqualTo(256), "西の壁の手前");
            Assert.That(box.Top, Is.EqualTo((run.Map.Height - 1) * 256), "北の壁の手前");
        }

        /// <summary>防御だけを置き換える（式だけを確かめる）。</summary>
        private static void GiveArmor(DelveRun run, int armor)
        {
            var s = run.Stats;
            run.SetStatsForTest(new StatBlock(new[]
            {
                s.Life, s.DamageMin, s.DamageMax, s.AttackSpeed, s.MoveSpeed, armor, s.CritChance, s.CritDamage, s.Leech, s.FireResist, s.ColdResist,
            }));
        }
    }
}
