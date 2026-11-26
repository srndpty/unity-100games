using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>統一ターンスケジューラ。</summary>
    public sealed class SchedulerTests
    {
        private static readonly int[] _fastOrder = { 0, 1, 1, 0, 1, 1 };

        [Test]
        public void ActionCostsGiveProportionalTurnCounts()
        {
            var s = new TurnScheduler();
            s.Add(0, 0);
            s.Add(1, 0);
            s.Add(2, 0);
            s.Add(3, 0);
            var costs = new Dictionary<int, int> { [0] = 100, [1] = 50, [2] = 100, [3] = 200 };
            var counts = new Dictionary<int, int> { [0] = 0, [1] = 0, [2] = 0, [3] = 0 };
            for (int i = 0; i < 900; i++)
            {
                var id = s.Next();
                counts[id]++;
                s.Spend(id, costs[id]);
            }

            // 速さの比 4:2:2:1（重さ 50:100:100:200）。
            Assert.That(counts[1], Is.EqualTo(400));
            Assert.That(counts[0], Is.EqualTo(200));
            Assert.That(counts[2], Is.EqualTo(200));
            Assert.That(counts[3], Is.EqualTo(100));
        }

        [Test]
        public void TiesGoToTheSmallerIdAndPlayerFirst()
        {
            var s = new TurnScheduler();
            s.Add(5, 100);
            s.Add(2, 100);
            s.Add(0, 100);
            Assert.That(s.Next(), Is.EqualTo(0));
            s.Spend(0, 100);
            Assert.That(s.Next(), Is.EqualTo(2));
            s.Spend(2, 100);
            Assert.That(s.Next(), Is.EqualTo(5));
            Assert.That(s.Now, Is.EqualTo(100));
        }

        [Test]
        public void FastMonsterActsTwicePerPlayerAction()
        {
            var order = new List<int>();
            var s = new TurnScheduler();
            s.Add(0, 0);
            s.Add(1, 0);
            for (int i = 0; i < 6; i++)
            {
                var id = s.Next();
                order.Add(id);
                s.Spend(id, id == 0 ? 100 : 50);
            }

            Assert.That(order, Is.EqualTo(_fastOrder));
        }

        [Test]
        public void TimeUsesLongAndInvalidUseIsRejected()
        {
            var s = new TurnScheduler();
            s.Add(0, (long)int.MaxValue * 4);
            Assert.That(s.Next(), Is.EqualTo(0));
            s.Spend(0, 800);
            Assert.That(s.TimeOf(0), Is.EqualTo(((long)int.MaxValue * 4) + 800));
            Assert.That(() => s.Add(0, s.Now), Throws.InvalidOperationException, "同じ番号を二重に入れない");
            Assert.That(() => s.Add(1, s.Now - 1), Throws.TypeOf<System.ArgumentOutOfRangeException>(), "過去の時刻");
            Assert.That(() => s.Spend(0, 0), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(() => s.Spend(9, 100), Throws.TypeOf<KeyNotFoundException>());
            s.Remove(0);
            Assert.That(() => s.Next(), Throws.InvalidOperationException);
        }

        [Test]
        public void RunUsesTheSchedulerForMonsterSpeeds()
        {
            // 速い小獣は自機の 1 行動の間に 2 歩、岩人形は 2 行動で 1 歩。
            var run = Lab.Quiet(2);
            var origin = run.Player.Position;
            var far = KnownMapTests.FarUnseenRoomTile(run, origin);
            var skitter = run.AddMonster(MonsterKind.Skitter, far);
            run.Refresh();
            var before = run.Scheduler.TimeOf(skitter.Id);
            run.Submit(Command.Wait());
            Assert.That(run.Scheduler.TimeOf(skitter.Id) - before, Is.EqualTo(100), "50 × 2");
            Assert.That(run.Scheduler.TimeOf(TurnScheduler.PlayerId), Is.EqualTo(100));

            run.ClearMonsters();
            var golem = run.AddMonster(MonsterKind.Golem, far);
            var golemStart = run.Scheduler.TimeOf(golem.Id);
            run.Submit(Command.Wait());
            run.Submit(Command.Wait());
            Assert.That(run.Scheduler.TimeOf(golem.Id) - golemStart, Is.EqualTo(200), "200 × 1");
        }
    }
}
