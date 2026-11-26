using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>階段・勝利。</summary>
    public sealed class FloorProgressTests
    {
        [Test]
        public void DescendingFiveTimesWins()
        {
            var run = Lab.NewRun(4);
            for (int floor = 1; floor <= CrawlConfig.FloorCount; floor++)
            {
                Assert.That(run.Floor, Is.EqualTo(floor));
                Assert.That(run.Submit(Command.Descend()).Reason, Is.EqualTo(RejectReason.NotOnStairs).Or.EqualTo(RejectReason.None));
                run.ClearMonsters();
                run.Teleport(run.Grid.Stairs);
                Assert.That(run.Submit(Command.Descend()).Accepted, Is.True);
            }

            Assert.That(run.State, Is.EqualTo(RunState.Won));
            Assert.That(run.Floor, Is.EqualTo(CrawlConfig.FloorCount));
            Assert.That(Lab.EventsOf(run, EventKind.Won), Has.Count.EqualTo(1));
            Assert.That(run.Submit(Command.Descend()).Reason, Is.EqualTo(RejectReason.NotPlaying));
        }

        [Test]
        public void NewFloorStartsWithFreshKnowledgeAndKeepsThePlayer()
        {
            var run = Lab.NewRun(5);
            run.Player.Inventory.Add(ItemKind.Stone);
            run.Player.Level = 3;
            run.ClearMonsters();
            run.Teleport(run.Grid.Stairs);
            var time = run.Scheduler.TimeOf(TurnScheduler.PlayerId);
            run.Submit(Command.Descend());
            Assert.That(run.Floor, Is.EqualTo(2));
            Assert.That(run.Player.Inventory, Has.Member(ItemKind.Stone));
            Assert.That(run.Player.Level, Is.EqualTo(3));
            Assert.That(run.Scheduler.TimeOf(TurnScheduler.PlayerId), Is.EqualTo(time + 100));
            Assert.That(run.Monsters.Count, Is.EqualTo(run.Config.MonstersBase + (2 * run.Config.MonstersPerFloor)));
            var known = 0;
            for (int i = 0; i < run.Grid.Width * run.Grid.Height; i++)
            {
                if (run.Visible[i]) { known++; }
            }

            Assert.That(known, Is.GreaterThan(0));
            Assert.That(run.Known.IsSeen(run.Grid.Stairs), Is.EqualTo(run.IsVisible(run.Grid.Stairs)), "新しい階の既知は見えている所だけ");
        }

        [Test]
        public void PlacementIsIndependentOfPlay()
        {
            // 1 階での行動が違っても、2 階の敵と品の配置は同じ。
            string Placement(int waits)
            {
                var run = Lab.NewRun(9);
                for (int i = 0; i < waits && run.State == RunState.Playing; i++) { run.Submit(Command.Wait()); }
                run.ClearMonsters();
                run.Player.Hp = run.Player.MaxHp;
                run.Teleport(run.Grid.Stairs);
                run.Submit(Command.Descend());
                var parts = new System.Collections.Generic.List<string>();
                foreach (var m in run.Monsters) { parts.Add($"{m.Kind}{m.Position}"); }
                for (int y = 0; y < run.Grid.Height; y++)
                {
                    for (int x = 0; x < run.Grid.Width; x++)
                    {
                        var item = run.ItemAt(new IntGeometry.Vec2i(x, y));
                        if (item.HasValue) { parts.Add($"{item}({x},{y})"); }
                    }
                }

                return string.Join(";", parts);
            }

            Assert.That(Placement(0), Is.EqualTo(Placement(3)));
        }
    }
}
