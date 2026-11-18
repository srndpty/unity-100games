using System;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class SevenBagTests
    {
        private static PieceKind[] Deal(SevenBag bag, int count) => Enumerable.Range(0, count).Select(_ => bag.Take()).ToArray();

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(-12345)]
        [TestCase(int.MaxValue)]
        public void EveryBagHoldsAllSevenKindsOnce(int seed)
        {
            var bag = new SevenBag(seed);
            for (var round = 0; round < 200; round++)
            {
                var dealt = Deal(bag, PieceShapes.KindCount);
                Assert.That(dealt.Distinct().Count(), Is.EqualTo(PieceShapes.KindCount), $"{round} 袋目");
            }
        }

        [Test]
        public void SameKindNeverRepeatsMoreThanOnceAcrossABagBoundary()
        {
            var dealt = Deal(new SevenBag(7), PieceShapes.KindCount * 200);
            for (var i = 2; i < dealt.Length; i++)
            {
                Assert.That(dealt[i] == dealt[i - 1] && dealt[i] == dealt[i - 2], Is.False, $"{i} 個目");
            }
        }

        [Test]
        public void SameSeedDealsTheSameSequence()
        {
            Assert.That(Deal(new SevenBag(42), 70), Is.EqualTo(Deal(new SevenBag(42), 70)));
        }

        [Test]
        public void DifferentSeedsDealDifferentSequences()
        {
            Assert.That(Deal(new SevenBag(1), 70), Is.Not.EqualTo(Deal(new SevenBag(2), 70)));
        }

        [Test]
        public void PeekShowsTheUpcomingPiecesWithoutTakingThem()
        {
            var bag = new SevenBag(9);
            var preview = Enumerable.Range(0, 20).Select(bag.Peek).ToArray();
            Assert.That(Deal(bag, 20), Is.EqualTo(preview));
        }

        [Test]
        public void PeekDoesNotChangeTheSequence()
        {
            var peeked = new SevenBag(11);
            peeked.Peek(30);
            Assert.That(Deal(peeked, 40), Is.EqualTo(Deal(new SevenBag(11), 40)));
        }

        [Test]
        public void NegativePeekIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SevenBag(1).Peek(-1));
        }
    }
}
