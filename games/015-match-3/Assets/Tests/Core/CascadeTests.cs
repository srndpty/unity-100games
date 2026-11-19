using System;
using System.Linq;
using NUnit.Framework;
using static Game.Core.Tests.Match3TestKit;

namespace Game.Core.Tests
{
    public sealed class CascadeTests
    {
        private static readonly Board _holesBoard = Parse(
            "..LPLP",
            "P.PLPL",
            "LPLPLP",
            ".LPLPL",
            "LPLPLP",
            "P.PLPL");

        [Test]
        public void CollapseMovesPiecesDownAndLeavesEmptiesOnTop()
        {
            var result = BoardGravity.Collapse(_holesBoard);

            Assert.That(result.Board, Is.EqualTo(Parse(
                "..LPLP",
                "..PLPL",
                "P.LPLP",
                "LPPLPL",
                "LLLPLP",
                "PPPLPL")));
            Assert.That(result.MaxDistance, Is.EqualTo(1));
            Assert.That(result.Moves.Single(m => m.X == 1 && m.FromY == 3).ToY, Is.EqualTo(2));
            Assert.That(result.Moves.Count, Is.EqualTo(5));
        }

        [Test]
        public void CollapseOfFullBoardChangesNothing()
        {
            var board = DeadBoard();
            var result = BoardGravity.Collapse(board);

            Assert.That(result.Board, Is.SameAs(board));
            Assert.That(result.Moves, Is.Empty);
        }

        [Test]
        public void RefillFillsOnlyEmptiesAndDropsThemFromAbove()
        {
            var collapsed = BoardGravity.Collapse(_holesBoard).Board;
            var result = Refiller.Fill(collapsed, new SeededRandom(7), 5);

            Assert.That(result.Board.HasEmpty(), Is.False);
            Assert.That(result.Moves.Count, Is.EqualTo(5));
            foreach (var move in result.Moves)
            {
                Assert.That(collapsed.At(move.X, move.ToY), Is.EqualTo(Piece.Empty));
                Assert.That(move.FromY, Is.GreaterThanOrEqualTo(Board.Height));
                Assert.That(result.Board.At(move.X, move.ToY), Is.EqualTo(move.Piece));
            }

            for (var y = 0; y < Board.Height; y++)
            {
                for (var x = 0; x < Board.Width; x++)
                {
                    if (collapsed.At(x, y) != Piece.Empty) { Assert.That(result.Board.At(x, y), Is.EqualTo(collapsed.At(x, y))); }
                }
            }
        }

        [Test]
        public void RefillIsReproducibleFromSeed()
        {
            var collapsed = BoardGravity.Collapse(_holesBoard).Board;

            Assert.That(Refiller.Fill(collapsed, new SeededRandom(42), 5).Board, Is.EqualTo(Refiller.Fill(collapsed, new SeededRandom(42), 5).Board));
        }

        [Test]
        public void RefillDrawsOneValuePerEmptyCell()
        {
            var collapsed = BoardGravity.Collapse(_holesBoard).Board;
            var used = new SeededRandom(3);
            Refiller.Fill(collapsed, used, 5);
            var fresh = new SeededRandom(3);
            for (var i = 0; i < 5; i++) { fresh.NextUInt(); }

            Assert.That(used.NextUInt(), Is.EqualTo(fresh.NextUInt()));
        }

        [Test]
        public void RefillRejectsBoardWithFloatingPieces()
        {
            Assert.Throws<ArgumentException>(() => Refiller.Fill(_holesBoard, new SeededRandom(1), 5));
        }

        [Test]
        public void RefillDoesNotAvoidRuns()
        {
            // 補充は並びを知らないので、補充だけで並びが生まれることがある。
            var top = Parse(
                "......",
                "......",
                "......",
                "......",
                "......",
                "......");
            var made = Enumerable.Range(0, 50).Any(seed => !MatchFinder.Find(Refiller.Fill(top, new SeededRandom(seed), 3).Board).IsEmpty);

            Assert.That(made, Is.True);
        }

        [Test]
        public void StepWithoutMatchReturnsNullAndDrawsNothing()
        {
            var used = new SeededRandom(9);

            Assert.That(CascadeResolver.Step(DeadBoard(), 1, used, 5, 10), Is.Null);
            Assert.That(used.NextUInt(), Is.EqualTo(new SeededRandom(9).NextUInt()));
        }

        [Test]
        public void StepClearsCollapsesAndRefills()
        {
            var board = TwoChainBoard().Swapped(TwoChainSwap.From, TwoChainSwap.To);
            var step = CascadeResolver.Step(board, 1, new SeededRandom(1), 5, 10)!;

            Assert.That(step.Match.Cells, Is.EqualTo(new[] { P(0, 2), P(0, 3), P(0, 4) }));
            Assert.That(step.Cleared.At(0, 3), Is.EqualTo(Piece.Empty));
            Assert.That(step.After.HasEmpty(), Is.False);
            Assert.That(step.After.At(0, 2), Is.EqualTo(Piece.Kelp), "上の K が3段落ちる");
            Assert.That(step.FallRows, Is.EqualTo(3));
            Assert.That(step.Score, Is.EqualTo(30));
        }

        [Test]
        public void CascadeAfterRefillIsResolvedToTheEnd()
        {
            var board = TwoChainBoard().Swapped(TwoChainSwap.From, TwoChainSwap.To);
            for (var seed = 0; seed < 200; seed++)
            {
                var outcome = CascadeResolver.ResolveAll(board, new SeededRandom(seed), 5, 10);

                Assert.That(outcome.Chains, Is.GreaterThanOrEqualTo(2), $"seed {seed}");
                Assert.That(MatchFinder.Find(outcome.Board).IsEmpty, Is.True, $"seed {seed}");
                Assert.That(outcome.Board.HasEmpty(), Is.False, $"seed {seed}");
            }
        }

        [Test]
        public void SecondChainScoresDouble()
        {
            var board = TwoChainBoard().Swapped(TwoChainSwap.From, TwoChainSwap.To);
            var random = new SeededRandom(1);
            var first = CascadeResolver.Step(board, 1, random, 5, 10)!;
            var second = CascadeResolver.Step(first.After, 2, random, 5, 10)!;

            Assert.That(second.Match.Cells, Has.Member(P(0, 0)).And.Member(P(0, 1)).And.Member(P(0, 2)));
            Assert.That(second.Score, Is.EqualTo(second.Match.Cells.Count * 10 * 2));
        }

        [Test]
        public void CrossIsScoredOncePerCell()
        {
            var board = Parse(
                "LPLPLP",
                "PLPLPL",
                "LPCPLP",
                "PCCCPL",
                "LPCPLP",
                "PLPLPL");
            var step = CascadeResolver.Step(board, 1, new SeededRandom(1), 5, 10)!;

            Assert.That(step.Score, Is.EqualTo(50));
        }

        [Test]
        public void ScoreAdditionSaturates()
        {
            Assert.That(CascadeResolver.AddScore(long.MaxValue - 5, 10), Is.EqualTo(long.MaxValue));
            Assert.That(CascadeResolver.AddScore(100, 10), Is.EqualTo(110));
        }

        [Test]
        public void StepRejectsChainBelowOne()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => CascadeResolver.Step(DeadBoard(), 0, new SeededRandom(1), 5, 10));
        }
    }
}
