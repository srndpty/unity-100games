using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>カードを使える条件。拒否したときは状態を何も変えない。</summary>
    public sealed class PlayRuleTests
    {
        private static void AssertUnchanged(Battle battle, System.Func<PlayResult> act, RejectReason reason)
        {
            var hash = battle.Hash();
            var events = battle.Events.Count;
            var hand = battle.Hand.Count;
            var energy = battle.Energy;
            var result = act();
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(reason));
            Assert.That(battle.Hash(), Is.EqualTo(hash), "状態ハッシュ（乱数の状態を含む）");
            Assert.That(battle.Events.Count, Is.EqualTo(events), "出来事");
            Assert.That(battle.Hand.Count, Is.EqualTo(hand));
            Assert.That(battle.Energy, Is.EqualTo(energy));
        }

        [Test]
        public void CardCostingOneMoreThanEnergyIsRejectedWithoutAnyChange()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden"), "crush", "strike", energy: 1);
            var crush = Lab.InHand(battle, "crush");
            AssertUnchanged(battle, () => battle.Play(crush.Id, 1), RejectReason.NotEnoughEnergy);
            Assert.That(battle.CanPlay(crush.Id, 1).Reason, Is.EqualTo(RejectReason.NotEnoughEnergy));
            Assert.That(battle.Affordable(crush), Is.False);
        }

        [Test]
        public void CardCostingExactlyTheEnergyIsPlayedAndEnergyBecomesZero()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden"), "crush strike", energy: 2);
            var crush = Lab.InHand(battle, "crush");
            Assert.That(battle.Play(crush.Id, 1).Accepted, Is.True);
            Assert.That(battle.Energy, Is.EqualTo(0));
            var strike = Lab.InHand(battle, "strike");
            AssertUnchanged(battle, () => battle.Play(strike.Id, 1), RejectReason.NotEnoughEnergy);
        }

        [Test]
        public void ZeroCostCardIsPlayableWithZeroEnergy()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden"), "glance", "strike", energy: 0);
            Assert.That(battle.Play(Lab.InHand(battle, "glance").Id, null).Accepted, Is.True);
            Assert.That(battle.Hand.Count, Is.EqualTo(1));
        }

        [Test]
        public void EnergyFromAnEffectCanPayTheNextCard()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden"), "kindle strike", "guard", energy: 0);
            Assert.That(battle.Play(Lab.InHand(battle, "strike").Id, 1).Reason, Is.EqualTo(RejectReason.NotEnoughEnergy));
            Assert.That(battle.Play(Lab.InHand(battle, "kindle").Id, null).Accepted, Is.True);
            Assert.That(battle.Energy, Is.EqualTo(1));
            Assert.That(battle.Play(Lab.InHand(battle, "strike").Id, 1).Accepted, Is.True);
        }

        [Test]
        public void CardNotInHandIsRejected()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden"), "strike", "guard");
            var inDraw = battle.DrawPile[0];
            AssertUnchanged(battle, () => battle.Play(inDraw.Id, 1), RejectReason.NotInHand);
            AssertUnchanged(battle, () => battle.Play(999, 1), RejectReason.NotInHand);
        }

        [Test]
        public void SameCardCannotBePlayedTwice()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden"), "strike");
            var strike = Lab.InHand(battle, "strike");
            Assert.That(battle.Play(strike.Id, 1).Accepted, Is.True);
            AssertUnchanged(battle, () => battle.Play(strike.Id, 1), RejectReason.NotInHand);
        }

        [Test]
        public void TargetingRulesAreCheckedBeforeAnything()
        {
            var battle = Lab.Arranged(Lab.Encounter("imp", "imp"), "strike guard");
            var strike = Lab.InHand(battle, "strike");
            var guard = Lab.InHand(battle, "guard");
            AssertUnchanged(battle, () => battle.Play(strike.Id, null), RejectReason.TargetRequired);
            AssertUnchanged(battle, () => battle.Play(strike.Id, 0), RejectReason.TargetInvalid);
            AssertUnchanged(battle, () => battle.Play(strike.Id, 7), RejectReason.TargetInvalid);
            AssertUnchanged(battle, () => battle.Play(guard.Id, 1), RejectReason.TargetNotAllowed);
            battle.EnemyById(2)!.Hp = 0;
            AssertUnchanged(battle, () => battle.Play(strike.Id, 2), RejectReason.TargetInvalid);
        }

        [Test]
        public void EverythingIsRejectedAfterTheBattleEnds()
        {
            var battle = Lab.Arranged(Lab.Encounter("imp"), "finisher strike");
            Assert.That(battle.Play(Lab.InHand(battle, "finisher").Id, 1).Accepted, Is.True);
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Won));
            var strike = Lab.InHand(battle, "strike");
            AssertUnchanged(battle, () => battle.Play(strike.Id, 1), RejectReason.BattleOver);
            AssertUnchanged(battle, battle.EndTurn, RejectReason.BattleOver);
        }
    }
}
