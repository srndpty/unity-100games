using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>ターンの流れ・ブロック・状態異常・敵の行動。</summary>
    public sealed class TurnTests
    {
        private static void SetIntent(Battle battle, int enemyId, string moveId)
        {
            var enemy = battle.EnemyById(enemyId)!;
            enemy.Intent = enemy.Def.Moves.First(m => m.Id == moveId);
        }

        [Test]
        public void FirstTurnDrawsFiveAndSetsEnergyAndIntents()
        {
            var battle = Lab.NewBattle(Lab.Starter(), Lab.Encounter("imp", "warden"));
            Assert.That(battle.Turn, Is.EqualTo(1));
            Assert.That(battle.Hand.Count, Is.EqualTo(5));
            Assert.That(battle.DrawPile.Count, Is.EqualTo(5));
            Assert.That(battle.Energy, Is.EqualTo(3));
            Assert.That(battle.Enemies.All(e => e.Intent != null), Is.True);
            Assert.That(battle.EnemyById(2)!.Intent!.Id, Is.EqualTo("raise"), "巡回は書いた順の最初から");
        }

        [Test]
        public void BlockAbsorbsExactlyAndResetsAtTheOwnersTurnStart()
        {
            var battle = Lab.Arranged(Lab.Encounter("imp"), "guard", "strike strike strike strike strike");
            battle.Play(1, null);
            SetIntent(battle, 1, "scratch");
            var start = battle.Events.Count;
            battle.EndTurn();
            Assert.That(Lab.TraceFrom(battle, start), Does.Contain("Dmg:0:1+5"));
            Assert.That(battle.Player.Hp, Is.EqualTo(69));
            Assert.That(battle.Player.Block, Is.EqualTo(0));

            battle.Player.Block = 6;
            SetIntent(battle, 1, "scratch");
            start = battle.Events.Count;
            battle.EndTurn();
            Assert.That(Lab.TraceFrom(battle, start), Does.Contain("Dmg:0:0+6"), "ブロックちょうどなら HP は減らない");
            Assert.That(battle.Player.Hp, Is.EqualTo(69));
        }

        [Test]
        public void EnemiesActInIdOrderAfterTheHandIsDiscarded()
        {
            var battle = Lab.Arranged(Lab.Encounter("imp", "imp"), "strike", "guard guard guard guard guard");
            SetIntent(battle, 1, "scratch");
            SetIntent(battle, 2, "jab");
            var start = battle.Events.Count;
            battle.EndTurn();
            var trace = Lab.TraceFrom(battle, start);
            Assert.That(trace, Does.StartWith("TurnEnded Disc:1 EnemyActed Dmg:0:6+0 EnemyActed Dmg:0:3+0 Dmg:0:3+0 TurnStarted"));
        }

        [Test]
        public void WeakAndVulnerableLastTwoOfTheOpponentsTurns()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden"), "sap strike", "strike strike strike strike strike");
            battle.Play(1, 1);
            var warden = battle.EnemyById(1)!;
            Assert.That(warden.Status(StatusKind.Weak), Is.EqualTo(2));
            Assert.That(warden.Status(StatusKind.Vulnerable), Is.EqualTo(1));
            SetIntent(battle, 1, "slam");
            var start = battle.Events.Count;
            battle.EndTurn();

            // 脱力で 11 × 75% = 8。行動の後に 1 減る。
            Assert.That(Lab.TraceFrom(battle, start), Does.Contain("Dmg:0:8+0"));
            Assert.That(warden.Status(StatusKind.Weak), Is.EqualTo(1));
            Assert.That(warden.Status(StatusKind.Vulnerable), Is.EqualTo(0));
        }

        [Test]
        public void PoisonIgnoresBlockTicksAtTheOwnersTurnStartAndCanWinTheBattle()
        {
            var battle = Lab.Arranged(Lab.Encounter("imp"), "venom", "guard guard guard guard guard");
            battle.Play(1, 1);
            var imp = battle.EnemyById(1)!;
            Assert.That(imp.Hp, Is.EqualTo(12));
            imp.Block = 10;
            imp.Hp = 4;
            var start = battle.Events.Count;
            battle.EndTurn();
            Assert.That(Lab.TraceFrom(battle, start), Is.EqualTo("TurnEnded BlockLost Psn:1:4 Died:1 Victory"));
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Won));
        }

        [Test]
        public void PlayerKilledByPoisonAtTurnStartGetsNoEnergyOrDraw()
        {
            var battle = Lab.Arranged(Lab.Encounter("imp"), "strike", Lab.Many("guard", 10));
            battle.Play(1, 1);
            Assume.That(battle.Energy, Is.EqualTo(2));
            battle.EnemyById(1)!.Intent = null;
            battle.Player.Block = 3;
            battle.Player.Hp = 2;
            battle.Player.AddStatus(StatusKind.Poison, 2);
            var start = battle.Events.Count;
            battle.EndTurn();
            // 順序はブロック 0 → 毒 → 持続 → エネルギー → 引く。毒で倒れたらエネルギーの補充と引きの出来事は出ない。
            Assert.That(Lab.TraceFrom(battle, start), Is.EqualTo("TurnEnded TurnStarted BlockLost Psn:0:2 Died:0 Defeat"));
            Assert.That(battle.Energy, Is.EqualTo(2));
        }

        [Test]
        public void PowersTriggerAtThePlayersTurnStart()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden", "imp"), "bulwark fumes", "guard guard guard guard guard", energy: 3);
            battle.Play(1, null);
            battle.Play(2, null);
            battle.EnemyById(1)!.Intent = null;
            battle.EnemyById(2)!.Intent = null;
            var start = battle.Events.Count;
            battle.EndTurn();
            Assert.That(Lab.TraceFrom(battle, start), Does.Contain("TurnStarted Blk:0:4 St:1:Poison2 St:2:Poison2 En:3 Draw:"));
        }

        [Test]
        public void DrawingIntoAFullHandIsReportedAndLeavesTheCardInTheDrawPile()
        {
            var nine = Lab.Arranged(Lab.Encounter("warden"), "glance " + Lab.Many("guard", 9), "strike");
            var s1 = nine.Events.Count;
            nine.Play(1, null);
            Assert.That(Lab.TraceFrom(nine, s1), Is.EqualTo("Play:1 Draw:11 Disc:1"));
            Assert.That(nine.Hand.Count, Is.EqualTo(10));

            var full = Lab.Arranged(Lab.Encounter("warden"), "glance " + Lab.Many("guard", 10), "strike");
            var s2 = full.Events.Count;
            full.Play(1, null);
            Assert.That(Lab.TraceFrom(full, s2), Is.EqualTo("Play:1 HandFull Disc:1"));
            Assert.That(full.DrawPile.Count, Is.EqualTo(1));
        }

        [Test]
        public void CycleEnemyRepeatsItsMovesInOrder()
        {
            var battle = Lab.Arranged(Lab.Encounter("moth"), "guard", Lab.Many("guard", 30));
            battle.EnemyById(1)!.Hp = 999;
            var seen = new System.Collections.Generic.List<string>();
            for (int i = 0; i < 6; i++)
            {
                seen.Add(battle.EnemyById(1)!.Intent!.Id);
                battle.EndTurn();
            }

            Assert.That(string.Join(" ", seen), Is.EqualTo("dust gaze bite dust gaze bite"));
        }

        [Test]
        public void RandomEnemyNeverRepeatsTheSameMoveTwiceInARow([Range(1, 10)] int seed)
        {
            var battle = Lab.Arranged(Lab.Encounter("imp"), "guard", Lab.Many("guard", 40), seed: seed);
            battle.EnemyById(1)!.Hp = 999;
            battle.Player.Hp = 70;
            string? last = null;
            for (int i = 0; i < 12 && !battle.IsOver; i++)
            {
                var intent = battle.EnemyById(1)!.Intent!.Id;
                Assert.That(intent, Is.Not.EqualTo(last));
                last = intent;
                battle.Player.Hp = 70;
                battle.EndTurn();
            }
        }

        [Test]
        public void ShownIntentIsWhatTheEnemyDoes()
        {
            var battle = Lab.Arranged(Lab.Encounter("spitter"), "guard", Lab.Many("guard", 40), seed: 3);
            battle.EnemyById(1)!.Hp = 999;
            for (int i = 0; i < 8; i++)
            {
                var shown = battle.EnemyById(1)!.Intent!;
                var start = battle.Events.Count;
                battle.Player.Hp = 70;
                battle.EndTurn();
                var acted = battle.Events.Skip(start).First(e => e.Kind == EventKind.EnemyActed);
                Assert.That(acted.Detail, Is.EqualTo(shown.Index));
            }
        }

        [Test]
        public void BossCallsHelpOnceWhenHalfAndSummonFizzlesWhenFull()
        {
            var battle = Lab.Arranged(Lab.Encounter("kiln"), "guard", Lab.Many("guard", 40));
            var kiln = battle.EnemyById(1)!;
            Assert.That(kiln.Intent!.Id, Is.EqualTo("slam"));
            kiln.Hp = 48;
            battle.Player.Hp = 70;
            battle.EndTurn();
            Assert.That(kiln.Intent!.Id, Is.EqualTo("call"));
            battle.Player.Hp = 70;
            var start = battle.Events.Count;
            battle.EndTurn();
            Assert.That(battle.Events.Skip(start).Count(e => e.Kind == EventKind.Summoned), Is.EqualTo(1));
            Assert.That(battle.Enemies.Count, Is.EqualTo(2));
            Assert.That(kiln.Intent!.Id, Is.Not.EqualTo("call"), "一度だけ");

            // 満杯（上限 3）のときは不発。
            var crowded = Lab.Arranged(Lab.Encounter("kiln", "imp", "imp"), "guard", Lab.Many("guard", 20));
            var boss = crowded.EnemyById(1)!;
            boss.Intent = boss.Def.Moves.First(m => m.Id == "call");
            crowded.EnemyById(2)!.Intent = null;
            crowded.EnemyById(3)!.Intent = null;
            var s = crowded.Events.Count;
            crowded.EndTurn();
            Assert.That(Lab.TraceFrom(crowded, s), Does.Contain("Fizz:Summon"));
            Assert.That(crowded.Enemies.Count, Is.EqualTo(3));
        }

        [Test]
        public void PlayerDeathEndsTheBattleImmediately()
        {
            var battle = Lab.Arranged(Lab.Encounter("imp", "imp"), "strike", Lab.Many("guard", 10));
            battle.Player.Hp = 6;
            SetIntent(battle, 1, "scratch");
            SetIntent(battle, 2, "scratch");
            var start = battle.Events.Count;
            battle.EndTurn();
            Assert.That(Lab.TraceFrom(battle, start), Is.EqualTo("TurnEnded Disc:1 EnemyActed Dmg:0:6+0 Died:0 Defeat"));
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Lost));
        }
    }
}
