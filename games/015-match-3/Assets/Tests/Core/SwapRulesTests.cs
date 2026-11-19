using System.Collections.Generic;
using NUnit.Framework;
using static Game.Core.Tests.Match3TestKit;

namespace Game.Core.Tests
{
    public sealed class SwapRulesTests
    {
        [Test]
        public void SwapThatMakesRunIsLegal()
        {
            Assert.That(SwapRules.Check(TwoChainBoard(), TwoChainSwap.From, TwoChainSwap.To), Is.EqualTo(SwapVerdict.Legal));
            Assert.That(SwapRules.Check(TwoChainBoard(), TwoChainSwap.To, TwoChainSwap.From), Is.EqualTo(SwapVerdict.Legal));
        }

        [Test]
        public void AdjacentSwapWithoutRunIsNoMatch()
        {
            Assert.That(SwapRules.Check(DeadBoard(), P(0, 0), P(1, 0)), Is.EqualTo(SwapVerdict.NoMatch));
            Assert.That(SwapRules.Check(DeadBoard(), P(2, 3), P(2, 4)), Is.EqualTo(SwapVerdict.NoMatch));
        }

        [Test]
        public void SameColorsSwapIsNoMatch()
        {
            // 同じ色どうしの入れ替えは盤面が変わらないので、並びができない。
            var board = Parse(
                "LPLPLP",
                "PLPLPL",
                "LPLPLP",
                "PLPLPL",
                "LPLPLP",
                "CCLPLP");

            Assert.That(SwapRules.Check(board, P(0, 0), P(1, 0)), Is.EqualTo(SwapVerdict.NoMatch));
        }

        [Test]
        public void SameCellIsRejected()
        {
            Assert.That(SwapRules.Check(TwoChainBoard(), P(0, 2), P(0, 2)), Is.EqualTo(SwapVerdict.SameCell));
        }

        [TestCase(0, 2, 1, 3)]
        [TestCase(0, 2, 2, 2)]
        [TestCase(0, 0, 5, 5)]
        public void NonAdjacentIsRejected(int ax, int ay, int bx, int by)
        {
            Assert.That(SwapRules.Check(TwoChainBoard(), P(ax, ay), P(bx, by)), Is.EqualTo(SwapVerdict.NotAdjacent));
        }

        [TestCase(0, 0, -1, 0)]
        [TestCase(5, 5, 6, 5)]
        [TestCase(5, 5, 5, 6)]
        [TestCase(0, 0, 0, -1)]
        [TestCase(-1, 0, -2, 0)]
        public void OutsideTheBoardIsRejected(int ax, int ay, int bx, int by)
        {
            Assert.That(SwapRules.Check(TwoChainBoard(), P(ax, ay), P(bx, by)), Is.EqualTo(SwapVerdict.OutOfBoard));
        }

        [Test]
        public void DeadBoardHasNoLegalMove()
        {
            Assert.That(SwapRules.HasLegalMove(DeadBoard()), Is.False);
            Assert.That(MatchFinder.Find(DeadBoard()).IsEmpty, Is.True);
        }

        [Test]
        public void LegalMovesMatchBruteForceOnRandomBoards()
        {
            for (var seed = 0; seed < 300; seed++)
            {
                var board = BoardGenerator.Generate(new SeededRandom(seed), 3 + (seed % 4), 1000);
                var expected = new List<Swap>();
                for (var y = 0; y < Board.Height; y++)
                {
                    for (var x = 0; x < Board.Width; x++)
                    {
                        foreach (var to in new[] { P(x + 1, y), P(x, y + 1) })
                        {
                            if (Board.IsInside(to) && !MatchFinder.Find(board.Swapped(P(x, y), to)).IsEmpty) { expected.Add(new Swap(P(x, y), to)); }
                        }
                    }
                }

                Assert.That(SwapRules.LegalMoves(board), Is.EqualTo(expected), $"seed {seed}");
            }
        }
    }
}
