using System;
using System.Collections.Generic;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>死亡と復活・階の移動・勝利・再現性。</summary>
    public sealed class RunTests
    {
        [Test]
        public void DeathRespawnsAtFloorEntranceWithFullLifeAndKeepsItems()
        {
            var run = Lab.NewRun(4);
            Lab.Center(run);
            var killed = Lab.Isolate(run, FoeKind.Grunt, 300, life: 1);
            run.HitFoe(killed, 100);
            var alive = (Foe)run.Foes[1];
            alive.Dead = false;
            alive.Dropped = false;
            alive.Position = run.Hero.Position + new Vec2i(0, 600);
            alive.Life = 3;
            alive.Aggro = true;
            var weapon = run.Equipment[Slot.Weapon];

            run.HurtHero(100000, Element.Physical);
            Assert.That(run.Hero.Dead, Is.True);
            Assert.That(run.Record.Deaths, Is.EqualTo(1));
            Lab.Wait(run, run.Config.DeathTicks - 1);
            Assert.That(run.Hero.Dead, Is.True, "まだ倒れている");
            Lab.Wait(run, 1);

            Assert.That(run.Hero.Dead, Is.False);
            Assert.That(run.Hero.Position, Is.EqualTo(run.Map.Entrance));
            Assert.That(run.Hero.Life, Is.EqualTo(run.Stats.Life));
            Assert.That(run.Equipment[Slot.Weapon], Is.SameAs(weapon));
            Assert.That(killed.Dead, Is.True);
            Assert.That(alive.Life, Is.EqualTo(alive.MaxLife), "生きている敵は体力が戻る");
            Assert.That(alive.Position, Is.EqualTo(alive.Spawn.Center), "生きている敵は初めの位置へ戻る");
            Assert.That(alive.Aggro, Is.False);
        }

        [Test]
        public void StairsLeadToTheNextFloorAndClearTheGround()
        {
            var run = Lab.NewRun(6);
            var foe = Lab.Isolate(run, FoeKind.Grunt, 300, life: 1);
            run.HitFoe(foe, 100);
            var map = run.Map;
            run.Hero.Position = FloorMap.TileCenter(map.StairsColumn - 1, map.StairsRow);
            for (int i = 0; i < 30 && run.FloorNumber == 1; i++) { run.Step(new HeroCommand(1, 0, run.Hero.Position, false)); }

            Assert.That(run.FloorNumber, Is.EqualTo(2));
            Assert.That(run.Hero.Position, Is.EqualTo(run.Map.Entrance));
            Assert.That(run.Ground, Is.Empty);
            Assert.That(run.Foes.Count, Is.EqualTo(run.Map.Spawns.Count));
            foreach (var f in run.Foes)
            {
                Assert.That(f.Dead, Is.False);
                Assert.That(f.MaxLife, Is.EqualTo(run.Config.Foe(f.Kind).Life * run.Config.FloorLifePercent[1] / 100));
            }
        }

        [Test]
        public void KillingTheBossWinsAndEndsAfterTheDelay()
        {
            var run = Lab.NewRun(2);
            Enter(run, 3);
            Foe boss = null!;
            foreach (var f in run.Foes)
            {
                if (f.Kind == FoeKind.Boss) { boss = f; }
                else
                {
                    f.Dead = true;
                    f.Dropped = true;
                }
            }

            Assert.That(boss.MaxLife, Is.EqualTo(run.Config.Foe(FoeKind.Boss).Life), "番人には階の倍率を掛けない");
            boss.Life = 1;
            run.HitFoe(boss, 100);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Won));
            int rares = 0;
            foreach (var g in run.Ground) { if (!g.IsPotion && g.Item!.Rarity == Rarity.Rare) { rares++; } }
            Assert.That(rares, Is.EqualTo(2), "番人は希少を 2 つ落とす");

            int delay = run.Config.VictoryDelay;
            Lab.Wait(run, delay - 1);
            Assert.That(run.IsOver, Is.False);
            Lab.Wait(run, 1);
            Assert.That(run.IsOver, Is.True);

            long tick = run.Tick;
            uint hash = run.Hash();
            Lab.Wait(run, 10);
            Assert.That(run.Tick, Is.EqualTo(tick), "終わった後は進まない");
            Assert.That(run.Hash(), Is.EqualTo(hash));
            Assert.That(run.Equip(0), Is.EqualTo(ItemResult.NotFound).Or.EqualTo(ItemResult.NotAllowed));
            Assert.That(run.PickUp(1), Is.EqualTo(ItemResult.NotAllowed));
        }

        [Test]
        public void BossResetsToFullLifeWhenTheHeroDies()
        {
            var run = Lab.NewRun(2);
            Enter(run, 3);
            Foe boss = null!;
            foreach (var f in run.Foes) { if (f.Kind == FoeKind.Boss) { boss = f; } }
            boss.Life = 10;
            run.HurtHero(100000, Element.Physical);
            Lab.Wait(run, run.Config.DeathTicks);
            Assert.That(boss.Life, Is.EqualTo(boss.MaxLife));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Playing));
        }

        [Test]
        public void SameSeedAndCommandsGiveTheSameHashAndClonesFollow()
        {
            var a = Lab.NewRun(9);
            var b = Lab.NewRun(9);
            var rng = new SeededRandom(1);
            DelveRun? clone = null;
            var commands = new List<HeroCommand>();
            for (int i = 0; i < 3000; i++)
            {
                var c = new HeroCommand(rng.Range(-1, 1), rng.Range(-1, 1), a.Hero.Position + new Vec2i(rng.Range(-500, 500), rng.Range(-500, 500)), rng.Next(3) == 0, rng.Next(50) == 0, rng.Next(200) == 0, rng.Next(30) == 0);
                commands.Add(c);
                a.Step(c);
                b.Step(c);
                if (i == 1500) { clone = a.Clone(); }
                if (i > 1500) { clone!.Step(c); }
            }

            Assert.That(b.Hash(), Is.EqualTo(a.Hash()));
            Assert.That(clone!.Hash(), Is.EqualTo(a.Hash()));
            Assert.That(Lab.NewRun(10).Hash(), Is.Not.EqualTo(Lab.NewRun(9).Hash()), "シードが違えば別の状態");
        }

        [Test]
        public void HashDiffersWhenRulesDiffer()
        {
            var a = Lab.NewRun(1);
            var b = Lab.NewRun(1, new DelveConfig { PotionHealPercent = 41 });
            Assert.That(b.Hash(), Is.Not.EqualTo(a.Hash()), "設定の指紋");
            var c = new DelveRun(new DelveConfig(), LootData.Parse(Lab.DataText("Loot.txt").Replace("hero life 100", "hero life 101", StringComparison.Ordinal)), Lab.Floors(), 1);
            Assert.That(c.Hash(), Is.Not.EqualTo(a.Hash()), "戦利品の指紋");
        }

        [Test]
        public void HashCoversPendingState()
        {
            // 今の位置が同じでも以後が分かれる状態（攻撃の待ち・薙ぎ払いの待ち・無敵・薬）でハッシュが違う（022）。
            var baseRun = Lab.NewRun(1);
            uint h = baseRun.Hash();
            var a = baseRun.Clone();
            a.Hero.AttackCooldown = 3;
            var b = baseRun.Clone();
            b.Hero.SkillCooldown = 3;
            var c = baseRun.Clone();
            c.Hero.Invuln = 3;
            var d = baseRun.Clone();
            d.Hero.Potions = 0;
            foreach (var x in new[] { a, b, c, d }) { Assert.That(x.Hash(), Is.Not.EqualTo(h)); }
        }

        internal static void Enter(DelveRun run, int floor)
        {
            while (run.FloorNumber < floor)
            {
                var map = run.Map;
                run.Hero.Position = FloorMap.TileCenter(map.StairsColumn, map.StairsRow);
                run.Step(Lab.Idle(run));
            }
        }
    }

    /// <summary>拾う・装備する・捨てる。</summary>
    public sealed class ItemOpsTests
    {
        [Test]
        public void PickNearestWithinRangeOnly()
        {
            var run = Lab.NewRun(1);
            Lab.Center(run);
            Lab.Isolate(run, FoeKind.Grunt, 20 * 256);
            var item = Make(run, "ironhelm");
            var far = run.AddGroundForTest(run.Hero.Position + new Vec2i(run.Config.PickupRange + 10, 0), item);
            run.Step(new HeroCommand(0, 0, run.Hero.Position, false, pick: true));
            Assert.That(run.Inventory, Is.Empty, "範囲の外は拾わない");

            var near = run.AddGroundForTest(run.Hero.Position + new Vec2i(0, run.Config.PickupRange - 10), item);
            run.Step(new HeroCommand(0, 0, run.Hero.Position, false, pick: true));
            Assert.That(run.Inventory.Count, Is.EqualTo(1));
            Assert.That(ContainsId(run, near.Id), Is.False);
            Assert.That(ContainsId(run, far.Id), Is.True);
        }

        [Test]
        public void FullInventoryRefusesAndKeepsTheItemOnTheGround()
        {
            var run = Lab.NewRun(1, new DelveConfig { InventorySize = 1 });
            Lab.Center(run);
            Lab.Isolate(run, FoeKind.Grunt, 20 * 256);
            var a = run.AddGroundForTest(run.Hero.Position, Make(run, "hood"));
            var b = run.AddGroundForTest(run.Hero.Position, Make(run, "ironhelm"));
            Assert.That(run.PickUp(a.Id), Is.EqualTo(ItemResult.Done));
            Assert.That(run.PickUp(b.Id), Is.EqualTo(ItemResult.InventoryFull));
            Assert.That(ContainsId(run, b.Id), Is.True);
            Assert.That(run.PickUp(a.Id), Is.EqualTo(ItemResult.NotFound), "同じ品は 2 回拾えない");
        }

        [Test]
        public void ClickPickupRespectsDistance()
        {
            var run = Lab.NewRun(1);
            Lab.Center(run);
            Lab.Isolate(run, FoeKind.Grunt, 20 * 256);
            int r = run.Config.ClickPickupRange;
            var edge = run.AddGroundForTest(run.Hero.Position + new Vec2i(r, 0), Make(run, "hood"));
            var beyond = run.AddGroundForTest(run.Hero.Position + new Vec2i(0, r + 1), Make(run, "hood"));
            Assert.That(run.PickUp(beyond.Id), Is.EqualTo(ItemResult.TooFar));
            Assert.That(run.PickUp(edge.Id), Is.EqualTo(ItemResult.Done), "ちょうどの距離は拾える");
            Assert.That(run.PickUp(999999), Is.EqualTo(ItemResult.NotFound));
        }

        [Test]
        public void PotionsArePickedByTouchUpToTheLimit()
        {
            var run = Lab.NewRun(1, new DelveConfig { StartPotions = 4, MaxPotions = 5 });
            Lab.Center(run);
            Lab.Isolate(run, FoeKind.Grunt, 20 * 256);
            run.AddGroundForTest(run.Hero.Position, null);
            run.AddGroundForTest(run.Hero.Position, null);
            run.Step(Lab.Idle(run));
            Assert.That(run.Hero.Potions, Is.EqualTo(5));
            Assert.That(run.Ground.Count, Is.EqualTo(1), "持てない薬は地面に残る");
        }

        [Test]
        public void PotionHealsAndRespectsCooldownAndFullLife()
        {
            var run = Lab.NewRun(1, new DelveConfig { StartPotions = 3 });
            Lab.Center(run);
            Lab.Isolate(run, FoeKind.Grunt, 20 * 256);
            run.Step(new HeroCommand(0, 0, run.Hero.Position, false, potion: true));
            Assert.That(run.Hero.Potions, Is.EqualTo(3), "満タンでは使わない");
            run.Hero.Life = 10;
            run.Step(new HeroCommand(0, 0, run.Hero.Position, false, potion: true));
            Assert.That(run.Hero.Life, Is.EqualTo(10 + (run.Stats.Life * 40 / 100)));
            run.Hero.Life = 10;
            run.Step(new HeroCommand(0, 0, run.Hero.Position, false, potion: true));
            Assert.That(run.Hero.Potions, Is.EqualTo(2), "待ちの間は使えない");
        }

        [Test]
        public void EquipSwapsWithInventoryAndRecomputesStats()
        {
            var run = Lab.NewRun(1);
            var axe = Make(run, "warmaul");
            run.AddInventoryForTest(axe);
            var oldWeapon = run.Equipment[Slot.Weapon]!;
            Assert.That(run.Equip(0), Is.EqualTo(ItemResult.Done));
            Assert.That(run.Equipment[Slot.Weapon], Is.SameAs(axe));
            Assert.That(run.Inventory[0], Is.SameAs(oldWeapon), "前の武器は同じ番号へ");
            Assert.That(run.Stats, Is.EqualTo(StatSheet.Compute(run.Loot.Hero, run.Equipment)));
            Assert.That(run.Equip(5), Is.EqualTo(ItemResult.NotFound));
            Assert.That(run.Equip(-1), Is.EqualTo(ItemResult.NotFound));
        }

        [Test]
        public void UnequippingLifeClampsCurrentLife()
        {
            var run = Lab.NewRun(1);
            var helm = Make(run, "hornhelm");
            run.AddInventoryForTest(helm);
            run.Equip(0);
            run.Hero.Life = run.Stats.Life;
            Assert.That(run.Unequip(Slot.Helm), Is.EqualTo(ItemResult.Done));
            Assert.That(run.Hero.Life, Is.EqualTo(run.Stats.Life), "最大 HP が減ったら今の HP も上限で止まる");
            Assert.That(run.Unequip(Slot.Helm), Is.EqualTo(ItemResult.NotFound));
        }

        [Test]
        public void UnequipNeedsRoomAndDiscardNeedsGroundRoom()
        {
            var run = Lab.NewRun(1, new DelveConfig { InventorySize = 1, GroundCap = 1 });
            Lab.Isolate(run, FoeKind.Grunt, 20 * 256);
            run.AddInventoryForTest(Make(run, "hood"));
            Assert.That(run.Unequip(Slot.Weapon), Is.EqualTo(ItemResult.InventoryFull));
            Assert.That(run.Equipment[Slot.Weapon], Is.Not.Null);
            Assert.That(run.Discard(0), Is.EqualTo(ItemResult.Done));
            Assert.That(run.Inventory, Is.Empty);
            Assert.That(run.Ground.Count, Is.EqualTo(1));
            run.AddInventoryForTest(Make(run, "hood"));
            Assert.That(run.Discard(0), Is.EqualTo(ItemResult.GroundFull), "地面が満杯なら捨てない（品を消さない）");
            Assert.That(run.Inventory.Count, Is.EqualTo(1));
        }

        [Test]
        public void PublicListsAreReadOnly()
        {
            var run = Lab.NewRun(1);
            Assert.That(run.Foes is List<Foe>, Is.False);
            Assert.That(run.Ground is List<GroundItem>, Is.False);
            Assert.That(run.Inventory is List<Item>, Is.False);
            Assert.That(run.Projectiles is List<Projectile>, Is.False);
        }

        private static Item Make(DelveRun run, string id) => new(run.Loot.Base(id), Rarity.Normal, Array.Empty<RolledAffix>());

        private static bool ContainsId(DelveRun run, long id)
        {
            foreach (var g in run.Ground)
            {
                if (g.Id == id) { return true; }
            }

            return false;
        }
    }

    /// <summary>地図と設定の検証。</summary>
    public sealed class SetupTests
    {
        [Test]
        public void DefaultFloorsParse()
        {
            var floors = Lab.Floors();
            Assert.That(floors.Floors.Count, Is.EqualTo(3));
            Assert.That(floors[1].HasStairs && floors[2].HasStairs && !floors[3].HasStairs, Is.True);
            var kinds = new HashSet<FoeKind>();
            foreach (var map in floors.Floors)
            {
                foreach (var s in map.Spawns) { kinds.Add(s.Kind); }
            }

            Assert.That(kinds.Count, Is.EqualTo(4), "敵 3 種と番人");
        }

        [TestCase("floor 1 a\n##\n#@\nend", "階は")]
        [TestCase("floor 1 a\n###\n#@#\n#>\nend", "幅")]
        [TestCase("floor 2 a\n###\nend", "番号順")]
        [TestCase("floor 1 a\n#####\n#@#>#\n#####\nend", "届きません")]
        [TestCase("floor 1 a\n#####\n#@#g#\n#####\nend", "届きません")]
        [TestCase("floor 1 a\n####\n#@x#\n####\nend", "不明な文字")]
        [TestCase("floor 1 a\n####\n#..#\n####\nend", "入口")]
        [TestCase("floor 1 a\n####\n#@@#\n####\nend", "入口が 2 つ")]
        [TestCase("floor 1 a\n####\n#@.#\n####", "end")]
        public void RejectsBrokenFloors(string text, string reason)
        {
            var ex = Assert.Throws<FormatException>(() => FloorSet.Parse(text));
            Assert.That(ex!.Message.Contains(reason, StringComparison.Ordinal), Is.True, ex.Message);
        }

        [Test]
        public void RejectsMisplacedStairsAndBoss()
        {
            var text = Lab.DataText("Floors.txt");
            var noStairs = text.Replace("#.................#......a......#..>...#", "#.................#......a......#......#", StringComparison.Ordinal);
            Assert.That(noStairs, Is.Not.EqualTo(text));
            Assert.Throws<FormatException>(() => FloorSet.Parse(noStairs));
            var noBoss = text.Replace("..B.......#", "..........#", StringComparison.Ordinal);
            Assert.That(noBoss, Is.Not.EqualTo(text));
            Assert.Throws<FormatException>(() => FloorSet.Parse(noBoss));
        }

        [Test]
        public void DefaultConfigIsValidAndCopiesAreIndependent()
        {
            var config = new DelveConfig();
            Assert.DoesNotThrow(config.Validate);
            var copy = config.Copy();
            copy.Foes[0].Life = 1;
            copy.FloorLifePercent[0] = 5;
            Assert.That(config.Foes[0].Life, Is.Not.EqualTo(1));
            Assert.That(config.FloorLifePercent[0], Is.EqualTo(100));
            Assert.That(copy.Fingerprint(), Is.Not.EqualTo(config.Fingerprint()));

            var run = Lab.NewRun(1, config);
            config.Foes[0].Life = 1;
            Assert.That(run.Config.Foes[0].Life, Is.Not.EqualTo(1), "試合は設定の複製を使う");
        }

        [Test]
        public void RejectsInvalidConfig()
        {
            void Reject(Action<DelveConfig> edit, string reason)
            {
                var config = new DelveConfig();
                edit(config);
                var ex = Assert.Throws<ArgumentException>(config.Validate);
                Assert.That(ex!.Message.Contains(reason, StringComparison.Ordinal), Is.True, ex.Message);
            }

            Reject(c => c.HeroSpeed = 0, nameof(DelveConfig.HeroSpeed));
            Reject(c => c.StartPotions = c.MaxPotions + 1, nameof(DelveConfig.StartPotions));
            Reject(c => c.ClickPickupRange = c.PickupRange - 1, nameof(DelveConfig.ClickPickupRange));
            Reject(c => c.FloorLifePercent = new List<int> { 100, 100 }.ToArray(), nameof(DelveConfig.FloorLifePercent));
            Reject(c => c.Foes = new FoeStats[3], "Foes");
            Reject(c => c.Foes[1].ProjectileSpeed = 0, "ProjectileSpeed");
            Reject(c => c.Foes[2].ChargeSpeed = 0, "ChargeSpeed");
            Reject(c => c.Foes[1].Keep = c.Foes[1].Range + 1, "Keep");
            Reject(c => c.StartWeapon = string.Empty, nameof(DelveConfig.StartWeapon));
            Reject(c => c.BossFanSpread = Trig.FullTurn, nameof(DelveConfig.BossFanSpread));
            Reject(c => c.HeroBox = 48, "自機の箱");
            Assert.Throws<KeyNotFoundException>(() => Lab.NewRun(1, new DelveConfig { StartWeapon = "nothing" }));
        }
    }
}
