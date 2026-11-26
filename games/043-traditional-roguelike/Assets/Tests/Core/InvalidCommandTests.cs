using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有テスト：無効操作で敵ターンが進まない（時刻・乱数・満腹度・敵の位置・ハッシュが変わらない）。</summary>
    public sealed class InvalidCommandTests
    {
        /// <summary>自機を見ていて動き続ける速い敵を置いた場面。</summary>
        private static CrawlRun WithChasingMonster(Vec2i playerAt)
        {
            var run = Lab.Quiet(11);
            run.Teleport(playerAt);
            Monster? m = null;
            for (int dist = 3; dist <= 6 && m == null; dist++)
            {
                for (int d = 0; d < Directions.Count && m == null; d++)
                {
                    var off = ((Direction)d).Offset();
                    var p = playerAt + new Vec2i(off.X * dist, off.Y * dist);
                    if (run.Grid.IsWalkable(p) && run.IsVisible(p)) { m = run.AddMonster(MonsterKind.Skitter, p); }
                }
            }

            Assert.That(m, Is.Not.Null, "敵を置ける場所がありません");
            run.Refresh();
            return run;
        }

        private static void AssertRejectedWithoutChange(CrawlRun run, Command command, RejectReason reason)
        {
            var before = Lab.Snapshot(run);
            var outcome = run.Submit(command);
            Assert.That(outcome.Accepted, Is.False, command.ToString());
            Assert.That(outcome.Reason, Is.EqualTo(reason), command.ToString());
            Assert.That(Lab.Snapshot(run), Is.EqualTo(before), command + " の後に状態が変わった");
        }

        [Test]
        public void WallMoveDoesNotAdvanceMonsters()
        {
            var run = Lab.Quiet(11);
            var spot = Lab.FloorFacingWall(run, Direction.North);
            run = WithChasingMonster(spot);
            AssertRejectedWithoutChange(run, Command.Move(Direction.North), RejectReason.Blocked);

            // 裏：有効な操作なら敵が動き、時刻が進む。
            var before = Lab.Snapshot(run);
            Assert.That(run.Submit(Command.Wait()).Accepted, Is.True);
            Assert.That(Lab.Snapshot(run), Is.Not.EqualTo(before));
            Assert.That(run.Scheduler.TimeOf(TurnScheduler.PlayerId), Is.EqualTo(100));
        }

        [Test]
        public void CornerCutDoesNotAdvanceMonsters()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var probe = Lab.Quiet(seed);
                if (!Lab.TryCornerCut(probe, out var at, out var dir)) { continue; }
                probe.Teleport(at);
                AssertRejectedWithoutChange(probe, Command.Move(dir), RejectReason.Blocked);
                return;
            }

            Assert.Fail("角抜けの場所が見つかりません");
        }

        [Test]
        public void EveryKindOfInvalidCommandLeavesStateUntouched()
        {
            var run = Lab.Quiet(11);
            run = WithChasingMonster(Lab.FloorFacingWall(run, Direction.West));
            run.SetItem(run.Player.Position, null);
            run.Refresh();
            AssertRejectedWithoutChange(run, Command.Pickup(), RejectReason.NothingToPickUp);
            AssertRejectedWithoutChange(run, Command.Use(0), RejectReason.EmptySlot);
            AssertRejectedWithoutChange(run, Command.Throw(0), RejectReason.EmptySlot);
            AssertRejectedWithoutChange(run, Command.Drop(0), RejectReason.EmptySlot);
            AssertRejectedWithoutChange(run, Command.Use(-1), RejectReason.EmptySlot);
            AssertRejectedWithoutChange(run, Command.Descend(), RejectReason.NotOnStairs);
            AssertRejectedWithoutChange(run, Command.Move((Direction)8), RejectReason.InvalidCommand);
            AssertRejectedWithoutChange(run, Command.Face((Direction)(-1)), RejectReason.InvalidCommand);

            // 石は使えない。持ち物が満杯なら拾えない。足元に品があれば置けない。
            run.Player.Inventory.Add(ItemKind.Stone);
            run.Refresh();
            AssertRejectedWithoutChange(run, Command.Use(0), RejectReason.CannotUse);
            run.SetItem(run.Player.Position, ItemKind.Bread);
            AssertRejectedWithoutChange(run, Command.Drop(0), RejectReason.CannotDrop);
            while (run.Player.Inventory.Count < run.Config.InventoryCapacity) { run.Player.Inventory.Add(ItemKind.Stone); }
            AssertRejectedWithoutChange(run, Command.Pickup(), RejectReason.InventoryFull);
        }

        [Test]
        public void TurningInPlaceIsAcceptedButUsesNoTime()
        {
            var run = WithChasingMonster(Lab.RoomInterior(Lab.Quiet(11)));
            var before = Lab.Snapshot(run);
            Assert.That(run.Submit(Command.Face(Direction.West)).Accepted, Is.True);
            Assert.That(run.Player.Facing, Is.EqualTo(Direction.West));
            Assert.That(Lab.Snapshot(run), Is.Not.EqualTo(before), "向きはハッシュに入る");
            run.Player.Facing = Direction.South;
            Assert.That(Lab.Snapshot(run), Is.EqualTo(before), "向き以外は変わらない");
        }

        [Test]
        public void CommandsAfterTheEndAreRejected()
        {
            var run = Lab.Quiet(12);
            run.Player.Hp = 1;
            run.Player.Satiety = 0;
            Assert.That(run.Submit(Command.Wait()).Accepted, Is.True);
            Assert.That(run.State, Is.EqualTo(RunState.Dead));
            Assert.That(run.DeathCause, Is.EqualTo(DeathCause.Starvation));
            foreach (var command in new[] { Command.Wait(), Command.Move(Direction.East), Command.Face(Direction.North), Command.Descend(), Command.Pickup() })
            {
                AssertRejectedWithoutChange(run, command, RejectReason.NotPlaying);
            }
        }

        [Test]
        public void ManyRejectedCommandsInARowNeverMoveTheClock()
        {
            var run = WithChasingMonster(Lab.FloorFacingWall(Lab.Quiet(11), Direction.South));
            var before = Lab.Snapshot(run);
            for (int i = 0; i < 50; i++)
            {
                run.Submit(Command.Move(Direction.South));
                run.Submit(Command.Pickup());
                run.Submit(Command.Use(3));
            }

            Assert.That(Lab.Snapshot(run), Is.EqualTo(before));
        }
    }
}
