using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>拾う・使う・投げる・置く。</summary>
    public sealed class ItemTests
    {
        [Test]
        public void SteppingOnAnItemPicksItUpUnlessFull()
        {
            var run = Lab.Center(1);
            var dir = Lab.OpenDirection(run);
            var spot = run.Player.Position + dir.Offset();
            run.SetItem(spot, ItemKind.Bread);
            Assert.That(run.Submit(Command.Move(dir)).Accepted, Is.True);
            Assert.That(run.Player.Inventory, Is.EqualTo(new[] { ItemKind.Bread }));
            Assert.That(run.ItemAt(spot), Is.Null);
            Assert.That(Lab.EventsOf(run, EventKind.PickedUp), Has.Count.EqualTo(1));

            // 満杯なら拾わずに乗るだけ（移動は受け付ける）。
            var back = (Direction)(((int)dir + 4) % 8);
            run.SetItem(run.Player.Position + back.Offset(), ItemKind.Stone);
            while (run.Player.Inventory.Count < run.Config.InventoryCapacity) { run.Player.Inventory.Add(ItemKind.Bread); }
            Assert.That(run.Submit(Command.Move(back)).Accepted, Is.True);
            Assert.That(run.ItemAt(run.Player.Position), Is.EqualTo(ItemKind.Stone));
            Assert.That(Lab.EventsOf(run, EventKind.FullCannotPickUp), Has.Count.EqualTo(1));
            Assert.That(run.Submit(Command.Pickup()).Reason, Is.EqualTo(RejectReason.InventoryFull));

            // 1 つ置けば拾える。
            run.Player.Inventory.RemoveAt(0);
            Assert.That(run.Submit(Command.Pickup()).Accepted, Is.True);
            Assert.That(run.Player.Inventory[run.Player.Inventory.Count - 1], Is.EqualTo(ItemKind.Stone));
        }

        [Test]
        public void PotionsAndFoodAreCappedAndPoisonLeavesOneHp()
        {
            var run = Lab.Center(2);
            var p = run.Player;
            p.Inventory.AddRange(new[] { ItemKind.HealPotion, ItemKind.HealPotion, ItemKind.PoisonPotion, ItemKind.Bread, ItemKind.BigBread });
            p.Hp = p.MaxHp - 3;
            run.Submit(Command.Use(0));
            Assert.That(p.Hp, Is.EqualTo(p.MaxHp), "最大まで");
            Assert.That(Lab.EventsOf(run, EventKind.Healed)[0].Value, Is.EqualTo(3));

            p.Hp = 2;
            run.Submit(Command.Use(0));
            Assert.That(p.Hp, Is.LessThanOrEqualTo(2 + run.Config.HealAmount));
            Assert.That(p.Inventory[0], Is.EqualTo(ItemKind.PoisonPotion));

            p.Hp = 3;
            run.Submit(Command.Use(0));
            Assert.That(p.Hp, Is.EqualTo(1), "毒で死なない");

            p.Satiety = 30;
            run.Submit(Command.Use(0));
            Assert.That(p.Satiety, Is.EqualTo(30 + run.Config.BreadSatiety));
            run.Submit(Command.Use(0));
            Assert.That(p.Satiety, Is.EqualTo(run.Config.MaxSatiety));
            Assert.That(p.Inventory, Is.Empty);
        }

        [Test]
        public void SleepPotionMakesThePlayerSkipTurnsWhileMonstersAct()
        {
            var run = Lab.Center(3);
            run.Player.Inventory.Add(ItemKind.SleepPotion);
            var actions = run.Player.Actions;
            Assert.That(run.Submit(Command.Use(0)).Accepted, Is.True);
            Assert.That(run.Player.Actions - actions, Is.EqualTo(1 + run.Config.SleepTurns), "眠っている間は自動で足踏み");
            Assert.That(run.Player.SleepTurns, Is.EqualTo(0));
            Assert.That(Lab.EventsOf(run, EventKind.WokeUp), Has.Count.EqualTo(1));
            Assert.That(run.View.Asleep, Is.False);
        }

        [Test]
        public void ThrownStoneHitsTheFirstMonsterInLine()
        {
            var run = Lab.Center(4);
            var dir = Lab.OpenDirection(run);
            run.Player.Facing = dir;
            var golem = run.AddMonster(MonsterKind.Golem, run.Player.Position + dir.Offset());
            golem.SleepTurns = 10;
            run.Player.Inventory.Add(ItemKind.Stone);
            run.Refresh();
            var items = run.FloorItemCount;
            Assert.That(run.Submit(Command.Throw(0)).Accepted, Is.True);
            Assert.That(golem.Hp, Is.EqualTo(golem.MaxHp - run.Config.StoneDamage));
            Assert.That(run.Player.Inventory, Is.Empty);
            Assert.That(run.FloorItemCount, Is.EqualTo(items), "当たった品は消える");
        }

        [Test]
        public void ThrownSleepPotionPutsTheMonsterToSleep()
        {
            var run = Lab.Center(5);
            var dir = Lab.OpenDirection(run);
            run.Player.Facing = dir;
            var skitter = run.AddMonster(MonsterKind.Skitter, run.Player.Position + dir.Offset());
            run.Player.Inventory.Add(ItemKind.SleepPotion);
            run.Refresh();
            run.Submit(Command.Throw(0));
            Assert.That(skitter.SleepTurns, Is.GreaterThan(0));
            var hp = run.Player.Hp;
            run.Submit(Command.Wait());
            Assert.That(run.Player.Hp, Is.GreaterThanOrEqualTo(hp), "眠っている敵は攻撃しない");
        }

        [Test]
        public void MissedThrowLandsBeforeTheWallAndSpreadsWhenOccupied()
        {
            var run = Lab.Quiet(6);
            var spot = Lab.FloorFacingWall(run, Direction.North);
            run.Teleport(spot);
            run.Player.Facing = Direction.North;
            run.Player.Inventory.Add(ItemKind.Bread);
            run.Player.Inventory.Add(ItemKind.Stone);
            run.SetItem(spot, null);
            var items = run.FloorItemCount;
            run.Submit(Command.Throw(0));
            Assert.That(run.ItemAt(spot), Is.EqualTo(ItemKind.Bread), "壁に向けて投げると足元に落ちる");
            run.Submit(Command.Throw(0));
            Assert.That(run.FloorItemCount, Is.EqualTo(items + 2));
            Assert.That(Lab.EventsOf(run, EventKind.ThrowLanded)[0].Position, Is.Not.EqualTo(spot), "埋まっていれば近くへ");
        }

        [Test]
        public void ThrowStopsAtRangeAndDisappearsWhenTheFloorIsFull()
        {
            var config = new CrawlConfig { ThrowRange = 2, FloorItemCap = 11, ItemsBase = 5, ItemsPerFloor = 1 };
            var run = new CrawlRun(config, 7);
            run.ClearMonsters();
            run.Teleport(Lab.RoomInterior(run));
            var dir = Lab.OpenDirection(run);
            run.Player.Facing = dir;
            run.Player.Inventory.Add(ItemKind.Stone);
            run.Player.Inventory.Add(ItemKind.Stone);
            var two = run.Player.Position + dir.Offset() + dir.Offset();
            if (run.Grid.InBounds(two)) { run.SetItem(two, null); }
            var count = run.FloorItemCount;
            run.Submit(Command.Throw(0));
            Assert.That(run.FloorItemCount, Is.EqualTo(count + 1));
            var landed = Lab.EventsOf(run, EventKind.ThrowLanded)[0].Position;
            Assert.That(Directions.Distance(landed, run.Player.Position), Is.LessThanOrEqualTo(2), "射程 2 で止まる");

            // 上限まで埋めると消える。
            for (int y = 0; y < run.Grid.Height && run.FloorItemCount < config.FloorItemCap; y++)
            {
                for (int x = 0; x < run.Grid.Width && run.FloorItemCount < config.FloorItemCap; x++)
                {
                    var p = new Vec2i(x, y);
                    if (run.Grid.IsWalkable(p) && run.ItemAt(p) == null && p != run.Player.Position) { run.SetItem(p, ItemKind.Bread); }
                }
            }

            run.Submit(Command.Throw(0));
            Assert.That(Lab.EventsOf(run, EventKind.ItemLost), Has.Count.EqualTo(1));
            Assert.That(run.FloorItemCount, Is.EqualTo(config.FloorItemCap));
        }

        [Test]
        public void DropPutsTheItemUnderfoot()
        {
            var run = Lab.Center(8);
            run.Player.Inventory.Add(ItemKind.Stone);
            Assert.That(run.Submit(Command.Drop(0)).Accepted, Is.True);
            Assert.That(run.ItemAt(run.Player.Position), Is.EqualTo(ItemKind.Stone));
            Assert.That(run.Player.Inventory, Is.Empty);
        }

        [Test]
        public void WarpScrollMovesThePlayerToAnotherFreeTile()
        {
            var run = Lab.Center(9);
            var from = run.Player.Position;
            run.Player.Inventory.Add(ItemKind.WarpScroll);
            run.Submit(Command.Use(0));
            Assert.That(run.Player.Position, Is.Not.EqualTo(from));
            Assert.That(run.Grid.IsWalkable(run.Player.Position), Is.True);
            Assert.That(run.View.IsVisible(run.Player.Position), Is.True, "視界も計算し直す");
        }
    }
}
