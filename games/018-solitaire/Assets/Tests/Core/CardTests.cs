using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class CardTests
    {
        [Test]
        public void AllIdsRoundTripThroughSuitRankAndText()
        {
            for (var id = 0; id < Card.DeckSize; id++)
            {
                var card = Card.FromId(id);
                Assert.That(new Card(card.Suit, card.Rank), Is.EqualTo(card));
                Assert.That(Card.Parse(card.ToString()), Is.EqualTo(card));
                Assert.That(card.Id, Is.EqualTo(id));
            }
        }

        [TestCase("AS", Suit.Sun, 1, true)]
        [TestCase("TF", Suit.Flame, 10, true)]
        [TestCase("QM", Suit.Moon, 12, false)]
        [TestCase("KW", Suit.Wave, 13, false)]
        public void TextGivesSuitRankAndColor(string text, Suit suit, int rank, bool red)
        {
            var card = Card.Parse(text);
            Assert.That(card.Suit, Is.EqualTo(suit));
            Assert.That(card.Rank, Is.EqualTo(rank));
            Assert.That(card.IsRed, Is.EqualTo(red));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("1S")]
        [TestCase("AX")]
        [TestCase("10S")]
        [TestCase("as")]
        public void InvalidTextIsRejected(string? text)
        {
            Assert.That(Card.TryParse(text, out _), Is.False);
        }

        [TestCase(0)]
        [TestCase(14)]
        public void RankOutOfRangeThrows(int rank)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Card(Suit.Sun, rank));
        }

        [Test]
        public void UndefinedSuitOrIdThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Card((Suit)4, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Card.FromId(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Card.FromId(Card.DeckSize));
        }

        [Test]
        public void PilesHaveDistinctIndicesKindsAndNames()
        {
            Assert.That(PileId.All, Has.Count.EqualTo(13));
            for (var i = 0; i < PileId.All.Count; i++)
            {
                var pile = PileId.All[i];
                Assert.That(pile.Index, Is.EqualTo(i));
                Assert.That(PileId.FromIndex(i), Is.EqualTo(pile));
            }

            Assert.That(PileId.Foundation(3).Kind, Is.EqualTo(PileKind.Foundation));
            Assert.That(PileId.Foundation(3).Slot, Is.EqualTo(3));
            Assert.That(PileId.Tableau(6).ToString(), Is.EqualTo("t7"));
            Assert.Throws<ArgumentOutOfRangeException>(() => PileId.Tableau(7));
            Assert.Throws<ArgumentOutOfRangeException>(() => PileId.Foundation(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => PileId.FromIndex(13));
        }
    }
}
