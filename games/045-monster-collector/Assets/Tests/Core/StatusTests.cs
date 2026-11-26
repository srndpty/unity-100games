using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有テスト：状態異常の解除が予定ターンに発生する。</summary>
    public sealed class StatusTests
    {
        /// <summary>
        /// 眠り（持続 2〜3）：付与したターン t と持続 d から、t+d のターンの開始で治り、そのターンに動く。
        /// t+1〜t+d−1 のターンは眠って動けない。多くのシードで 2 と 3 の両方を確かめる。
        /// </summary>
        [Test]
        public void SleepEndsAtTheScheduledTurn()
        {
            var seen = new HashSet<int>();
            for (int seed = 1; seed <= 60; seed++)
            {
                var battle = Lab.Wild(Lab.Mon("mossling", 20, "sleepspore", "harden"), Lab.Mon("brinecrab", 20, "harden"), seed);
                BattleEvent? inflicted = null;
                var turns = new List<IReadOnlyList<BattleEvent>>();
                while (inflicted == null)
                {
                    var events = battle.Submit(BattleCommand.Fight(0)).Events;
                    turns.Add(events);
                    inflicted = Lab.First(events, BattleEventKind.StatusInflicted, player: false);
                }

                var duration = inflicted.Amount;
                var due = inflicted.Turn;
                var start = due - duration;
                seen.Add(duration);
                Assert.That(battle.FoeMonster.StatusEndsTurn, Is.EqualTo(due));
                Assert.That(battle.Turn, Is.EqualTo(start + 1), "付与したターンが終わって次のターン");

                // 治るまでは眠って動けない。
                for (int t = start + 1; t < due; t++)
                {
                    var events = battle.Submit(BattleCommand.Fight(1)).Events;
                    Assert.That(Lab.First(events, BattleEventKind.Asleep, player: false), Is.Not.Null, $"シード {seed}：ターン {t} は眠っている");
                    Assert.That(Lab.First(events, BattleEventKind.UsedMove, player: false), Is.Null);
                    var cured = Lab.First(events, BattleEventKind.StatusCured, player: false);
                    if (t + 1 == due)
                    {
                        Assert.That(cured, Is.Not.Null, $"シード {seed}：ターン {due} の開始で治る");
                        Assert.That(cured!.Turn, Is.EqualTo(due));
                    }
                    else
                    {
                        Assert.That(cured, Is.Null);
                    }
                }

                if (due == start + 1)
                {
                    Assert.That(Lab.First(turns[turns.Count - 1], BattleEventKind.StatusCured, player: false), Is.Not.Null);
                }

                Assert.That(battle.Turn, Is.EqualTo(due));
                Assert.That(battle.FoeMonster.Status, Is.Null);
                var wake = battle.Submit(BattleCommand.Fight(1)).Events;
                Assert.That(Lab.First(wake, BattleEventKind.UsedMove, player: false), Is.Not.Null, $"シード {seed}：治ったターンに動く");
                Assert.That(Lab.First(wake, BattleEventKind.Asleep, player: false), Is.Null);
            }

            Assert.That(seen, Is.EquivalentTo(new List<int> { 2, 3 }), "持続は 2 と 3 の両方が出る");
        }

        /// <summary>毒（持続 4）：付与したターンから 4 回ターンの終わりにダメージを受け、5 回目の前のターンの開始で治る。</summary>
        [Test]
        public void PoisonDamagesFourTimesThenCures()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var battle = Lab.Wild(Lab.Mon("cinderpup", 20, "smolder", "harden"), Lab.Mon("brinecrab", 20, "harden"), seed);
                BattleEvent? inflicted = null;
                var ticks = 0;
                while (inflicted == null)
                {
                    var events = battle.Submit(BattleCommand.Fight(0)).Events;
                    inflicted = Lab.First(events, BattleEventKind.StatusInflicted, player: false);
                    ticks += Lab.Count(events, BattleEventKind.StatusDamage);
                }

                Assert.That(inflicted.Amount, Is.EqualTo(4));
                var cureTurn = 0;
                while (battle.FoeMonster.Status != null)
                {
                    var events = battle.Submit(BattleCommand.Fight(1)).Events;
                    ticks += Lab.Count(events, BattleEventKind.StatusDamage);
                    var cured = Lab.First(events, BattleEventKind.StatusCured, player: false);
                    if (cured != null) { cureTurn = cured.Turn; }
                }

                Assert.That(ticks, Is.EqualTo(4), $"シード {seed}：毒のダメージは 4 回");
                Assert.That(cureTurn, Is.EqualTo(inflicted.Turn));
                var maxHp = battle.FoeMonster.MaxHp;
                Assert.That(battle.FoeMonster.Hp, Is.EqualTo(maxHp - 4 * (maxHp / 8)));
            }
        }

        [Test]
        public void ScheduleIsKeptWhileBenched_AndBenchedTakesNoPoison()
        {
            var a = Lab.Mon("mossling", 20, "harden");
            var b = Lab.Mon("cinderpup", 20, "harden");
            var battle = Lab.Wild(new[] { a, b }, Lab.Mon("brinecrab", 20, "harden"));
            battle.Party[0].SetStatus(Lab.Data.Status("poison"), 3);
            var hp = battle.Party[0].Hp;

            var turn1 = battle.Submit(BattleCommand.Switch(1)).Events;
            Assert.That(Lab.Count(turn1, BattleEventKind.StatusDamage), Is.Zero, "控えは毒のダメージを受けない");
            var turn2 = battle.Submit(BattleCommand.Fight(0)).Events;
            var cured = Lab.First(turn2, BattleEventKind.StatusCured, player: true);
            Assert.That(cured, Is.Not.Null, "控えでも予定のターン 3 の開始で治る");
            Assert.That(cured!.Slot, Is.EqualTo(0));
            Assert.That(cured.Turn, Is.EqualTo(3));
            Assert.That(battle.Party[0].Status, Is.Null);
            Assert.That(battle.Party[0].Hp, Is.EqualTo(hp));
        }

        [Test]
        public void StatusDoesNotStack_TheScheduleStays()
        {
            var battle = Lab.Wild(Lab.Mon("mossling", 20, "sleepspore"), Lab.Mon("brinecrab", 20, "harden"));
            battle.FoeMonster.SetStatus(Lab.Data.Status("poison"), 5);
            var state = Lab.StateWhere(r =>
            {
                r.Next(1);
                return r.Next(100) < 75;
            });
            battle.SetRngStateForTest(state);
            var events = battle.Submit(BattleCommand.Fight(0)).Events;
            Assert.That(Lab.First(events, BattleEventKind.StatusFailed, player: false), Is.Not.Null);
            Assert.That(battle.FoeMonster.Status!.Id, Is.EqualTo("poison"));
            Assert.That(battle.FoeMonster.StatusEndsTurn, Is.EqualTo(5));
        }

        [Test]
        public void BattleEndClearsStatuses()
        {
            var battle = Lab.Wild(Lab.Mon("cinderpup", 20, "blaze"), Lab.Mon("burrowroot", 2, "tackle"));
            battle.Party[0].SetStatus(Lab.Data.Status("poison"), 9);
            while (!battle.IsOver) { battle.Submit(BattleCommand.Fight(0)); }
            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Won));
            Assert.That(battle.Party[0].Status, Is.Null);
        }

        [Test]
        public void StatusDataDrivesTheDuration()
        {
            var data = Lab.ParseWith("Statuses.txt", "status sleep 眠り 1 0 2 3 200", "status sleep 眠り 1 0 5 5 200");
            Assert.That(MonsterDataValidator.Validate(data), Is.Empty);
            Assert.That(data.Status("sleep").MinTurns, Is.EqualTo(5));
        }
    }
}
