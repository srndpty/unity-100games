using NUnit.Framework;
using static Game.Core.Tests.SudokuTestKit;

namespace Game.Core.Tests
{
    public sealed class PropagatorTests
    {
        /// <summary>行 0 の 9 が入りうるのは右端だけ（他の列とブロックに 9 がある）。</summary>
        private static Grid NineOnlyFitsTopRight() => Grid.Empty
            .With(Board.Cell(1, 0), 9)
            .With(Board.Cell(2, 3), 9)
            .With(Board.Cell(3, 6), 9)
            .With(Board.Cell(6, 7), 9);

        [Test]
        public void EasyPuzzleIsSolvedByPropagationAloneAndEveryStepIsTheSolutionDigit()
        {
            var result = Propagator.Run(Easy);
            Assert.That(result.Status, Is.EqualTo(PropagationStatus.Solved));
            Assert.That(result.Grid, Is.EqualTo(EasySolution));
            Assert.That(result.Steps, Has.Count.EqualTo(Board.CellCount - Easy.FilledCount));
            foreach (var step in result.Steps) { Assert.That(step.Digit, Is.EqualTo(EasySolution[step.Cell])); }
        }

        [Test]
        public void EveryStepHoldsForItsReasonOnTheGridBeforeIt()
        {
            var grid = Easy;
            foreach (var step in Propagator.Run(Easy).Steps)
            {
                if (step.Reason == DeductionReason.NakedSingle)
                {
                    Assert.That(step.Unit, Is.EqualTo(-1));
                    Assert.That(NaiveCandidates(grid, step.Cell), Is.EqualTo(Digits.Bit(step.Digit)), step.ToString());
                }
                else
                {
                    var places = 0;
                    for (var i = 0; i < Board.Size; i++)
                    {
                        var cell = Board.UnitCell(step.Unit, i);
                        Assert.That(grid[cell], Is.Not.EqualTo(step.Digit), step.ToString());
                        if (grid[cell] == 0 && Digits.Has(NaiveCandidates(grid, cell), step.Digit)) { places++; }
                    }

                    Assert.That(places, Is.EqualTo(1), step.ToString());
                    Assert.That(Digits.Has(NaiveCandidates(grid, step.Cell), step.Digit), Is.True, step.ToString());
                }

                grid = grid.With(step.Cell, step.Digit);
            }
        }

        [Test]
        public void NakedSingleIsFoundWhenACellHasOneCandidate()
        {
            var grid = Grid.Empty;
            for (var column = 1; column < Board.Size; column++) { grid = grid.With(column, column); }
            Assert.That(Propagator.FindNext(grid), Is.EqualTo(new Deduction(0, 9, DeductionReason.NakedSingle, -1)));
        }

        [Test]
        public void HiddenSingleIsFoundWhenADigitHasOnePlaceInAUnit()
        {
            Assert.That(Propagator.FindNext(NineOnlyFitsTopRight()), Is.EqualTo(new Deduction(8, 9, DeductionReason.HiddenSingle, 0)));
        }

        [Test]
        public void EmptyGridIsStuckWithoutSteps()
        {
            var result = Propagator.Run(Grid.Empty);
            Assert.That(result.Status, Is.EqualTo(PropagationStatus.Stuck));
            Assert.That(result.Steps, Is.Empty);
            Assert.That(Propagator.FindNext(Grid.Empty), Is.Null);
        }

        [Test]
        public void HardPuzzleGetsStuckAndItsStepsAgreeWithTheSolution()
        {
            var hard = Grid.Parse(HardText);
            var result = Propagator.Run(hard);
            var solution = Solver.Count(hard, 2).First!;
            Assert.That(result.Status, Is.EqualTo(PropagationStatus.Stuck));
            foreach (var step in result.Steps) { Assert.That(step.Digit, Is.EqualTo(solution[step.Cell])); }
        }

        [Test]
        public void DuplicateDigitIsAContradiction()
        {
            var grid = Easy.With(2, 5);
            AssertContradiction(grid);
        }

        [Test]
        public void CellWithoutCandidatesIsAContradiction()
        {
            var grid = Grid.Empty.With(Board.Cell(5, 0), 9);
            for (var column = 1; column < Board.Size; column++) { grid = grid.With(column, column); }
            Assert.That(grid.HasConflict, Is.False);
            AssertContradiction(grid);
        }

        [Test]
        public void DigitWithoutAPlaceInAUnitIsAContradiction()
        {
            var grid = NineOnlyFitsTopRight().With(8, 1);
            Assert.That(grid.HasConflict, Is.False);
            AssertContradiction(grid);
        }

        [Test]
        public void ContradictionFoundOnlyAfterPropagatingIsReported()
        {
            // 左上と左下のマスはどちらも 9 しか入らない。片方に 9 を置くと、同じ列のもう片方の候補が尽きる。
            var grid = Grid.Empty;
            for (var column = 1; column < Board.Size; column++)
            {
                grid = grid.With(Board.Cell(0, column), column).With(Board.Cell(8, column), column % 8 + 1);
            }

            Assert.That(grid.HasConflict, Is.False);
            Assert.That(Propagator.FindNext(grid), Is.EqualTo(new Deduction(0, 9, DeductionReason.NakedSingle, -1)));
            Assert.That(Propagator.Run(grid).Status, Is.EqualTo(PropagationStatus.Contradiction));
            Assert.That(Solver.Count(grid, 2).Solutions, Is.EqualTo(0));
        }

        private static void AssertContradiction(Grid grid)
        {
            Assert.That(Propagator.FindNext(grid), Is.Null);
            var result = Propagator.Run(grid);
            Assert.That(result.Status, Is.EqualTo(PropagationStatus.Contradiction));
            Assert.That(result.Grid, Is.EqualTo(grid));
        }

        /// <summary>ピアに入っていない数字の集合（伝播の実装を使わない素朴な計算）。</summary>
        private static int NaiveCandidates(Grid grid, int cell)
        {
            var mask = Digits.All;
            for (var other = 0; other < Board.CellCount; other++)
            {
                if (Board.AreAligned(cell, other) && grid[other] != 0) { mask &= ~Digits.Bit(grid[other]); }
            }

            return mask;
        }
    }
}
