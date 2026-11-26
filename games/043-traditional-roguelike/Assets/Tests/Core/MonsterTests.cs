using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>敵 5 種の考え。</summary>
    public sealed class MonsterTests
    {
        /// <summary>自機から直線（方向）に dist マス離れた床に敵を置く。途中が床でなければ null。</summary>
        private static Monster? InLine(CrawlRun run, MonsterKind kind, Direction dir, int dist)
        {
            var p = run.Player.Position;
            for (int i = 0; i < dist; i++)
            {
                if (!run.Grid.CanStep(p, dir)) { return null; }
                p += dir.Offset();
            }

            var m = run.AddMonster(kind, p);
            run.Refresh();
            return m;
        }

        private static (CrawlRun Run, Monster Monster, Direction Dir) LineScene(MonsterKind kind, int dist)
        {
            for (int seed = 1; seed <= 60; seed++)
            {
                var run = Lab.Center(seed);
                for (int d = 0; d < Directions.Count; d++)
                {
                    var m = InLine(run, kind, (Direction)d, dist);
                    if (m != null) { return (run, m, (Direction)d); }
                }
            }

            throw new System.InvalidOperationException("場面が作れません。");
        }

        [Test]
        public void AwakeMonsterThatSeesThePlayerApproaches()
        {
            var (run, m, _) = LineScene(MonsterKind.Skitter, 4);
            var before = Directions.Distance(m.Position, run.Player.Position);
            run.Submit(Command.Wait());
            Assert.That(Directions.Distance(m.Position, run.Player.Position), Is.LessThan(before));
            Assert.That(m.LastSeen, Is.EqualTo(run.Player.Position));
        }

        [Test]
        public void SleepingSerpentStaysUntilThePlayerIsAdjacent()
        {
            var (run, m, dir) = LineScene(MonsterKind.Serpent, 2);
            m.Asleep = true;
            var at = m.Position;
            for (int i = 0; i < 5; i++) { run.Submit(Command.Wait()); }
            Assert.That(m.Position, Is.EqualTo(at));
            Assert.That(m.Asleep, Is.True);
            Assert.That(run.View.Monsters[0].Sleeping, Is.True);

            // 隣に来ると、大蛇の番で起きる（その番は動かない）。
            run.Submit(Command.Move(dir));
            Assert.That(m.Asleep, Is.False);
            Assert.That(Lab.EventsOf(run, EventKind.MonsterWoke), Has.Count.EqualTo(1));
            Assert.That(Lab.EventsOf(run, EventKind.Attack), Is.Empty);
        }

        [Test]
        public void AttackingWakesTheSerpentButAPotionSleepDoesNotBreak()
        {
            var run = Lab.Center(7);
            var serpent = Lab.Beside(run, MonsterKind.Serpent, Direction.East, hp: 1000);
            serpent.Asleep = true;
            run.Submit(Command.Move(Direction.East));
            Assert.That(serpent.Asleep, Is.False);

            var run2 = Lab.Center(7);
            var golem = Lab.Beside(run2, MonsterKind.Golem, Direction.East, hp: 1000);
            golem.SleepTurns = 4;
            run2.Submit(Command.Move(Direction.East));
            Assert.That(golem.SleepTurns, Is.GreaterThan(0), "眠り薬の眠りは殴っても覚めない");
        }

        [Test]
        public void ArcherShootsAlongAClearLineOnly()
        {
            var (run, archer, _) = LineScene(MonsterKind.Archer, 4);
            run.Player.Hp = 1000;
            var stand = archer.Position;
            run.Submit(Command.Wait());
            Assert.That(Lab.EventsOf(run, EventKind.Shot), Has.Count.EqualTo(1));
            Assert.That(archer.Position, Is.EqualTo(stand), "射つ番は動かない");

            // 間に別の敵がいると射たない（近づく）。
            var (run2, archer2, dir2) = LineScene(MonsterKind.Archer, 4);
            run2.Player.Hp = 1000;
            var blocker = run2.AddMonster(MonsterKind.Golem, run2.Player.Position + dir2.Offset() + dir2.Offset());
            blocker.SleepTurns = 100;
            run2.Refresh();
            var before = archer2.Position;
            run2.Submit(Command.Wait());
            Assert.That(Lab.EventsOf(run2, EventKind.Shot), Is.Empty);
            Assert.That(archer2.Position, Is.Not.EqualTo(before));
        }

        [Test]
        public void ArcherDoesNotShootOffTheLines()
        {
            // (2, 1) ずれた位置は直線でない。
            for (int seed = 1; seed <= 60; seed++)
            {
                var run = Lab.Center(seed);
                var p = run.Player.Position + new Vec2i(2, 1);
                if (!run.Grid.IsWalkable(p) || !run.Grid.IsWalkable(run.Player.Position + new Vec2i(1, 0)) || !run.Grid.IsWalkable(run.Player.Position + new Vec2i(1, 1))) { continue; }
                run.AddMonster(MonsterKind.Archer, p);
                run.Refresh();
                run.Submit(Command.Wait());
                Assert.That(Lab.EventsOf(run, EventKind.Shot), Is.Empty);
                return;
            }

            Assert.Fail("場面が作れません。");
        }

        [Test]
        public void ThiefStealsVanishesOutOfSightAndDropsTheLootWhenKilled()
        {
            var run = Lab.Center(8);
            run.Player.Inventory.Add(ItemKind.HealPotion);
            var thief = Lab.Beside(run, MonsterKind.Thief, Direction.East);
            for (int i = 0; i < 30 && run.Player.Inventory.Count > 0; i++) { run.Submit(Command.Wait()); }
            Assert.That(run.Player.Inventory, Is.Empty);
            Assert.That(thief.Held, Is.EqualTo(ItemKind.HealPotion));
            Assert.That(run.IsVisible(thief.Position), Is.False, "見えないマスへ跳ぶ");
            Assert.That(run.View.Monsters, Is.Empty, "跳んだ先は画面に出ない");

            // 隣へ回り込んで倒すと、盗んだ品を落とす。
            var items = run.FloorItemCount;
            thief.Hp = 1;
            for (int i = 0; i < 40 && run.Monsters.Count > 0; i++)
            {
                var spot = Lab.NeighborOf(run, thief.Position);
                run.Teleport(spot);
                Directions.TryBetween(spot, thief.Position, out var d);
                run.Submit(Command.Move(d));
            }

            Assert.That(run.Monsters, Is.Empty);
            Assert.That(Lab.EventsOf(run, EventKind.Dropped), Has.Count.EqualTo(1));
            Assert.That(run.FloorItemCount, Is.EqualTo(items + 1));
        }

        [Test]
        public void ThiefWithEmptyInventoryJustHits()
        {
            var run = Lab.Center(9);
            Lab.Beside(run, MonsterKind.Thief, Direction.West);
            run.Player.Hp = 1000;
            for (int i = 0; i < 10; i++) { run.Submit(Command.Wait()); }
            Assert.That(run.Monsters[0].Held, Is.Null);
            Assert.That(run.Player.Hp, Is.LessThan(1000));
        }

        [Test]
        public void MonsterThatLostSightGoesToTheLastSeenSpot()
        {
            var (run, m, _) = LineScene(MonsterKind.Skitter, 4);
            run.Submit(Command.Wait());
            var seen = m.LastSeen!.Value;

            // 自機を見えない所へ移すと、敵は最後に見た位置へ向かう。
            var far = KnownMapTests.FarUnseenRoomTile(run, m.Position);
            run.Teleport(far);
            Assert.That(run.IsVisible(m.Position), Is.False);
            var reached = false;
            for (int i = 0; i < 30 && !reached; i++)
            {
                run.Submit(Command.Wait());
                Assert.That(run.IsVisible(m.Position), Is.False, "遠くの部屋からは見えない");
                reached = m.Position == seen;
            }

            Assert.That(reached, Is.True);
            run.Submit(Command.Wait());
            Assert.That(m.LastSeen, Is.Null, "着いたら忘れて巡回に戻る");
        }
    }
}
