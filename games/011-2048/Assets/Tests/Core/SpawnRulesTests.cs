using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class SpawnRulesTests
    {
        private static readonly int[] _emptyCells = { 1, 6, 11, 12 };

        [Test]
        public void Choose_OnlyPicksEmptyCellsAndReachesAllOfThem()
        {
            var board = BoardState.FromValues(
                2, 0, 2, 2,
                2, 2, 0, 2,
                2, 2, 2, 0,
                0, 2, 2, 2);
            var random = new SeededRandom(1);
            var seen = new HashSet<int>();
            for (var i = 0; i < 500; i++)
            {
                var tile = SpawnRules.Choose(board, random, 10);
                Assert.That(board[tile.Cell], Is.Zero);
                seen.Add(tile.Cell);
            }

            Assert.That(seen, Is.EquivalentTo(_emptyCells));
        }

        [TestCase(0, 2)]
        [TestCase(100, 4)]
        public void Choose_HonorsPercentBoundaries(int percent, int expected)
        {
            var random = new SeededRandom(2);
            for (var i = 0; i < 500; i++)
            {
                Assert.That(SpawnRules.Choose(BoardState.Empty, random, percent).Value, Is.EqualTo(expected));
            }
        }

        [Test]
        public void Choose_DefaultChanceProducesFoursAboutOneInTen()
        {
            var random = new SeededRandom(3);
            var fours = 0;
            for (var i = 0; i < 10000; i++)
            {
                if (SpawnRules.Choose(BoardState.Empty, random, 10).Value == 4) { fours++; }
            }

            Assert.That(fours, Is.InRange(800, 1200));
        }

        [Test]
        public void Choose_SameSeedGivesSameSequence()
        {
            var first = new SeededRandom(4);
            var second = new SeededRandom(4);
            for (var i = 0; i < 100; i++)
            {
                var a = SpawnRules.Choose(BoardState.Empty, first, 10);
                var b = SpawnRules.Choose(BoardState.Empty, second, 10);
                Assert.That((a.Cell, a.Value), Is.EqualTo((b.Cell, b.Value)));
            }
        }

        [Test]
        public void Choose_DoesNotChangeTheBoard()
        {
            SpawnRules.Choose(BoardState.Empty, new SeededRandom(5), 10);
            Assert.That(BoardState.Empty.TileCount, Is.Zero);
        }

        [Test]
        public void Choose_RejectsInvalidArguments()
        {
            var random = new SeededRandom(6);
            var full = BoardState.FromValues(2, 4, 2, 4, 4, 2, 4, 2, 2, 4, 2, 4, 4, 2, 4, 2);
            Assert.Throws<ArgumentNullException>(() => SpawnRules.Choose(null!, random, 10));
            Assert.Throws<ArgumentNullException>(() => SpawnRules.Choose(BoardState.Empty, null!, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpawnRules.Choose(BoardState.Empty, random, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => SpawnRules.Choose(BoardState.Empty, random, 101));
            Assert.Throws<InvalidOperationException>(() => SpawnRules.Choose(full, random, 10));
        }
    }
}
