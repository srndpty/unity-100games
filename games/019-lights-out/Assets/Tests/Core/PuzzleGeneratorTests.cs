using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class PuzzleGeneratorTests
    {
        [Test]
        public void SameSeedGivesTheSamePuzzle()
        {
            for (var seed = 0; seed < 50; seed++)
            {
                var a = PuzzleGenerator.Generate(seed, 10);
                var b = PuzzleGenerator.Generate(seed, 10);
                Assert.That(b.Board, Is.EqualTo(a.Board));
                Assert.That(b.KnownSolution, Is.EqualTo(a.KnownSolution));
            }
        }

        [Test]
        public void DifferentSeedsGiveVariedPuzzles()
        {
            var boards = new HashSet<uint>();
            for (var seed = 0; seed < 100; seed++)
            {
                boards.Add(PuzzleGenerator.Generate(seed, 10).Board.Bits);
            }

            Assert.That(boards, Has.Count.GreaterThan(95));
        }

        [Test]
        public void EveryAllowedPressCountGivesVariedPuzzles()
        {
            // 押す順序は結果に影響しないので、全 25 マスを選ぶと全シードで同じ盤になる。上限はそれを避けている。
            for (var presses = LightsOutConfig.MinScramblePresses; presses <= LightsOutConfig.MaxScramblePresses; presses++)
            {
                var boards = new HashSet<uint>();
                for (var seed = 0; seed < 200; seed++)
                {
                    boards.Add(PuzzleGenerator.Generate(seed, presses).Board.Bits);
                }

                // 1 マスだけ・1 マス残しは高々 25 通りなので、その数に近い多様性を求める。
                Assert.That(boards, Has.Count.GreaterThanOrEqualTo(20), $"押下数 {presses}");
            }
        }

        [Test]
        public void KnownSolutionHasTheRequestedPressesAndRebuildsTheBoard()
        {
            for (var presses = LightsOutConfig.MinScramblePresses; presses <= LightsOutConfig.MaxScramblePresses; presses++)
            {
                for (var seed = 0; seed < 40; seed++)
                {
                    var puzzle = PuzzleGenerator.Generate(seed, presses);
                    Assert.That(BitCount.Of(puzzle.KnownSolution), Is.EqualTo(presses));
                    Assert.That(puzzle.Board.IsDark, Is.False);
                    Assert.That(LightsBoard.Dark.PressAll(puzzle.KnownSolution), Is.EqualTo(puzzle.Board));
                    Assert.That(puzzle.Board.PressAll(puzzle.KnownSolution).IsDark, Is.True);
                }
            }
        }

        [Test]
        public void EveryGeneratedBoardIsSolvedBySolver()
        {
            for (var presses = LightsOutConfig.MinScramblePresses; presses <= LightsOutConfig.MaxScramblePresses; presses++)
            {
                for (var seed = 0; seed < 40; seed++)
                {
                    var board = PuzzleGenerator.Generate(seed * 31 + presses, presses).Board;
                    Assert.That(Gf2Solver.TrySolveMinimal(board, out var minimal), Is.True, board.ToString());
                    Assert.That(board.PressAll(minimal).IsDark, Is.True);
                    Assert.That(LightsTestKit.AllSolutionsByChasing(board), Has.Count.EqualTo(4));
                }
            }
        }

        [Test]
        public void ThousandDefaultPuzzlesAreSolvable()
        {
            for (var seed = 0; seed < 1000; seed++)
            {
                var board = PuzzleGenerator.Generate(seed, 10).Board;
                Assert.That(Gf2Solver.TrySolve(board, out var solution), Is.True, $"seed {seed}");
                Assert.That(board.PressAll(solution).IsDark, Is.True);
            }
        }

        [Test]
        public void KernelSizedScramblesNeverProduceTheDarkBoard()
        {
            // 核の元と同じ大きさの押下では全消灯になりうる。引き直しで必ず点灯のある盤になることを確かめる。
            var sizes = new HashSet<int>();
            foreach (var vector in new[] { Gf2Solver.Kernel[0], Gf2Solver.Kernel[1], Gf2Solver.Kernel[0] ^ Gf2Solver.Kernel[1] })
            {
                sizes.Add(BitCount.Of(vector));
            }

            foreach (var size in sizes)
            {
                for (var seed = 0; seed < 2000; seed++)
                {
                    Assert.That(PuzzleGenerator.Generate(seed, size).Board.IsDark, Is.False);
                }
            }
        }

        [Test]
        public void PuzzlesAreIdenticalAcrossRuntimes()
        {
            // 整数だけで作るので Mono（Unity）と .NET で一致する。両方の環境でこの値を照合する。
            var hash = 2166136261u;
            for (var seed = 1; seed <= 100; seed++)
            {
                var puzzle = PuzzleGenerator.Generate(seed, 10);
                hash = unchecked((hash ^ puzzle.Board.Bits) * 16777619u);
                hash = unchecked((hash ^ puzzle.KnownSolution) * 16777619u);
            }

            Assert.That(hash, Is.EqualTo(2182070728u));
        }

        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(25)]
        [TestCase(26)]
        public void OutOfRangePressCountIsRejected(int presses)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PuzzleGenerator.Generate(1, presses));
        }
    }
}
