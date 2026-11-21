using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class LightsOutGameTests
    {
        private static LightsOutGame NewGame(int seed = 1, int presses = 10) =>
            new(new LightsOutConfig { ScramblePresses = presses }, seed);

        private static void PressAll(LightsOutGame game, uint presses)
        {
            for (var index = 0; index < LightsBoard.CellCount; index++)
            {
                if ((presses & (1u << index)) != 0)
                {
                    Assert.That(game.Press(index), Is.EqualTo(PressResult.Applied));
                }
            }
        }

        [Test]
        public void NewGameStartsFromThePuzzle()
        {
            var game = NewGame(seed: 5);
            Assert.That(game.Seed, Is.EqualTo(5));
            Assert.That(game.Board, Is.EqualTo(PuzzleGenerator.Generate(5, 10).Board));
            Assert.That(game.Moves, Is.Zero);
            Assert.That(game.Status, Is.EqualTo(GameStatus.Playing));
            Assert.That(game.IsOver, Is.False);
        }

        [Test]
        public void OptimalMovesIsTheMinimalSolutionSizeAndNoMoreThanTheScramble()
        {
            for (var seed = 0; seed < 100; seed++)
            {
                var game = NewGame(seed);
                Assert.That(game.OptimalMoves, Is.EqualTo(BitCount.Of(game.MinimalSolution())));
                Assert.That(game.OptimalMoves, Is.LessThanOrEqualTo(10));
                Assert.That(game.OptimalMoves, Is.GreaterThan(0));
            }
        }

        [Test]
        public void PressFlipsTheBoardAndCountsMoves()
        {
            var game = NewGame();
            var before = game.Board;
            Assert.That(game.Press(12), Is.EqualTo(PressResult.Applied));
            Assert.That(game.Board, Is.EqualTo(before.Press(12)));
            Assert.That(game.Moves, Is.EqualTo(1));

            Assert.That(game.Press(12), Is.EqualTo(PressResult.Applied));
            Assert.That(game.Board, Is.EqualTo(before));
            Assert.That(game.Moves, Is.EqualTo(2), "同じマスを 2 回押しても 2 手と数える");
        }

        [Test]
        public void PressingTheMinimalSolutionClearsTheGame()
        {
            var game = NewGame(seed: 8);
            var solution = game.MinimalSolution();
            PressAll(game, solution);

            Assert.That(game.Board.IsDark, Is.True);
            Assert.That(game.Status, Is.EqualTo(GameStatus.Cleared));
            Assert.That(game.Moves, Is.EqualTo(game.OptimalMoves));
        }

        [Test]
        public void MinimalSolutionFollowsTheCurrentBoard()
        {
            var game = NewGame(seed: 3);
            game.Press(0);
            game.Press(24);
            game.Press(7);
            var solution = game.MinimalSolution();
            Assert.That(game.Board.PressAll(solution).IsDark, Is.True);

            PressAll(game, solution);
            Assert.That(game.Status, Is.EqualTo(GameStatus.Cleared));
            Assert.That(game.Moves, Is.EqualTo(3 + BitCount.Of(solution)));
        }

        [Test]
        public void PressAfterClearIsRejectedWithoutChangingState()
        {
            var game = NewGame(seed: 4);
            PressAll(game, game.MinimalSolution());
            var moves = game.Moves;

            Assert.That(game.Press(0), Is.EqualTo(PressResult.Rejected));
            Assert.That(game.Board.IsDark, Is.True);
            Assert.That(game.Moves, Is.EqualTo(moves));
            Assert.That(game.Reset(), Is.False);
            Assert.That(game.GiveUp(), Is.False);
            Assert.That(game.Status, Is.EqualTo(GameStatus.Cleared));
        }

        [Test]
        public void GiveUpEndsTheGameOnce()
        {
            var game = NewGame();
            game.Press(6);
            var board = game.Board;

            Assert.That(game.GiveUp(), Is.True);
            Assert.That(game.Status, Is.EqualTo(GameStatus.GaveUp));
            Assert.That(game.GiveUp(), Is.False, "重ねてあきらめても何も起きない");
            Assert.That(game.Press(6), Is.EqualTo(PressResult.Rejected));
            Assert.That(game.Reset(), Is.False);
            Assert.That(game.Board, Is.EqualTo(board));
            Assert.That(game.Moves, Is.EqualTo(1));
            Assert.That(game.Board.PressAll(game.MinimalSolution()).IsDark, Is.True, "あきらめた盤の解答を出せる");
        }

        [Test]
        public void ResetRestoresThePuzzleAndMoves()
        {
            var game = NewGame(seed: 6);
            game.Press(1);
            game.Press(2);

            Assert.That(game.Reset(), Is.True);
            Assert.That(game.Board, Is.EqualTo(game.Puzzle.Board));
            Assert.That(game.Moves, Is.Zero);
            Assert.That(game.Status, Is.EqualTo(GameStatus.Playing));
        }

        [TestCase(-1)]
        [TestCase(25)]
        public void OutOfRangePressIsRejected(int index)
        {
            var game = NewGame();
            Assert.Throws<ArgumentOutOfRangeException>(() => game.Press(index));
            Assert.That(game.Moves, Is.Zero);
        }

        [Test]
        public void InvalidConfigIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => _ = new LightsOutGame(null!, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = NewGame(presses: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = NewGame(presses: 26));
        }

        [TestCase(1)]
        [TestCase(25)]
        public void BoundaryConfigIsAccepted(int presses)
        {
            var game = NewGame(presses: presses);
            Assert.That(game.Board.IsDark, Is.False);
        }

        [Test]
        public void ConfigCopyIsIndependent()
        {
            var config = new LightsOutConfig { ScramblePresses = 7 };
            var copy = config.Copy();
            config.ScramblePresses = 3;
            Assert.That(copy.ScramblePresses, Is.EqualTo(7));
        }
    }
}
