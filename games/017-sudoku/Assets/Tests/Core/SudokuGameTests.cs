using System;
using NUnit.Framework;
using static Game.Core.Tests.SudokuTestKit;

namespace Game.Core.Tests
{
    public sealed class SudokuGameTests
    {
        // Easy の (0,2) は空きで正解は 4。行 0 に 5・3・7、ブロック 0 に 6・9・8、列 2 に 8 がある。
        private const int Cell02 = 2;

        [Test]
        public void StartsWithTheGivensAndNoNotes()
        {
            var game = new SudokuGame(EasyPuzzle);
            Assert.That(game.Current, Is.EqualTo(Easy));
            Assert.That(game.IsGiven(0), Is.True);
            Assert.That(game.IsGiven(Cell02), Is.False);
            Assert.That(game.NotesAt(Cell02), Is.EqualTo(0));
            Assert.That(game.IsSolved, Is.False);
            Assert.That(game.HintsUsed, Is.EqualTo(0));
        }

        [TestCase(5, 0, TestName = "行の与えと重複")]
        [TestCase(6, 9, TestName = "ブロックの与えと重複")]
        [TestCase(8, 20, TestName = "列の与えと重複")]
        public void DuplicateOfAGivenIsRejectedWithoutChangingTheGame(int digit, int conflict)
        {
            var game = new SudokuGame(EasyPuzzle);
            Assert.That(game.Place(Cell02, digit), Is.EqualTo(MoveVerdict.Conflict));
            Assert.That(game.LastConflict, Is.EqualTo(conflict));
            Assert.That(game.Current, Is.EqualTo(Easy));
        }

        [Test]
        public void DuplicateOfAPlayerDigitIsRejectedInRowColumnAndBox()
        {
            var game = new SudokuGame(EasyPuzzle);
            Assert.That(game.Place(Cell02, 4), Is.EqualTo(MoveVerdict.Ok));
            var before = game.Current;
            foreach (var other in new[] { 3, Board.Cell(3, 2), Board.Cell(2, 0) })
            {
                Assert.That(game.Place(other, 4), Is.EqualTo(MoveVerdict.Conflict), $"cell {other}");
                Assert.That(game.LastConflict, Is.EqualTo(Cell02));
                Assert.That(game.Current, Is.EqualTo(before));
            }
        }

        [Test]
        public void WrongDigitWithoutDuplicateCanBePlacedAndOverwritten()
        {
            var game = new SudokuGame(EasyPuzzle);
            Assert.That(game.Place(Cell02, 1), Is.EqualTo(MoveVerdict.Ok));
            Assert.That(game.ValueAt(Cell02), Is.EqualTo(1));
            Assert.That(game.Place(Cell02, 1), Is.EqualTo(MoveVerdict.Unchanged));
            Assert.That(game.Place(Cell02, 4), Is.EqualTo(MoveVerdict.Ok));
            Assert.That(game.ValueAt(Cell02), Is.EqualTo(4));
            Assert.That(game.LastConflict, Is.EqualTo(-1));
        }

        [Test]
        public void GivensCannotBeChangedOrErased()
        {
            var game = new SudokuGame(EasyPuzzle);
            Assert.That(game.Place(0, 1), Is.EqualTo(MoveVerdict.Given));
            Assert.That(game.ToggleNote(0, 1), Is.EqualTo(MoveVerdict.Given));
            Assert.That(game.Erase(0), Is.EqualTo(MoveVerdict.Given));
            Assert.That(game.Current, Is.EqualTo(Easy));
        }

        [TestCase(-1, 1, MoveVerdict.OutOfBoard)]
        [TestCase(81, 1, MoveVerdict.OutOfBoard)]
        [TestCase(Cell02, 0, MoveVerdict.InvalidDigit)]
        [TestCase(Cell02, 10, MoveVerdict.InvalidDigit)]
        [TestCase(Cell02, -1, MoveVerdict.InvalidDigit)]
        public void OutOfRangeInputIsRejected(int cell, int digit, MoveVerdict expected)
        {
            var game = new SudokuGame(EasyPuzzle);
            Assert.That(game.Place(cell, digit), Is.EqualTo(expected));
            Assert.That(game.ToggleNote(cell, digit), Is.EqualTo(expected));
            Assert.That(game.Current, Is.EqualTo(Easy));
            if (expected == MoveVerdict.OutOfBoard)
            {
                Assert.That(game.Erase(cell), Is.EqualTo(MoveVerdict.OutOfBoard));
                Assert.Throws<ArgumentOutOfRangeException>(() => game.ValueAt(cell));
            }
        }

        [Test]
        public void NotesToggleOnEmptyCellsOnly()
        {
            var game = new SudokuGame(EasyPuzzle);
            Assert.That(game.ToggleNote(Cell02, 1), Is.EqualTo(MoveVerdict.Ok));
            Assert.That(game.ToggleNote(Cell02, 4), Is.EqualTo(MoveVerdict.Ok));
            Assert.That(game.NotesAt(Cell02), Is.EqualTo(Digits.Bit(1) | Digits.Bit(4)));
            Assert.That(game.ToggleNote(Cell02, 1), Is.EqualTo(MoveVerdict.Ok));
            Assert.That(game.NotesAt(Cell02), Is.EqualTo(Digits.Bit(4)));

            game.Place(Cell02, 4);
            Assert.That(game.NotesAt(Cell02), Is.EqualTo(0));
            Assert.That(game.ToggleNote(Cell02, 2), Is.EqualTo(MoveVerdict.Filled));
        }

        [Test]
        public void PlacingADigitRemovesItFromPeerNotesOnly()
        {
            var game = new SudokuGame(EasyPuzzle);
            var rowPeer = 3;
            var farCell = Board.Cell(4, 4);
            game.ToggleNote(rowPeer, 4);
            game.ToggleNote(rowPeer, 2);
            game.ToggleNote(farCell, 4);
            game.Place(Cell02, 4);
            Assert.That(game.NotesAt(rowPeer), Is.EqualTo(Digits.Bit(2)));
            Assert.That(game.NotesAt(farCell), Is.EqualTo(Digits.Bit(4)));
        }

        [Test]
        public void EraseRemovesTheDigitThenTheNotes()
        {
            var game = new SudokuGame(EasyPuzzle);
            Assert.That(game.Erase(Cell02), Is.EqualTo(MoveVerdict.Empty));
            game.ToggleNote(Cell02, 1);
            game.Place(Cell02, 4);
            Assert.That(game.Erase(Cell02), Is.EqualTo(MoveVerdict.Ok));
            Assert.That(game.ValueAt(Cell02), Is.EqualTo(0));
            Assert.That(game.Erase(Cell02), Is.EqualTo(MoveVerdict.Empty));
            game.ToggleNote(Cell02, 1);
            Assert.That(game.Erase(Cell02), Is.EqualTo(MoveVerdict.Ok));
            Assert.That(game.NotesAt(Cell02), Is.EqualTo(0));
        }

        [Test]
        public void FullBoardWithAWrongDigitIsNotSolved()
        {
            // 入れ替え可能な長方形の 4 マスを、正解と逆の組み合わせで埋める。重複はないが解とは違う。
            var (givens, cells) = WithDeadlyRectangle(EasySolution);
            var game = new SudokuGame(new Puzzle(givens, EasySolution));
            Assert.That(game.Place(cells[0], EasySolution[cells[1]]), Is.EqualTo(MoveVerdict.Ok));
            Assert.That(game.Place(cells[1], EasySolution[cells[0]]), Is.EqualTo(MoveVerdict.Ok));
            Assert.That(game.Place(cells[2], EasySolution[cells[3]]), Is.EqualTo(MoveVerdict.Ok));
            Assert.That(game.Place(cells[3], EasySolution[cells[2]]), Is.EqualTo(MoveVerdict.Ok));
            Assert.That(game.IsFull, Is.True);
            Assert.That(game.Current.HasConflict, Is.False);
            Assert.That(game.IsSolved, Is.False);
        }

        [Test]
        public void FillingTheSolutionSolvesAndLaterMovesAreRejected()
        {
            var game = new SudokuGame(EasyPuzzle);
            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                if (game.IsGiven(cell)) { continue; }
                Assert.That(game.IsSolved, Is.False);
                Assert.That(game.Place(cell, EasySolution[cell]), Is.EqualTo(MoveVerdict.Ok));
            }

            Assert.That(game.IsSolved, Is.True);
            Assert.That(game.Place(Cell02, 1), Is.EqualTo(MoveVerdict.Solved));
            Assert.That(game.ToggleNote(Cell02, 1), Is.EqualTo(MoveVerdict.Solved));
            Assert.That(game.Erase(Cell02), Is.EqualTo(MoveVerdict.Solved));
            Assert.That(game.RequestHint().Kind, Is.EqualTo(HintKind.None));
            Assert.That(game.HintsUsed, Is.EqualTo(0));
            Assert.That(game.Current, Is.EqualTo(EasySolution));
        }

        [Test]
        public void HintPointsAtTheFirstWrongDigitWithoutChangingTheBoard()
        {
            var game = new SudokuGame(EasyPuzzle);
            game.Place(Cell02, 1);
            game.Place(3, 2);
            var before = game.Current;
            var hint = game.RequestHint();
            Assert.That(hint.Kind, Is.EqualTo(HintKind.Mistake));
            Assert.That(hint.Cell, Is.EqualTo(Cell02));
            Assert.That(hint.Digit, Is.EqualTo(1));
            Assert.That(game.Current, Is.EqualTo(before));
            Assert.That(game.HintsUsed, Is.EqualTo(1));
        }

        [Test]
        public void HintFillsTheNextDeductionWithItsReason()
        {
            var game = new SudokuGame(EasyPuzzle);
            var expected = Propagator.FindNext(Easy)!.Value;
            var hint = game.RequestHint();
            Assert.That(hint.Kind, Is.EqualTo(HintKind.Deduced));
            Assert.That(hint.Deduction, Is.EqualTo(expected));
            Assert.That(hint.Cell, Is.EqualTo(expected.Cell));
            Assert.That(game.ValueAt(expected.Cell), Is.EqualTo(EasySolution[expected.Cell]));
        }

        [Test]
        public void HintRevealsASolutionDigitWhenPropagationIsStuck()
        {
            var (givens, cells) = WithDeadlyRectangle(EasySolution);
            var game = new SudokuGame(new Puzzle(givens, EasySolution));
            var hint = game.RequestHint();
            Assert.That(hint.Kind, Is.EqualTo(HintKind.Revealed));
            Assert.That(hint.Cell, Is.EqualTo(cells[0]));
            Assert.That(game.ValueAt(cells[0]), Is.EqualTo(EasySolution[cells[0]]));
        }

        [Test]
        public void RepeatedHintsSolveGeneratedPuzzles()
        {
            for (var seed = 0; seed < 10; seed++)
            {
                var game = new SudokuGame(PuzzleGenerator.Generate(seed, new SudokuConfig(), Difficulty.Hard));
                var empty = Board.CellCount - game.Puzzle.Givens.FilledCount;
                for (var i = 0; i < empty; i++)
                {
                    var hint = game.RequestHint();
                    Assert.That(hint.Kind, Is.EqualTo(HintKind.Deduced).Or.EqualTo(HintKind.Revealed));
                    Assert.That(hint.Digit, Is.EqualTo(game.Puzzle.Solution[hint.Cell]));
                }

                Assert.That(game.IsSolved, Is.True, $"seed {seed}");
                Assert.That(game.HintsUsed, Is.EqualTo(empty));
            }
        }
    }
}
