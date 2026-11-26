using System.Collections.Generic;
using System.Linq;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>廃棄したカードは捨て札・山札・手札へ戻らない。</summary>
    public sealed class ExhaustTests
    {
        [Test]
        public void ExhaustCardGoesToTheExhaustPileNotTheDiscard()
        {
            var battle = Lab.Arranged(Lab.Encounter("kiln"), "finisher strike");
            battle.Play(1, 1);
            Assert.That(Lab.Ids(battle.ExhaustPile), Is.EqualTo("1"));
            Assert.That(battle.DiscardPile.Select(c => c.Id), Has.No.Member(1));
            Assert.That(battle.Events[battle.Events.Count - 1].Kind, Is.EqualTo(EventKind.Exhausted));
        }

        [Test]
        public void PowerCardIsRemovedLikeExhaustAndItsEffectStays()
        {
            var battle = Lab.Arranged(Lab.Encounter("kiln"), "temper strike");
            battle.Play(1, null);
            Assert.That(Lab.Ids(battle.ExhaustPile), Is.EqualTo("1"));
            Assert.That(battle.Player.Status(StatusKind.Strength), Is.EqualTo(2));
            var start = battle.Events.Count;
            battle.Play(2, 1);
            Assert.That(Lab.TraceFrom(battle, start), Is.EqualTo("Play:2 Dmg:1:8+0 Disc:2"));
        }

        [Test]
        public void ExhaustedCardsNeverComeBackThroughReshuffles()
        {
            // 手札：finisher（廃棄）と glance（1 枚引く）。山札は空、捨て札に 2 枚。何ターン回しても finisher は戻らない。
            var battle = Lab.Arranged(Lab.Encounter("kiln"), "finisher glance", discard: "guard strike");
            battle.EnemyById(1)!.Hp = 999;
            battle.Play(1, 1);
            for (int turn = 0; turn < 20 && !battle.IsOver; turn++)
            {
                foreach (var c in battle.Hand.ToArray())
                {
                    if (battle.Affordable(c)) { battle.Play(c.Id, c.Def.NeedsTarget ? 1 : null); }
                }

                Assert.That(battle.DrawPile.Concat(battle.Hand).Concat(battle.DiscardPile).Select(c => c.Id), Has.No.Member(1));
                battle.EndTurn();
            }

            Assert.That(Lab.Ids(battle.ExhaustPile), Is.EqualTo("1"));
            Assert.That(battle.Events.Count(e => e.Kind == EventKind.Shuffled), Is.GreaterThan(2));
        }

        [Test]
        public void ExhaustFromHandTakesAnotherCardAndDrawsAfter()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden"), "purge strike", "guard twin");
            var start = battle.Events.Count;
            battle.Play(1, null);

            // 手札は strike だけなので、それが廃棄され、その後に 2 枚引く。purge 自身は捨て札。
            Assert.That(Lab.TraceFrom(battle, start), Is.EqualTo("Play:1 Exh:2 Draw:3 Draw:4 Disc:1"));
            Assert.That(Lab.Ids(battle.ExhaustPile), Is.EqualTo("2"));
            Assert.That(Lab.Ids(battle.DiscardPile), Is.EqualTo("1"));
        }

        [Test]
        public void ExhaustFromEmptyHandDoesNothing()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden"), "purge", "guard");
            var start = battle.Events.Count;
            battle.Play(1, null);
            Assert.That(Lab.TraceFrom(battle, start), Is.EqualTo("Play:1 Draw:2 Disc:1"));
        }

        [Test]
        public void EndOfTurnDiscardsAnExhaustCardThatWasNotPlayed()
        {
            var battle = Lab.Arranged(Lab.Encounter("warden"), "finisher strike", "guard guard guard guard guard");
            battle.EndTurn();
            Assert.That(battle.ExhaustPile, Is.Empty);
            Assert.That(Lab.Ids(battle.DiscardPile), Is.EqualTo("1 2"));
        }

        [Test]
        public void ZonesInvariantHoldsInLongRandomBattles([Range(1, 40)] int seed)
        {
            var data = Lab.Data;
            var ids = data.Cards.Select(c => c.Id).Concat(data.StarterDeck.Select(c => c.Id)).ToArray();
            var deck = Lab.Deck(ids);
            var encounter = data.Encounters[seed % data.Encounters.Count];
            var battle = new Battle(data, new DeckConfig(), deck, 70, 70, encounter, seed, 0);
            var rng = new RandomSeries(seed * 31);
            var exhausted = new List<int>();
            for (int step = 0; step < 300 && !battle.IsOver; step++)
            {
                var playable = battle.Hand.Where(battle.Affordable).ToList();
                if (playable.Count == 0 || rng.Next(playable.Count + 1) == 0)
                {
                    Assert.That(battle.EndTurn().Accepted, Is.True);
                }
                else
                {
                    var card = playable[rng.Next(playable.Count)];
                    int? target = null;
                    if (card.Def.NeedsTarget)
                    {
                        var alive = battle.Enemies.Where(e => e.Alive).ToList();
                        target = alive[rng.Next(alive.Count)].Id;
                    }

                    Assert.That(battle.Play(card.Id, target).Accepted, Is.True);
                }

                Lab.AssertZones(battle, deck.Length);
                Assert.That(battle.Resolving, Is.Null);
                var now = battle.ExhaustPile.Select(c => c.Id).ToList();
                Assert.That(now.Take(exhausted.Count), Is.EqualTo(exhausted), "廃棄の山は増えるだけ");
                exhausted = now;
                Assert.That(battle.DiscardPile.Concat(battle.DrawPile).Concat(battle.Hand).Select(c => c.Id).Intersect(exhausted), Is.Empty);
            }
        }
    }
}
