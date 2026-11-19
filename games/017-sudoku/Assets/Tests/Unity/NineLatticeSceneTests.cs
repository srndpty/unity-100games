using System;
using System.Collections;
using Game.App;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Grid = Game.Core.Grid;
using MouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    public sealed class NineLatticeSceneTests
    {
        private const float WaitLimitSeconds = 10f;
        private const string EasyText = "53..7....6..195....98....6.8...6...34..8.3..17...2...6.6....28....419..5....8..79";
        private const string EasySolutionText = "534678912672195348198342567859761423426853791713924856961537284287419635345286179";

        // Easy の (0,2)。空きで正解は 4、5 は行の与えと重複、1 は重複しないが誤り。
        private const int Cell02 = 2;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private NineLatticeGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("NineLattice");
            yield return null;
            _game = Object.FindAnyObjectByType<NineLatticeGame>();
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            InputSystem.RemoveDevice(_mouse);
            _keyboard.Dispose();
            yield return null;
        }

        private SudokuFlow Flow => _game.Flow;

        private SudokuGame Game => Flow.Game;

        private NineLatticeBoardView View => _game.View;

        private static Grid Easy => Grid.Parse(EasyText);

        private static Grid EasySolution => Grid.Parse(EasySolutionText);

        private string Text(string name) => _game.HudRoot.Q<TextElement>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q(name).style.display.value == DisplayStyle.Flex;

        private Vector2 CellPoint(int cell) => _game.ViewCamera.WorldToScreenPoint(View.CellWorld(cell));

        private void SetFocus(bool focus) => _game.SendMessage("OnApplicationFocus", focus);

        private void SetMouse(Vector2 position, bool pressed = false)
        {
            var state = new MouseState { position = position };
            if (pressed) { state = state.WithButton(MouseButton.Left); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        private IEnumerator Click(int cell)
        {
            var position = CellPoint(cell);
            SetMouse(position);
            yield return null;
            SetMouse(position, true);
            yield return null;
            SetMouse(position);
            yield return null;
        }

        private IEnumerator Begin(Puzzle puzzle = null)
        {
            puzzle ??= new Puzzle(Easy, EasySolution);
            Flow.GameFactory = (_, _) => new SudokuGame(puzzle);
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return null;
        }

        private static Key DigitKey(int digit) => Key.Digit1 + (digit - 1);

        // バッチ実行では UI イベントが配送されないので、実際の座標でボタンに当たることを確かめてから送る。
        private IEnumerator ClickButton(string name)
        {
            yield return null;
            yield return null;
            SendButtonClick(name);
            yield return null;
            yield return null;
        }

        private void SendButtonClick(string name)
        {
            var button = _game.HudRoot.Q<Button>(name);
            var panelPoint = button.worldBound.center;
            var panel = _game.HudRoot.panel;
            Assert.That(panel.Pick(panelPoint), Is.SameAs(button), name);

            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = panelPoint, button = 0 }))
            {
                down.target = button;
                button.SendEvent(down);
            }

            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = panelPoint, button = 0 }))
            {
                up.target = button;
                button.SendEvent(up);
            }
        }

        private void AssertViewMatchesGame()
        {
            _game.Render();
            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                Assert.That(View.DigitAt(cell), Is.EqualTo(Game.ValueAt(cell)), $"cell {cell}");
                Assert.That(View.NotesAt(cell), Is.EqualTo(Game.NotesAt(cell)), $"cell {cell}");
                if (Game.ValueAt(cell) != 0)
                {
                    Assert.That(View.DigitColorAt(cell), Is.EqualTo(Game.IsGiven(cell) ? NineLatticeBoardView.GivenInk : NineLatticeBoardView.PlayerInk), $"cell {cell}");
                }
            }
        }

        [Test]
        public void SettingsAssetIsValid()
        {
            Assert.DoesNotThrow(_game.Settings.Validate);
        }

        [UnityTest]
        public IEnumerator Title_ShowsNameRulesAndAnExampleBoard()
        {
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("NINE LATTICE"));
            Assert.That(Text("message"), Does.Contain("1〜9").And.Contain("1 通り"));
            Assert.That(IsShown("overlay"), Is.True);
            Assert.That(IsShown("stats"), Is.False);
            Assert.That(IsShown("quit"), Is.True);
            var shown = 0;
            for (var cell = 0; cell < Board.CellCount; cell++) { if (View.DigitAt(cell) != 0) { shown++; } }
            Assert.That(shown, Is.GreaterThanOrEqualTo(SudokuConfig.MinTargetGivens));
            Assert.That(View.IsSelectionVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator Title_KeysAndButtonsChooseTheDifficulty()
        {
            yield return null;
            Assert.That(IsShown("options"), Is.True);
            Assert.That(IsDifficultySelected("difficulty-normal"), Is.True, "既定はふつう");

            yield return _keyboard.Tap(Key.LeftArrow);
            Assert.That(Flow.Difficulty, Is.EqualTo(Difficulty.Easy));
            yield return null;
            Assert.That(IsDifficultySelected("difficulty-easy"), Is.True);
            Assert.That(IsDifficultySelected("difficulty-normal"), Is.False);

            yield return ClickButton("difficulty-hard");
            Assert.That(Flow.Difficulty, Is.EqualTo(Difficulty.Hard));

            // 選んだ難しさで生成した問題が始まる。
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.MatchDifficulty, Is.EqualTo(Difficulty.Hard));
            Assert.That(Game.Puzzle.Givens, Is.EqualTo(PuzzleGenerator.Generate(Flow.MatchSeed, _game.Settings.Rules, Difficulty.Hard).Givens));
            yield return null;
            Assert.That(Text("difficulty"), Is.EqualTo("難しさ：むずかしい"));
            Assert.That(IsShown("options"), Is.False);
        }

        private bool IsDifficultySelected(string name) => _game.HudRoot.Q<Button>(name).ClassListContains("option--selected");

        [UnityTest]
        public IEnumerator Enter_StartsAndShowsTheGivensAndTheSelection()
        {
            yield return Begin();
            AssertViewMatchesGame();
            Assert.That(IsShown("stats"), Is.True);
            Assert.That(IsShown("overlay"), Is.False);
            Assert.That(Text("time"), Is.EqualTo("時間  00:00"));
            Assert.That(Text("mode"), Is.EqualTo("入力：数字"));
            Assert.That(View.IsSelectionVisible, Is.True);
            Assert.That(View.SelectionPosition, Is.EqualTo(View.CellWorld(CellCursor.Home)));
            Assert.That(View.TintAt(CellCursor.Home), Is.EqualTo(CellTint.Selected));
            Assert.That(View.TintAt(CellCursor.Home + 1), Is.EqualTo(CellTint.Peer));
        }

        [UnityTest]
        public IEnumerator Mouse_ClicksSelectTheCellUnderThePointer()
        {
            yield return Begin();

            // ブロックの境目の両側と盤の四隅。
            foreach (var cell in new[] { 0, 2, 3, 8, 26, 27, 40, 72, 80 })
            {
                yield return Click(cell);
                Assert.That(Flow.Cursor.Cell, Is.EqualTo(cell));
            }

            Assert.That(Game.Current, Is.EqualTo(Easy), "クリックだけでは数字を置かない");
        }

        [UnityTest]
        public IEnumerator Keys_PlaceADigitInTheChosenCell()
        {
            yield return Begin();
            yield return Click(Cell02);
            yield return _keyboard.Tap(Key.Digit4);
            Assert.That(Game.ValueAt(Cell02), Is.EqualTo(4));
            AssertViewMatchesGame();

            // 矢印で右へ動かし、テンキーで置く。
            yield return _keyboard.Tap(Key.RightArrow);
            Assert.That(Flow.Cursor.Cell, Is.EqualTo(3));
            yield return _keyboard.Tap(Key.Numpad6);
            Assert.That(Game.ValueAt(3), Is.EqualTo(6));
            AssertViewMatchesGame();
            Assert.That(View.TintAt(Cell02), Is.EqualTo(CellTint.Peer));
        }

        [UnityTest]
        public IEnumerator Conflict_IsRejectedAndShowsWhere()
        {
            yield return Begin();
            yield return Click(Cell02);
            yield return _keyboard.Tap(Key.Digit5);
            Assert.That(Game.ValueAt(Cell02), Is.EqualTo(0));
            Assert.That(Text("status"), Is.EqualTo("同じ行に 5 があります。"));
            Assert.That(View.TintAt(0), Is.EqualTo(CellTint.Conflict));
            Assert.That(View.DigitAt(Cell02), Is.EqualTo(0));

            yield return _keyboard.Tap(Key.Digit4);
            Assert.That(View.TintAt(0), Is.Not.EqualTo(CellTint.Conflict), "次の操作で消える");
        }

        [UnityTest]
        public IEnumerator Space_SwitchesToNotesAndDeleteErases()
        {
            yield return Begin();
            yield return Click(Cell02);
            yield return _keyboard.Tap(Key.Space);
            Assert.That(Text("mode"), Is.EqualTo("入力：候補（メモ）"));
            Assert.That(_game.HudRoot.Q<Button>("pad-note").ClassListContains("pad--on"), Is.True);
            yield return _keyboard.Tap(Key.Digit1);
            yield return _keyboard.Tap(Key.Digit4);
            Assert.That(Game.NotesAt(Cell02), Is.EqualTo(Digits.Bit(1) | Digits.Bit(4)));
            AssertViewMatchesGame();

            yield return _keyboard.Tap(Key.Space);
            yield return _keyboard.Tap(Key.Digit1);
            Assert.That(Game.ValueAt(Cell02), Is.EqualTo(1));
            Assert.That(View.NotesAt(Cell02), Is.EqualTo(0));

            yield return _keyboard.Tap(Key.Delete);
            Assert.That(Game.ValueAt(Cell02), Is.EqualTo(0));
            AssertViewMatchesGame();
        }

        [UnityTest]
        public IEnumerator HintKey_FillsADeducedCellAndGlowsForAWhile()
        {
            yield return Begin();
            yield return _keyboard.Tap(Key.H);
            var hint = Flow.LastHint!.Value;
            Assert.That(Flow.Notice, Is.EqualTo(Notice.HintDeduced));
            Assert.That(Text("status"), Does.EndWith("だけです。"));
            Assert.That(Text("hints"), Is.EqualTo("ヒント  1 回"));
            Assert.That(View.DigitAt(hint.Cell), Is.EqualTo(EasySolution[hint.Cell]));
            Assert.That(View.TintAt(hint.Cell), Is.EqualTo(CellTint.Hint));

            for (var i = 0; i < _game.Settings.HintGlowTicks; i++) { Flow.StepTick(); }
            _game.Render();
            Assert.That(View.TintAt(hint.Cell), Is.EqualTo(CellTint.Selected), "光る時間が過ぎたら選択の色に戻る");
        }

        [UnityTest]
        public IEnumerator HintKey_PointsAtAWrongDigit()
        {
            yield return Begin();
            yield return Click(Cell02);
            yield return _keyboard.Tap(Key.Digit1);
            yield return Click(80);
            yield return _keyboard.Tap(Key.H);
            Assert.That(Flow.Cursor.Cell, Is.EqualTo(Cell02));
            Assert.That(Text("status"), Does.StartWith("ここの 1 は正解と違います。"));
            Assert.That(View.TintAt(Cell02), Is.EqualTo(CellTint.Hint));
            Assert.That(Game.ValueAt(Cell02), Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PadButtons_PlaceNoteEraseAndHint()
        {
            yield return Begin();
            yield return Click(Cell02);
            yield return ClickButton("pad-4");
            Assert.That(Game.ValueAt(Cell02), Is.EqualTo(4), "押すと置ける");
            yield return ClickButton("pad-erase");
            Assert.That(Game.ValueAt(Cell02), Is.EqualTo(0));
            yield return ClickButton("pad-note");
            Assert.That(Flow.NoteMode, Is.True);
            yield return ClickButton("pad-7");
            Assert.That(Game.NotesAt(Cell02), Is.EqualTo(Digits.Bit(7)));
            yield return ClickButton("pad-hint");
            Assert.That(Game.HintsUsed, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PadButton_OnAGivenCellChangesNothing()
        {
            yield return Begin();
            yield return Click(0);
            yield return ClickButton("pad-1");
            Assert.That(Game.ValueAt(0), Is.EqualTo(5), "与えは変わらない");
            Assert.That(Text("status"), Is.EqualTo("初めから入っている数字は変えられません。"));

            // 同じ手順で、空きマスなら置ける。
            yield return Click(Cell02);
            yield return ClickButton("pad-4");
            Assert.That(Game.ValueAt(Cell02), Is.EqualTo(4));
        }

        [UnityTest]
        public IEnumerator FullBoardWithAMistake_IsAnnouncedAndPlayContinues()
        {
            var (puzzle, cells, swapped) = DemoPuzzles.SwappableRectangle(1, new SudokuConfig());
            yield return Begin(puzzle);
            for (var i = 0; i < cells.Length; i++)
            {
                yield return Click(cells[i]);
                yield return _keyboard.Tap(DigitKey(swapped[i]));
            }

            Assert.That(Game.IsFull, Is.True);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Text("status"), Does.StartWith("全部埋まりましたが、どこかが違います。"));

            yield return _keyboard.Tap(Key.H);
            Assert.That(Flow.Notice, Is.EqualTo(Notice.HintMistake));
            Assert.That(Flow.Cursor.Cell, Is.EqualTo(cells[0]));
        }

        [UnityTest]
        public IEnumerator Result_ShowsTimeAndHintsAndEnterStartsANewPuzzle()
        {
            yield return Begin();
            for (var i = 0; i < 125 * SudokuFlow.TickRate; i++) { Flow.StepTick(); }

            // 最後の 1 マスだけを実際のキーで埋める。
            var last = -1;
            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                if (Game.ValueAt(cell) != 0) { continue; }
                if (last >= 0)
                {
                    Flow.PointerDown(Board.Column(last) + .5, Board.Row(last) + .5);
                    Flow.InputDigit(EasySolution[last]);
                }

                last = cell;
            }

            yield return Click(last);
            yield return _keyboard.Tap(DigitKey(EasySolution[last]));
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Result));
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("完成！"));
            Assert.That(Text("message"), Does.Contain("難しさ  ふつう").And.Contain("時間  02:05").And.Contain("ヒント  0 回"));
            Assert.That(IsShown("options"), Is.True, "結果画面で次の難しさを選べる");
            Assert.That(View.IsSelectionVisible, Is.False);

            var seed = Flow.MatchSeed;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Flow.MatchSeed, Is.EqualTo(seed + 1));
            Assert.That(Text("time"), Is.EqualTo("時間  00:00"));
        }

        [UnityTest]
        public IEnumerator ClickAfterFocusReturnIsIgnored()
        {
            yield return Begin();
            SetFocus(false);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスを失うと一時停止する");

            // 表示したばかりのボタンが配置されるまで待つ。
            yield return null;
            yield return null;
            yield return null;

            // ウィンドウを選び直すクリックが再開ボタンに当たっても再開しない。
            SetFocus(true);
            SendButtonClick("submit");
            yield return null;
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused));

            // 同じ手順で、復帰から時間がたったクリックなら再開する。
            yield return ClickButton("submit");
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [UnityTest]
        public IEnumerator EscapePausesStopsTimeAndBlocksDigits()
        {
            yield return Begin();
            yield return Click(Cell02);
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused));
            var ticks = Flow.ElapsedTicks;
            yield return _keyboard.Tap(Key.Digit4);
            yield return _keyboard.Tap(Key.H);
            Assert.That(Game.ValueAt(Cell02), Is.EqualTo(0), "一時停止中は置けない");
            Assert.That(Game.HintsUsed, Is.EqualTo(0));
            Assert.That(Flow.ElapsedTicks, Is.EqualTo(ticks));
            Assert.That(IsShown("restart"), Is.True);
            Assert.That(IsShown("title"), Is.True);

            yield return _keyboard.Tap(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [UnityTest]
        public IEnumerator SubmitButton_StartsTheGame()
        {
            yield return ClickButton("submit");
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [UnityTest]
        public IEnumerator QuitButton_QuitsOnlyFromTheTitle()
        {
            var quits = 0;
            _game.QuitHandler = () => quits++;
            yield return ClickButton("quit");
            Assert.That(quits, Is.EqualTo(1));

            yield return Begin();
            yield return null;
            Assert.That(IsShown("quit"), Is.False);
        }
    }
}
