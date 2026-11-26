using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有テスト：視界外の情報を UI（PlayerView）に出さない。</summary>
    public sealed class ViewTests
    {
        /// <summary>窓口の全ての公開の値が、既知・視界の範囲から外れていないことを確かめる。</summary>
        internal static void AssertNoLeak(CrawlRun run)
        {
            var v = run.View;
            foreach (var m in v.Monsters)
            {
                Assert.That(v.IsVisible(m.Position), Is.True, $"見えない敵 {m.Kind} {m.Position}");
            }

            var visibleMonsters = 0;
            foreach (var m in run.Monsters)
            {
                if (run.IsVisible(m.Position)) { visibleMonsters++; }
                else { Assert.That(v.MonsterAt(m.Position), Is.Null); }
            }

            Assert.That(v.Monsters.Count, Is.EqualTo(visibleMonsters), "見えている敵は全て出す");
            foreach (var e in v.Events)
            {
                Assert.That(e.Seen, Is.True);
            }

            for (int y = 0; y < v.Height; y++)
            {
                for (int x = 0; x < v.Width; x++)
                {
                    var p = new Vec2i(x, y);
                    if (!run.Known.IsSeen(p))
                    {
                        Assert.That(v.TileAt(p), Is.Null, $"見ていない地形 {p}");
                        Assert.That(v.ItemAt(p), Is.Null, $"見ていない品 {p}");
                    }
                    else if (!run.IsVisible(p))
                    {
                        Assert.That(v.ItemAt(p), Is.EqualTo(run.Known.RememberedItem(p)), "視界の外は記憶だけ");
                    }
                    else
                    {
                        Assert.That(v.ItemAt(p), Is.EqualTo(run.ItemAt(p)));
                    }
                }
            }
        }

        [Test]
        public void RandomPlayNeverLeaksUnseenInformation()
        {
            for (int seed = 1; seed <= 6; seed++)
            {
                var run = Lab.NewRun(seed);
                run.Player.Hp = 10_000;
                foreach (var c in ReplayTests.RandomCommands(seed, 250))
                {
                    run.Submit(c);
                    AssertNoLeak(run);
                    if (run.State != RunState.Playing) { break; }
                }
            }
        }

        [Test]
        public void EventsOutsideTheViewAreRecordedButNotShown()
        {
            var run = Lab.Center(2);
            var far = KnownMapTests.FarUnseenRoomTile(run, run.Player.Position);
            var golem = run.AddMonster(MonsterKind.Golem, far);
            golem.SleepTurns = 1;
            run.Refresh();
            run.Submit(Command.Wait());
            var woke = Lab.EventsOf(run, EventKind.MonsterWoke);
            Assert.That(woke, Has.Count.EqualTo(1), "Core は記録する");
            Assert.That(woke[0].Seen, Is.False);
            foreach (var e in run.View.Events) { Assert.That(e.Kind, Is.Not.EqualTo(EventKind.MonsterWoke)); }
        }

        [Test]
        public void MonsterAppearsWhenSeenAndDisappearsWhenOutOfSightWhileTerrainStays()
        {
            var run = Lab.Center(3);
            var far = KnownMapTests.FarUnseenRoomTile(run, run.Player.Position);
            var golem = run.AddMonster(MonsterKind.Golem, far);
            golem.SleepTurns = 1000;
            run.Refresh();
            Assert.That(run.View.Monsters, Is.Empty);
            var home = run.Player.Position;
            run.Teleport(Lab.NeighborOf(run, far));
            Assert.That(run.View.Monsters, Has.Count.EqualTo(1));
            Assert.That(run.View.Monsters[0].Kind, Is.EqualTo(MonsterKind.Golem));
            run.Teleport(home);
            Assert.That(run.View.Monsters, Is.Empty, "敵は覚えない");
            Assert.That(run.View.TileAt(far), Is.EqualTo(run.Grid[far]), "地形は覚える");
        }

        [Test]
        public void ViewCollectionsCannotBeCastBackToMutableLists()
        {
            var run = Lab.NewRun(1);
            Assert.That((object)run.View.Inventory, Is.Not.InstanceOf<System.Collections.Generic.List<ItemKind>>());
            Assert.That(() => ((System.Collections.Generic.IList<ItemKind>)run.View.Inventory).Add(ItemKind.Stone), Throws.TypeOf<System.NotSupportedException>());
        }
    }
}
