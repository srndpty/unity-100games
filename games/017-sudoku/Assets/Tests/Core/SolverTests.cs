using System;
using System.Collections.Generic;
using NUnit.Framework;
using static Game.Core.Tests.SudokuTestKit;

namespace Game.Core.Tests
{
    public sealed class SolverTests
    {
        [Test]
        public void EasyPuzzleHasOneSolutionFoundWithoutBranching()
        {
            var result = Solver.Count(Easy, 2);
            Assert.That(result.Solutions, Is.EqualTo(1));
            Assert.That(result.First, Is.EqualTo(EasySolution));
            Assert.That(result.Branches, Is.EqualTo(0));
        }

        [Test]
        public void HardPuzzleNeedsBranchingAndStillHasOneSolution()
        {
            var hard = Grid.Parse(HardText);
            var result = Solver.Count(hard, 2);
            Assert.That(result.Solutions, Is.EqualTo(1));
            Assert.That(result.Branches, Is.GreaterThan(0));
            var solution = result.First!;
            Assert.That(IsValidSolution(solution), Is.True);
            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                if (hard[cell] != 0) { Assert.That(solution[cell], Is.EqualTo(hard[cell])); }
            }
        }

        [Test]
        public void CountingStopsAtTheLimit()
        {
            Assert.That(Solver.Count(Grid.Empty, 2).Solutions, Is.EqualTo(2));
            Assert.That(Solver.Count(Grid.Empty, 1).Solutions, Is.EqualTo(1));
            Assert.That(IsValidSolution(Solver.Count(Grid.Empty, 1).First!), Is.True);
        }

        [Test]
        public void DeadlyRectangleHasExactlyTwoSolutions()
        {
            var (grid, _) = WithDeadlyRectangle(EasySolution);
            Assert.That(Solver.Count(grid, 10).Solutions, Is.EqualTo(2));
            Assert.That(Propagator.FindNext(grid), Is.Null);
        }

        [Test]
        public void ContradictoryGridsHaveNoSolution()
        {
            var duplicate = Easy.With(2, 5);
            var noCandidate = Grid.Empty.With(Board.Cell(5, 0), 9);
            for (var column = 1; column < Board.Size; column++) { noCandidate = noCandidate.With(column, column); }

            // 重複はないが、正解（4）と違う数字を置いたので、どう埋めても矛盾する。
            var wrong = Easy.With(2, 1);
            Assert.That(wrong.HasConflict, Is.False);

            foreach (var grid in new[] { duplicate, noCandidate, wrong })
            {
                var result = Solver.Count(grid, 2);
                Assert.That(result.Solutions, Is.EqualTo(0), grid.ToString());
                Assert.That(result.First, Is.Null);
                Assert.That(Solver.Solve(grid), Is.Null);
            }
        }

        [Test]
        public void RandomDigitOrderGivesDifferentCompleteGridsPerSeed()
        {
            var seen = new HashSet<Grid>();
            for (var seed = 0; seed < 20; seed++)
            {
                var grid = Solver.Solve(Grid.Empty, new SeededRandom(seed))!;
                Assert.That(IsValidSolution(grid), Is.True);
                Assert.That(Solver.Solve(Grid.Empty, new SeededRandom(seed)), Is.EqualTo(grid));
                seen.Add(grid);
            }

            Assert.That(seen, Has.Count.EqualTo(20));
        }

        [Test]
        public void InvalidArgumentsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Solver.Count(Easy, 0));
            Assert.Throws<ArgumentNullException>(() => Solver.Count(null!, 2));
            Assert.Throws<ArgumentNullException>(() => Solver.Solve(null!));
        }
    }
}
