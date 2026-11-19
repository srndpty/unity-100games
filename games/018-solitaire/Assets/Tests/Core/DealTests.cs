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
            "KS 4S AS KW JS KF 3M 3W 7S 7W 7F 6W 6F QW AF 5M 8W QF 8M QM QS 7M 9M 2F 5W 8F " +
            "JW JM 3S 2S 4W 5F 8S 9S 9F 6S 4M KM 6M AM 2M TS 3F 5S JF 9W TM TW 4F 2W TF AW";
    }
}
