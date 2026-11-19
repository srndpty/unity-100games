using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class SudokuFlowTests
    {
        private const string EasyText = "53..7....6..195....98....6.8...6...34..8.3..17...2...6.6....28....419..5....8..79";
        private const string EasySolutionText = "534678912672195348198342567859761423426853791713924856961537284287419635345286179";

        // Easy の (0,2)。空きで正解は 4、5 は行の与えと重複、1 は重複しないが誤り。
        private const int Cell02 = 2;

        private static Puzzle EasyPuzzle() => new(Grid.Parse(EasyText), Grid.Parse(EasySolutionText));

        /// <summary>決まった問題で始めるフロー。</summary>
        private static SudokuFlow EasyFlow()
        {
            var flow = new SudokuFlow(new SudokuConfig(), 1) { GameFactory = (_, _) => new SudokuGame(EasyPuzzle()) };
            flow.Submit();
            return flow;
        }

        private static void Click(SudokuFlow flow, int cell) => flow.PointerDown(Board.Column(cell) + 0.5, Board.Row(cell) + 0.5);

        private static void SolveWithAutopilot(SudokuFlow flow)
        {
            for (var i = 0; i < Board.CellCount * 2 && flow.Screen == AppScreen.Playing; i++)
            {
                var (cell, digit) = Autopilot.Next(flow.Game!)!.Value;
                Click(flow, cell);
                if (digit == 0) { flow.Erase(); }
                else { flow.InputDigit(digit); }
            }
        }

        [Test]
        public void StartsAtTitleAndSubmitBeginsAGeneratedPuzzle()
        {
            var flow = new SudokuFlow(new SudokuConfig(), 7);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.MatchSeed, Is.EqualTo(7));
            Assert.That(flow.Game!.Puzzle.Givens, Is.EqualTo(PuzzleGenerator.Generate(7, new SudokuConfig(), Difficulty.Normal).Givens));
            Assert.That(flow.Cursor.Cell, Is.EqualTo(CellCursor.Home));
        }

        [Test]
        public void DifficultyIsChosenOnTheTitleAndUsedForTheMatch()
        {
            var flow = new SudokuFlow(new SudokuConfig(), 3);
            Assert.That(flow.Difficulty, Is.EqualTo(Difficulty.Normal));
            flow.Navigate(-1, 0);
            Assert.That(flow.Difficulty, Is.EqualTo(Difficulty.Easy));
            flow.Navigate(-1, 0);
            Assert.That(flow.Difficulty, Is.EqualTo(Difficulty.Easy), "端で止まる");
            flow.Submit();
            Assert.That(flow.MatchDifficulty, Is.EqualTo(Difficulty.Easy));
            Assert.That(flow.Game!.Puzzle.Givens, Is.EqualTo(PuzzleGenerator.Generate(3, new SudokuConfig(), Difficulty.Easy).Givens));

            flow.SelectDifficulty(Difficulty.Hard);
            flow.Navigate(1, 0);
            Assert.That(flow.Difficulty, Is.EqualTo(Difficulty.Easy), "プレイ中は選べない");
            Assert.That(flow.Cursor.Cell, Is.EqualTo(CellCursor.Home + 1), "プレイ中の矢印はマスを動かす");
        }

        [Test]
        public void DifficultyChosenOnTheResultAppliesToTheNextMatchOnly()
        {
            var flow = EasyFlow();
            SolveWithAutopilot(flow);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            flow.SelectDifficulty(Difficulty.Hard);
            Assert.That(flow.MatchDifficulty, Is.EqualTo(Difficulty.Normal), "終わった試合の難しさは変わらない");

            var used = new List<Difficulty>();
            flow.GameFactory = (_, difficulty) =>
            {
                used.Add(difficulty);
                return new SudokuGame(EasyPuzzle());
            };
            flow.Submit();
            Assert.That(used, Is.EqualTo(new List<Difficulty> { Difficulty.Hard }));
            Assert.That(flow.MatchDifficulty, Is.EqualTo(Difficulty.Hard));
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
            {
                flow.TogglePause();
                flow.BackToTitle();
                flow.SelectDifficulty((Difficulty)5);
            });
        }

        [Test]
        public void ClickSelectsACellAndDigitsPlaceThere()
        {
            var flow = EasyFlow();
            Click(flow, Cell02);
            Assert.That(flow.Cursor.Cell, Is.EqualTo(Cell02));
            Assert.That(flow.Game!.ValueAt(Cell02), Is.EqualTo(0), "クリックだけでは置かない");
            flow.InputDigit(4);
            Assert.That(flow.Game.ValueAt(Cell02), Is.EqualTo(4));
            Assert.That(flow.Notice, Is.EqualTo(Notice.Placed));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Placed));
        }

        [Test]
        public void DuplicateIsRejectedWithTheConflictingCell()
        {
            var flow = EasyFlow();
            Click(flow, Cell02);
            flow.InputDigit(5);
            Assert.That(flow.Game!.ValueAt(Cell02), Is.EqualTo(0));
            Assert.That(flow.Notice, Is.EqualTo(Notice.Conflict));
            Assert.That(flow.ConflictCell, Is.EqualTo(0));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Rejected));

            flow.InputDigit(4);
            Assert.That(flow.ConflictCell, Is.EqualTo(-1), "次の操作で消える");
        }

        [Test]
        public void GivenCellsRejectDigitsAndErase()
        {
            var flow = EasyFlow();
            Click(flow, 0);
            flow.InputDigit(1);
            Assert.That(flow.Notice, Is.EqualTo(Notice.Given));
            flow.Erase();
            Assert.That(flow.Notice, Is.EqualTo(Notice.Given));
            Assert.That(flow.Game!.ValueAt(0), Is.EqualTo(5));
        }

        [Test]
        public void NoteModeTogglesCandidatesInsteadOfPlacing()
        {
            var flow = EasyFlow();
            Click(flow, Cell02);
            flow.ToggleNoteMode();
            flow.InputDigit(1);
            flow.InputDigit(4);
            Assert.That(flow.Game!.ValueAt(Cell02), Is.EqualTo(0));
            Assert.That(flow.Game.NotesAt(Cell02), Is.EqualTo(Digits.Bit(1) | Digits.Bit(4)));
            Assert.That(flow.Notice, Is.EqualTo(Notice.Noted));

            flow.ToggleNoteMode();
            flow.InputDigit(4);
            Assert.That(flow.Game.ValueAt(Cell02), Is.EqualTo(4));
            Assert.That(flow.Game.NotesAt(Cell02), Is.EqualTo(0));

            flow.ToggleNoteMode();
            flow.InputDigit(2);
            Assert.That(flow.Notice, Is.EqualTo(Notice.Filled));
        }

        [Test]
        public void EraseRemovesThePlayersDigit()
        {
            var flow = EasyFlow();
            Click(flow, Cell02);
            flow.InputDigit(1);
            flow.Erase();
            Assert.That(flow.Game!.ValueAt(Cell02), Is.EqualTo(0));
            Assert.That(flow.Notice, Is.EqualTo(Notice.Erased));
            flow.TakeEvents();
            flow.Erase();
            Assert.That(flow.Notice, Is.EqualTo(Notice.None));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None), "消すものがなければ何も起きない");
        }

        [Test]
        public void KeyboardMovesTheSelectionAndStopsAtTheEdges()
        {
            var flow = EasyFlow();
            flow.MoveCursor(1, 0);
            Assert.That(flow.Cursor.Cell, Is.EqualTo(41));
            flow.MoveCursor(0, -1);
            Assert.That(flow.Cursor.Cell, Is.EqualTo(32));
            for (var i = 0; i < 12; i++) { flow.MoveCursor(-1, -1); }
            Assert.That(flow.Cursor.Cell, Is.EqualTo(0));
            for (var i = 0; i < 12; i++) { flow.MoveCursor(1, 1); }
            Assert.That(flow.Cursor.Cell, Is.EqualTo(80));
        }

        [TestCase(-0.5, 3.5)]
        [TestCase(9.0, 3.5)]
        [TestCase(3.5, -0.01)]
        [TestCase(3.5, double.NaN)]
        public void ClicksOutsideTheBoardAreIgnored(double column, double row)
        {
            var flow = EasyFlow();
            flow.PointerDown(column, row);
            Assert.That(flow.Cursor.Cell, Is.EqualTo(CellCursor.Home));
        }

        [Test]
        public void HintPointsAtAMistakeAndSelectsIt()
        {
            var flow = EasyFlow();
            Click(flow, Cell02);
            flow.InputDigit(1);
            flow.MoveCursor(1, 1);
            flow.TakeEvents();
            flow.RequestHint();
            Assert.That(flow.Notice, Is.EqualTo(Notice.HintMistake));
            Assert.That(flow.LastHint!.Value.Cell, Is.EqualTo(Cell02));
            Assert.That(flow.Cursor.Cell, Is.EqualTo(Cell02));
            Assert.That(flow.Game!.ValueAt(Cell02), Is.EqualTo(1), "誤りは指すだけで消さない");
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Hinted));
        }

        [Test]
        public void HintFillsADeducedCellAndRecordsTheTick()
        {
            var flow = EasyFlow();
            for (var i = 0; i < 3; i++) { flow.StepTick(); }
            flow.RequestHint();
            var hint = flow.LastHint!.Value;
            Assert.That(flow.Notice, Is.EqualTo(Notice.HintDeduced));
            Assert.That(hint.Deduction, Is.Not.Null);
            Assert.That(flow.Game!.ValueAt(hint.Cell), Is.EqualTo(hint.Digit));
            Assert.That(flow.Cursor.Cell, Is.EqualTo(hint.Cell));
            Assert.That(flow.HintTick, Is.EqualTo(3));
            Assert.That(flow.Game.HintsUsed, Is.EqualTo(1));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Hinted | FlowEvents.Placed));

            flow.MoveCursor(1, 0);
            Assert.That(flow.LastHint, Is.Not.Null, "カーソルの移動ではヒントの表示は消えない");
            flow.InputDigit(9);
            Assert.That(flow.LastHint, Is.Null, "盤の操作で消える");
        }

        [Test]
        public void SolvingShowsTheResultAndStopsTheClock()
        {
            var flow = EasyFlow();
            for (var i = 0; i < 90; i++) { flow.StepTick(); }
            SolveWithAutopilot(flow);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Game!.IsSolved, Is.True);
            Assert.That(flow.TakeEvents() & FlowEvents.Solved, Is.EqualTo(FlowEvents.Solved));
            flow.StepTick();
            flow.Advance(1);
            Assert.That(flow.ElapsedTicks, Is.EqualTo(90));
        }

        [Test]
        public void FullBoardWithMistakesIsAnnouncedButNotSolved()
        {
            // 入れ替えても重複しない 4 マスの長方形だけを空けた盤を、正解と逆の組み合わせで埋める。
            var (puzzle, cells, swapped) = DemoPuzzles.SwappableRectangle(1, new SudokuConfig());
            var flow = new SudokuFlow(new SudokuConfig(), 1) { GameFactory = (_, _) => new SudokuGame(puzzle) };
            flow.Submit();
            for (var i = 0; i < cells.Length; i++)
            {
                Click(flow, cells[i]);
                flow.InputDigit(swapped[i]);
            }

            Assert.That(flow.Game!.IsFull, Is.True);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Notice, Is.EqualTo(Notice.FullButWrong));
            Assert.That(flow.TakeEvents() & FlowEvents.FullButWrong, Is.EqualTo(FlowEvents.FullButWrong));

            flow.RequestHint();
            Assert.That(flow.Notice, Is.EqualTo(Notice.HintMistake));
        }

        [Test]
        public void PauseStopsTheClockAndBlocksBoardInput()
        {
            var flow = EasyFlow();
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.StepTick();
            Click(flow, Cell02);
            flow.InputDigit(4);
            flow.RequestHint();
            flow.ToggleNoteMode();
            Assert.That(flow.ElapsedTicks, Is.EqualTo(0));
            Assert.That(flow.Cursor.Cell, Is.EqualTo(CellCursor.Home));
            Assert.That(flow.Game!.ValueAt(Cell02), Is.EqualTo(0));
            Assert.That(flow.Game.HintsUsed, Is.EqualTo(0));
            Assert.That(flow.NoteMode, Is.False);

            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            flow.StepTick();
            Assert.That(flow.ElapsedTicks, Is.EqualTo(1));
        }

        [Test]
        public void ResultScreenIgnoresBoardInput()
        {
            var flow = EasyFlow();
            SolveWithAutopilot(flow);
            var before = flow.Game!.Current;
            flow.MoveCursor(1, 0);
            flow.InputDigit(1);
            flow.Erase();
            flow.RequestHint();
            Assert.That(flow.Game.Current, Is.EqualTo(before));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
        }

        [Test]
        public void LosingFocusPausesAndBlocksInputUntilItReturns()
        {
            var flow = EasyFlow();
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Submit();
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間は再開しない");

            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void PauseThenFocusLossInTheSameFrameStaysPaused()
        {
            var flow = EasyFlow();
            flow.TogglePause();
            flow.SetFocus(false);
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
        }

        [Test]
        public void RestartUsesANewSeedAndResetsTheMatch()
        {
            var flow = new SudokuFlow(new SudokuConfig(), 5);
            flow.Submit();
            var seeds = new List<int> { flow.MatchSeed };
            flow.ToggleNoteMode();
            flow.StepTick();
            flow.TogglePause();
            flow.Restart();
            seeds.Add(flow.MatchSeed);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.NoteMode, Is.False);
            Assert.That(flow.ElapsedTicks, Is.EqualTo(0));

            SolveWithAutopilot(flow);
            flow.Submit();
            seeds.Add(flow.MatchSeed);
            Assert.That(seeds, Is.EqualTo(new List<int> { 5, 6, 7 }));
            Assert.That(flow.Game!.Puzzle.Givens, Is.EqualTo(PuzzleGenerator.Generate(7, new SudokuConfig(), Difficulty.Normal).Givens));
        }

        [Test]
        public void RestartAndBackToTitleOnlyFromPauseOrResult()
        {
            var flow = EasyFlow();
            flow.Restart();
            flow.BackToTitle();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.MatchSeed, Is.EqualTo(1));

            flow.TogglePause();
            flow.BackToTitle();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
            flow.Restart();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [Test]
        public void AdvanceConvertsSecondsToTicks()
        {
            var flow = EasyFlow();
            for (var i = 0; i < 60; i++) { flow.Advance(1.0 / 60); }
            Assert.That(flow.ElapsedTicks, Is.EqualTo(60));
            for (var i = 0; i < 100; i++) { flow.Advance(0.01); }
            Assert.That(flow.ElapsedTicks, Is.EqualTo(120));
            for (var i = 0; i < 20; i++) { flow.Advance(0.05); }
            Assert.That(flow.ElapsedTicks, Is.EqualTo(180));
        }

        [Test]
        public void AdvanceIsCappedAndRejectsBadValues()
        {
            var flow = EasyFlow();
            flow.Advance(10);
            Assert.That(flow.ElapsedTicks, Is.EqualTo(SudokuFlow.MaxTicksPerFrame));
            flow.Advance(double.NaN);
            flow.Advance(double.PositiveInfinity);
            flow.Advance(-1);
            flow.Advance(0);
            Assert.That(flow.ElapsedTicks, Is.EqualTo(SudokuFlow.MaxTicksPerFrame), "有限でない値と 0 以下は無視する");
            flow.Advance(double.MaxValue);
            Assert.That(flow.ElapsedTicks, Is.EqualTo(SudokuFlow.MaxTicksPerFrame * 2), "Tick に直すと桁あふれする値も 1 フレームの上限まで");
        }

        [Test]
        public void AutopilotSolvesGeneratedPuzzlesThroughTheFlow()
        {
            for (var seed = 0; seed < 10; seed++)
            {
                var flow = new SudokuFlow(new SudokuConfig(), seed);
                flow.Submit();
                SolveWithAutopilot(flow);
                Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result), $"seed {seed}");
                Assert.That(flow.Game!.HintsUsed, Is.EqualTo(0));
            }
        }

        [Test]
        public void DemoRectangleHasTwoSolutionsAndNoDuplicateWhenSwapped()
        {
            var (puzzle, cells, swapped) = DemoPuzzles.SwappableRectangle(1, new SudokuConfig());
            Assert.That(Solver.Count(puzzle.Givens, 3).Solutions, Is.EqualTo(2));
            var wrong = puzzle.Givens;
            for (var i = 0; i < cells.Length; i++)
            {
                Assert.That(puzzle.Givens[cells[i]], Is.EqualTo(0));
                wrong = wrong.With(cells[i], swapped[i]);
            }

            Assert.That(wrong.IsComplete, Is.True);
            Assert.That(wrong.HasConflict, Is.False);
            Assert.That(wrong, Is.Not.EqualTo(puzzle.Solution));
        }

        [Test]
        public void AutopilotErasesMistakesFirst()
        {
            var flow = EasyFlow();
            Click(flow, Cell02);
            flow.InputDigit(1);
            Assert.That(Autopilot.Next(flow.Game!), Is.EqualTo((Cell02, 0)));
        }
    }
}
