using System;
using System.Linq;
using NUnit.Framework;
using static Game.Core.Tests.OthelloTestKit;

namespace Game.Core.Tests
{
    public sealed class SearcherTests
    {
        [TestCase(1, 30)]
        [TestCase(2, 30)]
        [TestCase(3, 15)]
        [TestCase(4, 4)]
        public void AlphaBetaGivesTheSameValueAsPlainMinimax(int depth, int games)
        {
            var checkedPositions = 0;
            foreach (var position in SamplePositions(games, 100).Where((_, i) => i % 3 == 0))
            {
                if (position.LegalMoves() == 0) { continue; }
                var result = Searcher.Search(position, depth);
                Assert.That(result.Score, Is.EqualTo(PlainMinimax(position, depth)), BoardText.Format(position).Aggregate((a, b) => a + "\n" + b));
                checkedPositions++;
            }

            Assert.That(checkedPositions, Is.GreaterThan(games * 10));
        }

        [Test]
        public void BestMovesAreExactlyTheMovesWithTheBestPlainValue()
        {
            foreach (var position in SamplePositions(10, 500).Where((_, i) => i % 4 == 0))
            {
                if (position.LegalMoves() == 0) { continue; }
                const int depth = 3;
                var result = Searcher.Search(position, depth);
                var values = Game.Core.Bits.ToSquares(position.LegalMoves()).ToDictionary(s => s, s => -PlainMinimax(position.Play(s), depth - 1));
                var best = values.Values.Max();
                Assert.That(result.Score, Is.EqualTo(best));
                Assert.That(result.BestMoves, Is.EquivalentTo(values.Where(v => v.Value == best).Select(v => v.Key)));
                Assert.That(result.BestMoves, Does.Contain(result.Move));
            }
        }

        [Test]
        public void DepthOneTakesAnAvailableCorner()
        {
            var position = Parse(Side.Black,
                ".WWB....",
                "........",
                "........",
                "...WB...",
                "...BW...",
                "........",
                "........",
                "........");
            Assert.That(Searcher.Search(position, 1).Move, Is.EqualTo(S("a1")));
        }

        [Test]
        public void MoveThatEndsTheGameIsScoredByTheFinalDiscDifference()
        {
            // 白の c1 で黒の石がなくなり、3対0で終局する。
            var position = Parse(Side.White,
                "WB......",
                "........",
                "........",
                "........",
                "........",
                "........",
                "........",
                "........");
            var result = Searcher.Search(position, OthelloConfig.MaxDepth);
            Assert.That(result.Move, Is.EqualTo(S("c1")));
            Assert.That(result.Score, Is.EqualTo(3 * Evaluator.TerminalScale));
        }

        [Test]
        public void SameRandomSeedChoosesTheSameMove()
        {
            foreach (var position in SamplePositions(5, 900).Where(p => p.LegalMoves() != 0))
            {
                var a = Searcher.Search(position, 2, new SeededRandom(42));
                var b = Searcher.Search(position, 2, new SeededRandom(42));
                Assert.That(a.Move, Is.EqualTo(b.Move));
                Assert.That(a.Nodes, Is.EqualTo(b.Nodes));
            }
        }

        [Test]
        public void RandomOnlyChoosesAmongTheBestMoves()
        {
            // 初期局面の4手は対称なので、深さ1ではすべて同じ評価になる。
            var chosen = Enumerable.Range(0, 200).Select(seed => Searcher.Search(Position.Initial, 1, new SeededRandom(seed))).ToList();
            Assert.That(chosen[0].BestMoves, Is.EquivalentTo(Game.Core.Bits.ToSquares(Bits("d3", "c4", "f5", "e6"))));
            Assert.That(chosen.Select(r => r.Move).Distinct().Count(), Is.EqualTo(4));
            Assert.That(Searcher.Search(Position.Initial, 1).Move, Is.EqualTo(chosen[0].BestMoves[0]), "乱数なしは探索順で最初の手");
        }

        [Test]
        public void DeeperSearchVisitsMoreNodes()
        {
            var position = Position.Initial.Play(S("f5")).Play(S("d6"));
            var nodes = Enumerable.Range(1, OthelloConfig.MaxDepth).Select(d => Searcher.Search(position, d).Nodes).ToList();
            for (var i = 1; i < nodes.Count; i++) { Assert.That(nodes[i], Is.GreaterThan(nodes[i - 1])); }
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(OthelloConfig.MaxDepth + 1)]
        public void DepthOutOfRangeIsRejected(int depth)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Searcher.Search(Position.Initial, depth));
        }

        [Test]
        public void SearchWithoutLegalMovesIsRejected()
        {
            var noMoves = Parse(Side.Black,
                "WB......",
                "........",
                "........",
                "........",
                "........",
                "........",
                "........",
                "........");
            Assert.Throws<InvalidOperationException>(() => Searcher.Search(noMoves, 1));
        }

        [Test]
        public void EvaluationIsAntisymmetricBetweenTheSides()
        {
            foreach (var position in SamplePositions(5, 300))
            {
                if (position.IsTerminal)
                {
                    Assert.That(Evaluator.Evaluate(position), Is.EqualTo(-Evaluator.Evaluate(position.Pass())));
                    continue;
                }

                Assert.That(Math.Abs(Evaluator.Evaluate(position)), Is.LessThan(Evaluator.TerminalScale), "途中の評価は終局の1石差より小さい");
                Assert.That(Evaluator.Evaluate(position), Is.EqualTo(-Evaluator.Evaluate(position.Pass())));
            }
        }
    }
}
