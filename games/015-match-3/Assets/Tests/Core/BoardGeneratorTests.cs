using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class BoardGeneratorTests
    {
        // 既定の上限 1000 に対して、実際の作り直し回数は十分小さいことを確かめる上限。
        private const int ObservedAttemptLimit = 20;

        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void GeneratedBoardsHaveNoRunAndALegalMove(int colors)
        {
            var maxAttempts = 0;
            for (var seed = 0; seed < 2000; seed++)
            {
                var board = BoardGenerator.Generate(new SeededRandom(seed), colors, new Match3Config().MaxGenerationAttempts, out var attempts);
                maxAttempts = Math.Max(maxAttempts, attempts);

                Assert.That(MatchFinder.Find(board).IsEmpty, Is.True, $"seed {seed}");
                Assert.That(SwapRules.HasLegalMove(board), Is.True, $"seed {seed}");
                Assert.That(board.HasEmpty(), Is.False, $"seed {seed}");
                foreach (var piece in board.ToCells()) { Assert.That((int)piece, Is.InRange(1, colors)); }
            }

            Assert.That(maxAttempts, Is.LessThanOrEqualTo(ObservedAttemptLimit));
        }

        [Test]
        public void SameSeedMakesSameBoard()
        {
            Assert.That(BoardGenerator.Generate(new SeededRandom(5), 5, 100), Is.EqualTo(BoardGenerator.Generate(new SeededRandom(5), 5, 100)));
            Assert.That(BoardGenerator.Generate(new SeededRandom(5), 5, 100), Is.Not.EqualTo(BoardGenerator.Generate(new SeededRandom(6), 5, 100)));
        }

        [TestCase(2)]
        [TestCase(7)]
        public void ColorsOutOfRangeAreRejected(int colors)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BoardGenerator.Generate(new SeededRandom(1), colors, 10));
        }

        [Test]
        public void ZeroAttemptsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BoardGenerator.Generate(new SeededRandom(1), 5, 0));
        }
    }
}
