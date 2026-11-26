using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>ルート・報酬・除去・休憩・勝敗。</summary>
    public sealed class RunTests
    {
        private static readonly EncounterTier[] _firstTiers = { EncounterTier.Easy, EncounterTier.Normal };

        private static readonly EncounterTier[] _secondTiers = { EncounterTier.Normal, EncounterTier.Hard };

        private static Run NewRun(int seed = 1, DeckConfig? config = null) => new(Lab.Data, config ?? new DeckConfig(), seed);

        /// <summary>敵の HP を 1 にして、払える攻撃を当て続けて勝つ。</summary>
        private static void Win(Run run)
        {
            for (int guard = 0; guard < 200 && run.Stage == RunStage.Battle; guard++)
            {
                var battle = run.Battle!;
                foreach (var e in battle.Enemies.Where(e => e.Alive)) { e.Hp = 1; }
                battle.Player.Hp = battle.Player.MaxHp;
                var attack = battle.Hand.FirstOrDefault(c => battle.Affordable(c) && c.Def.Type == CardType.Attack);
                if (attack == null)
                {
                    run.EndTurn();
                    continue;
                }

                int? target = attack.Def.NeedsTarget ? battle.Enemies.First(e => e.Alive).Id : null;
                run.Play(attack.Id, target);
            }

            Assert.That(run.Stage, Is.Not.EqualTo(RunStage.Battle));
        }

        private static void Lose(Run run)
        {
            for (int guard = 0; guard < 200 && run.Stage == RunStage.Battle; guard++)
            {
                run.Battle!.Player.Hp = 1;
                run.EndTurn();
            }

            Assert.That(run.Stage, Is.EqualTo(RunStage.Defeat));
        }

        private static void AssertRejected(Run run, PlayResult result, RejectReason reason)
        {
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(reason));
        }

        [Test]
        public void RouteOffersEasyOrNormalThenNormalOrHardThenTheBoss([Range(1, 20)] int seed)
        {
            var run = NewRun(seed);
            Assert.That(run.Route[0].Select(o => o.Encounter.Tier), Is.EqualTo(_firstTiers));
            Assert.That(run.Route[1], Is.Empty);
            Assert.That(run.Route[2].Select(o => o.Encounter.Tier), Is.EqualTo(_secondTiers));
            Assert.That(run.Route[3].Single().Encounter.Tier, Is.EqualTo(EncounterTier.Boss));
            Assert.That(run.Route[2][0].Encounter, Is.Not.SameAs(run.Route[0][1].Encounter), "戦 1 と戦 2 の Normal は別");
            Assert.That(run.Route[0][1].Hard && run.Route[2][1].Hard && !run.Route[0][0].Hard, Is.True);
        }

        [Test]
        public void SameSeedGivesTheSameRouteAndSeedsVary()
        {
            Assert.That(NewRun(5).Hash(), Is.EqualTo(NewRun(5).Hash()));
            var routes = new HashSet<string>();
            for (int seed = 1; seed <= 30; seed++)
            {
                var r = NewRun(seed);
                routes.Add(string.Join(",", r.Route.SelectMany(n => n.Select(o => o.Encounter.Id))));
            }

            Assert.That(routes.Count, Is.GreaterThan(5));
        }

        [Test]
        public void WrongStageAndBadChoicesAreRejectedWithoutChange()
        {
            var run = NewRun();
            var hash = run.Hash();
            AssertRejected(run, run.ChooseEncounter(2), RejectReason.InvalidChoice);
            AssertRejected(run, run.ChooseEncounter(-1), RejectReason.InvalidChoice);
            AssertRejected(run, run.Play(1, null), RejectReason.WrongStage);
            AssertRejected(run, run.EndTurn(), RejectReason.WrongStage);
            AssertRejected(run, run.TakeReward(0), RejectReason.WrongStage);
            AssertRejected(run, run.RemoveCard(1), RejectReason.WrongStage);
            AssertRejected(run, run.LeaveReward(), RejectReason.WrongStage);
            AssertRejected(run, run.Rest(RestChoice.Heal), RejectReason.WrongStage);
            Assert.That(run.Hash(), Is.EqualTo(hash));
        }

        [Test]
        public void FullRouteToVictoryCarriesHpAndGrowsTheDeck()
        {
            var run = NewRun(3);
            Assert.That(run.ChooseEncounter(1).Accepted, Is.True);
            Assert.That(run.Stage, Is.EqualTo(RunStage.Battle));
            Assert.That(run.Battle!.Player.Hp, Is.EqualTo(70));
            run.Battle.Player.Hp = 50;
            Win(run);
            Assert.That(run.Stage, Is.EqualTo(RunStage.Reward));
            Assert.That(run.Hp, Is.EqualTo(70), "Win で全快にしてから勝っている");

            Assert.That(run.RewardOptions.Count, Is.EqualTo(3));
            Assert.That(run.RewardOptions.Distinct().Count(), Is.EqualTo(3));
            Assert.That(run.RewardOptions.All(c => c.Rarity != Rarity.Starter), Is.True);
            var picked = run.RewardOptions[2];
            Assert.That(run.TakeReward(2).Accepted, Is.True);
            Assert.That(run.Deck.Count, Is.EqualTo(11));
            Assert.That(run.Deck[10].Def, Is.SameAs(picked));
            Assert.That(run.Deck.Select(c => c.Id).Distinct().Count(), Is.EqualTo(11));
            AssertRejected(run, run.TakeReward(0), RejectReason.AlreadyDone);

            var strike = run.Deck.First(c => c.Def.Id == "strike");
            Assert.That(run.RemoveCard(strike.Id).Accepted, Is.True);
            Assert.That(run.Deck.Count, Is.EqualTo(10));
            AssertRejected(run, run.RemoveCard(run.Deck[0].Id), RejectReason.AlreadyDone);
            Assert.That(run.LeaveReward().Accepted, Is.True);

            Assert.That(run.Stage, Is.EqualTo(RunStage.Rest));
            Assert.That(run.Node, Is.EqualTo(Run.RestNode));
            Assert.That(run.Rest(RestChoice.MaxHp).Accepted, Is.True);
            Assert.That(run.MaxHp, Is.EqualTo(76));
            Assert.That(run.Hp, Is.EqualTo(76));

            Assert.That(run.Stage, Is.EqualTo(RunStage.ChooseEncounter));
            run.ChooseEncounter(0);
            Assert.That(run.Battle.Deck.Count, Is.EqualTo(10), "除去と報酬が次の戦闘のデッキに入る");
            Win(run);
            run.LeaveReward();
            Assert.That(run.Node, Is.EqualTo(Run.BossNode));
            Assert.That(run.Options.Count, Is.EqualTo(1));
            run.ChooseEncounter(0);
            Assert.That(run.Battle!.Encounter.Tier, Is.EqualTo(EncounterTier.Boss));
            Win(run);
            Assert.That(run.Stage, Is.EqualTo(RunStage.Victory));
            Assert.That(run.RewardOptions, Is.Empty, "ボスの後は報酬なし");
            Assert.That(run.BattlesWon, Is.EqualTo(3));
            AssertRejected(run, run.ChooseEncounter(0), RejectReason.WrongStage);
            AssertRejected(run, run.LeaveReward(), RejectReason.WrongStage);
        }

        [Test]
        public void RestHealsThirtyPercentCappedAtMax()
        {
            var run = NewRun();
            run.ChooseEncounter(0);
            Win(run);
            run.LeaveReward();
            Assert.That(run.RestHeal, Is.EqualTo(21));
            var before = run.Hp;
            run.Rest(RestChoice.Heal);
            Assert.That(run.Hp, Is.EqualTo(System.Math.Min(70, before + 21)));
        }

        [Test]
        public void RemovalRespectsTheMinimumDeckSizeAndRejectsUnknownCards()
        {
            var run = NewRun(1, new DeckConfig { MinDeckSize = 10 });
            run.ChooseEncounter(0);
            Win(run);
            Assert.That(run.CanRemove, Is.False);
            AssertRejected(run, run.RemoveCard(run.Deck[0].Id), RejectReason.DeckTooSmall);
            run.TakeReward(0);
            Assert.That(run.CanRemove, Is.True);
            AssertRejected(run, run.RemoveCard(999), RejectReason.InvalidChoice);
            Assert.That(run.RemoveCard(run.Deck[0].Id).Accepted, Is.True);
            Assert.That(run.Deck.Count, Is.EqualTo(10));
        }

        [Test]
        public void LeavingWithoutTakingSkipsTheCard()
        {
            var run = NewRun();
            run.ChooseEncounter(0);
            Win(run);
            run.LeaveReward();
            Assert.That(run.Deck.Count, Is.EqualTo(10));
        }

        [Test]
        public void DefeatEndsTheRunAndRejectsEverything()
        {
            var run = NewRun();
            run.ChooseEncounter(1);
            Lose(run);
            Assert.That(run.Hp, Is.EqualTo(0));
            Assert.That(run.IsOver, Is.True);
            var hash = run.Hash();
            AssertRejected(run, run.EndTurn(), RejectReason.WrongStage);
            AssertRejected(run, run.TakeReward(0), RejectReason.WrongStage);
            Assert.That(run.Hash(), Is.EqualTo(hash));
        }

        [Test]
        public void ResultsAreSettledEvenWhenTheBattleIsDrivenDirectly()
        {
            var run = NewRun();
            run.ChooseEncounter(0);
            var battle = run.Battle!;
            for (int guard = 0; guard < 100 && !battle.IsOver; guard++)
            {
                foreach (var e in battle.Enemies.Where(e => e.Alive)) { e.Hp = 1; }
                var attack = battle.Hand.FirstOrDefault(c => battle.Affordable(c) && c.Def.Type == CardType.Attack);
                if (attack == null) { battle.EndTurn(); }
                else { battle.Play(attack.Id, attack.Def.NeedsTarget ? battle.Enemies.First(e => e.Alive).Id : null); }
            }

            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Won));
            Assert.That(run.Stage, Is.EqualTo(RunStage.Reward));
            Assert.That(run.BattlesWon, Is.EqualTo(1));
            Assert.That(run.RewardOptions.Count, Is.EqualTo(3));
        }

        [Test]
        public void HardEncountersGiveRarerRewardsOnAverage()
        {
            int Rares(bool hard)
            {
                var count = 0;
                for (int seed = 1; seed <= 60; seed++)
                {
                    var run = NewRun(seed);
                    run.ChooseEncounter(hard ? 1 : 0);
                    Win(run);
                    count += run.RewardOptions.Count(c => c.Rarity == Rarity.Rare);
                }

                return count;
            }

            Assert.That(Rares(true), Is.GreaterThan(Rares(false)));
        }

        [Test]
        public void EveryRewardCardCanAppear()
        {
            var seen = new HashSet<string>();
            for (int seed = 1; seed <= 80; seed++)
            {
                var run = NewRun(seed);
                run.ChooseEncounter(seed % 2);
                Win(run);
                foreach (var c in run.RewardOptions) { seen.Add(c.Id); }
            }

            var pool = Lab.Data.Cards.Where(c => c.Rarity != Rarity.Starter).Select(c => c.Id);
            Assert.That(seen, Is.EquivalentTo(pool));
        }
    }
}
