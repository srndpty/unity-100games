using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class Gf2SolverTests
    {
        [Test]
        public void KernelHasTwoIndependentQuietPatterns()
        {
            Assert.That(Gf2Solver.Kernel, Has.Count.EqualTo(2));
            var a = Gf2Solver.Kernel[0];
            var b = Gf2Solver.Kernel[1];
            Assert.That(a, Is.Not.Zero);
            Assert.That(b, Is.Not.Zero);
            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(PressMasks.Combine(a), Is.Zero);
            Assert.That(PressMasks.Combine(b), Is.Zero);
            Assert.That(PressMasks.Combine(a ^ b), Is.Zero);
        }

        [Test]
        public void KernelMatchesTheSolutionsOfTheDarkBoardByChasing()
        {
            var chased = LightsTestKit.AllSolutionsByChasing(LightsBoard.Dark).OrderBy(s => s).ToArray();
            var kernel = new[] { 0u, Gf2Solver.Kernel[0], Gf2Solver.Kernel[1], Gf2Solver.Kernel[0] ^ Gf2Solver.Kernel[1] }
                .OrderBy(s => s).ToArray();
            Assert.That(kernel, Is.EqualTo(chased));
        }

        [Test]
        public void SolvabilityMatchesLightChasingOnRandomBoards()
        {
            var random = new SeededRandom(2024);
            var solvable = 0;
            var unsolvable = 0;
            for (var trial = 0; trial < 2000; trial++)
            {
                var board = LightsTestKit.RandomBoard(random);
                var chased = LightsTestKit.AllSolutionsByChasing(board);
                var solved = Gf2Solver.TrySolve(board, out var solution);

                Assert.That(solved, Is.EqualTo(chased.Count > 0), board.ToString());
                if (solved)
                {
                    solvable++;
                    Assert.That(chased, Has.Count.EqualTo(4));
                    Assert.That(chased, Does.Contain(solution));
                }
                else
                {
                    unsolvable++;
                    Assert.That(solution, Is.Zero);
                }
            }

            // 可解な盤は 4 分の 1。両方の場合を十分に調べたことを確かめる。
            Assert.That(solvable, Is.GreaterThan(300));
            Assert.That(unsolvable, Is.GreaterThan(1200));
        }

        [Test]
        public void SolutionClearsTheBoard()
        {
            var random = new SeededRandom(99);
            for (var trial = 0; trial < 500; trial++)
            {
                var board = LightsBoard.Dark.PressAll(random.NextUInt() & LightsBoard.AllCells);
                Assert.That(Gf2Solver.TrySolve(board, out var solution), Is.True);
                Assert.That(board.PressAll(solution).IsDark, Is.True);
                Assert.That(Gf2Solver.TrySolveMinimal(board, out var minimal), Is.True);
                Assert.That(board.PressAll(minimal).IsDark, Is.True);
            }
        }

        [Test]
        public void MinimalSolutionIsTheSmallestOfAllSolutions()
        {
            var random = new SeededRandom(42);
            for (var trial = 0; trial < 500; trial++)
            {
                var board = LightsBoard.Dark.PressAll(random.NextUInt() & LightsBoard.AllCells);
                var chased = LightsTestKit.AllSolutionsByChasing(board);
                var expected = chased.OrderBy(LightsTestKit.PopCount).ThenBy(s => s).First();

                Assert.That(Gf2Solver.TrySolveMinimal(board, out var minimal), Is.True);
                Assert.That(minimal, Is.EqualTo(expected), board.ToString());
            }
        }

        [Test]
        public void DarkBoardNeedsNoPresses()
        {
            Assert.That(Gf2Solver.TrySolveMinimal(LightsBoard.Dark, out var minimal), Is.True);
            Assert.That(minimal, Is.Zero);
        }

        [Test]
        public void SinglePressBoardIsSolvedByThatPressAlone()
        {
            for (var index = 0; index < LightsBoard.CellCount; index++)
            {
                var board = LightsBoard.Dark.Press(index);
                Assert.That(Gf2Solver.TrySolveMinimal(board, out var minimal), Is.True);
                Assert.That(minimal, Is.EqualTo(1u << index));
            }
        }

        [Test]
        public void UnsolvableBoardIsReported()
        {
            // 左上の 1 マスだけの点灯は解けない。ライト追いでも解がないことを確かめる。
            var board = LightsTestKit.Parse(
                "#....",
                ".....",
                ".....",
                ".....",
                ".....");
            Assert.That(LightsTestKit.AllSolutionsByChasing(board), Is.Empty);
            Assert.That(Gf2Solver.TrySolve(board, out _), Is.False);
            Assert.That(Gf2Solver.TrySolveMinimal(board, out var minimal), Is.False);
            Assert.That(minimal, Is.Zero);
        }
    }
}
