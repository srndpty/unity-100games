using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class DealTests
    {
        [Test]
        public void DealsOneToSevenCardsWithOnlyTheTopFaceUp()
        {
            var table = Deal.Create(1, 1);
            for (var column = 0; column < PileId.TableauCount; column++)
            {
                var pile = PileId.Tableau(column);
                Assert.That(table.CountOf(pile), Is.EqualTo(column + 1));
                Assert.That(table.FaceDownCountOf(pile), Is.EqualTo(column));
            }

            Assert.That(table.CountOf(PileId.Stock), Is.EqualTo(24));
            Assert.That(table.FaceDownCountOf(PileId.Stock), Is.EqualTo(24));
            Assert.That(table.CountOf(PileId.Waste), Is.EqualTo(0));
            Assert.That(table.FoundationTotal, Is.EqualTo(0));
            table.ValidateInvariants();
        }

        [Test]
        public void SameSeedGivesTheSameDealAndOthersDiffer()
        {
            var first = TableText.Format(Deal.Create(42, 1));
            Assert.That(TableText.Format(Deal.Create(42, 1)), Is.EqualTo(first));
            var distinct = Enumerable.Range(0, 50).Select(seed => TableText.Format(Deal.Create(seed, 1))).Distinct().Count();
            Assert.That(distinct, Is.EqualTo(50));
        }

        [Test]
        public void ShuffleIsAPermutation()
        {
            for (var seed = -5; seed < 50; seed++)
            {
                var ids = Deal.Shuffled(seed).Select(c => c.Id).OrderBy(id => id);
                Assert.That(ids, Is.EqualTo(Enumerable.Range(0, Card.DeckSize)));
            }
        }

        /// <summary>シャッフルは整数演算だけなので、Mono と .NET で同じ並びになる。</summary>
        [Test]
        public void SeedOneShuffleIsFixedAcrossRuntimes()
        {
            var order = string.Join(" ", Deal.Shuffled(1).Select(c => c.ToString()));
            Assert.That(order, Is.EqualTo(ExpectedSeedOneOrder));
        }

        private const string ExpectedSeedOneOrder =
            "KH 4H AH KS JH KD 3C 3S 7H 7S 7D 6S 6D QS AD 5C 8S QD 8C QC QH 7C 9C 2D 5S 8D " +
            "JS JC 3H 2H 4S 5D 8H 9H 9D 6H 4C KC 6C AC 2C TH 3D 5H JD 9S TC TS 4D 2S TD AS";
    }
}
