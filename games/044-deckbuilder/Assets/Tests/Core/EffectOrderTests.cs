using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>複数の効果は書いた順に解決し、出来事の列が固定になる。</summary>
    public sealed class EffectOrderTests
    {
        [Test]
        public void TwoHitsResolveInOrderThenTheCardIsDiscarded()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden"), "twin", "strike");
            var start = battle.Events.Count;
            battle.Play(1, 1);
            Assert.That(Lab.TraceFrom(battle, start), Is.EqualTo("Play:1 Dmg:1:4+0 Dmg:1:4+0 Disc:1"));
        }

        [Test]
        public void StatusAppliedAfterTheHitDoesNotBoostThatHitButBoostsTheNext()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden"), "crush strike");
            var start = battle.Events.Count;
            battle.Play(1, 1);
            battle.Play(2, 1);
            Assert.That(
                Lab.TraceFrom(battle, start),
                Is.EqualTo("Play:1 Dmg:1:9+0 St:1:Vulnerable2 Disc:1 Play:2 Dmg:1:9+0 Disc:2"));
        }

        [Test]
        public void AttackThenDrawDrawsAfterTheHit()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden"), "quickcut", "guard");
            var start = battle.Events.Count;
            battle.Play(1, 1);
            Assert.That(Lab.TraceFrom(battle, start), Is.EqualTo("Play:1 Dmg:1:7+0 Draw:2 Disc:1"));
        }

        [Test]
        public void KilledChosenTargetMakesTheRestOfItsHitsFizzleButOtherEffectsContinue()
        {
            var battle = Lab.Arranged(Lab.Encounter("imp", "imp"), "twin quickcut", "guard");
            battle.EnemyById(1)!.Hp = 4;
            var start = battle.Events.Count;
            battle.Play(1, 1);
            Assert.That(Lab.TraceFrom(battle, start), Is.EqualTo("Play:1 Dmg:1:4+0 Died:1 Fizz:Damage Disc:1"));

            battle.EnemyById(2)!.Hp = 20;
            Assert.That(battle.Play(2, 1).Reason, Is.EqualTo(RejectReason.TargetInvalid));
        }

        [Test]
        public void DrawAfterAKillStillHappensWhileEnemiesRemain()
        {
            var battle = Lab.Arranged(Lab.Encounter("imp", "imp"), "quickcut", "guard");
            battle.EnemyById(1)!.Hp = 7;
            var start = battle.Events.Count;
            battle.Play(1, 1);
            Assert.That(Lab.TraceFrom(battle, start), Is.EqualTo("Play:1 Dmg:1:7+0 Died:1 Draw:2 Disc:1"));
        }

        [Test]
        public void LastKillEndsTheBattleAndDropsTheRestOfTheQueue()
        {
            var battle = Lab.Arranged(Lab.Encounter("imp"), "quickcut", "guard");
            battle.EnemyById(1)!.Hp = 7;
            var start = battle.Events.Count;
            battle.Play(1, 1);
            Assert.That(Lab.TraceFrom(battle, start), Is.EqualTo("Play:1 Dmg:1:7+0 Died:1 Victory Disc:1"));
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Won));
            Lab.AssertZones(battle, 2);
        }

        [Test]
        public void ReshuffleDuringResolutionDoesNotIncludeTheResolvingCard()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden"), "quickcut", discard: "guard strike");
            var start = battle.Events.Count;
            battle.Play(1, 1);
            var trace = Lab.TraceFrom(battle, start);
            Assert.That(trace, Does.StartWith("Play:1 Dmg:1:7+0 Shuf:2 Draw:"));
            Assert.That(trace, Does.EndWith("Disc:1"));
            Assert.That(battle.DrawPile.Concat(battle.Hand).Select(c => c.Id), Has.No.Member(1));
            Assert.That(Lab.Ids(battle.DiscardPile), Is.EqualTo("1"));
            Lab.AssertZones(battle, 3);
        }

        [Test]
        public void AllEnemiesAreHitInIdOrder()
        {
            var battle = Lab.Arranged(Lab.Encounter("imp", "moth", "imp"), "sweep");
            var start = battle.Events.Count;
            battle.Play(1, null);
            Assert.That(Lab.TraceFrom(battle, start), Is.EqualTo("Play:1 Dmg:1:7+0 Dmg:2:7+0 Dmg:3:7+0 Disc:1"));
        }

        [Test]
        public void BlockThenDoubleUsesTheBlockAtThatMoment()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden"), "guard fortress");
            var start = battle.Events.Count;
            battle.Play(1, null);
            battle.Play(2, null);
            Assert.That(Lab.TraceFrom(battle, start), Is.EqualTo("Play:1 Blk:0:5 Disc:1 Play:2 Blk:0:4 Blk:0:9 Exh:2"));
            Assert.That(battle.Player.Block, Is.EqualTo(18));
        }

        [Test]
        public void DamageFormulaOrderIsStrengthThenWeakThenVulnerable()
        {
            var config = new DeckConfig();
            var attacker = new Combatant(0, 10, 10);
            var defender = new Combatant(1, 10, 10);
            attacker.SetStatus(StatusKind.Strength, 3);
            attacker.SetStatus(StatusKind.Weak, 1);

            // (3 + 3) × 75% = 4（脱力を先に掛けると 2 + 3 = 5）。
            Assert.That(DamageCalc.Attack(3, attacker, defender, config), Is.EqualTo(4));

            attacker.SetStatus(StatusKind.Strength, 0);
            defender.SetStatus(StatusKind.Vulnerable, 1);

            // 5 × 75% = 3 → × 150% = 4（弱体を先に掛けると 7 → 5）。
            Assert.That(DamageCalc.Attack(5, attacker, defender, config), Is.EqualTo(4));
        }

        [Test]
        public void SameSeedAndInputsGiveTheSameEventsAndHash()
        {
            static Battle Play()
            {
                var battle = Lab.Arranged(Lab.Encounter("imp", "moth", "spitter"), "sparks purge sweep", "strike guard twin", seed: 42);
                battle.Play(1, null);
                battle.Play(2, null);
                battle.EndTurn();
                return battle;
            }

            var a = Play();
            var b = Play();
            Assert.That(Lab.Trace(a.Events), Is.EqualTo(Lab.Trace(b.Events)));
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
        }

        [Test]
        public void CloneResolvesTheSameWayAndDoesNotShareState()
        {
            var battle = Lab.Arranged(Lab.Encounter("imp", "moth"), "sparks strike", "guard twin", seed: 7);
            var copy = battle.Clone();
            Assert.That(copy.Hash(), Is.EqualTo(battle.Hash()));
            battle.Play(1, null);
            Assert.That(copy.Hash(), Is.Not.EqualTo(battle.Hash()));
            Assert.That(copy.Hand.Count, Is.EqualTo(2));
            copy.Play(1, null);
            Assert.That(copy.Hash(), Is.EqualTo(battle.Hash()));
            Assert.That(Lab.Trace(copy.Events), Is.EqualTo(Lab.Trace(battle.Events)));
        }
    }
}
