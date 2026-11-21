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
using MouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    public sealed class FireflyHushSceneTests
    {
        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private FireflyHushGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("FireflyHush");
            yield return null;
            _game = Object.FindAnyObjectByType<FireflyHushGame>();
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

        private LightsOutGame Game => _game.Flow.Game;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q<Button>(name).style.display.value == DisplayStyle.Flex;

        private Vector2 CellPoint(int cell) => _game.ViewCamera.WorldToScreenPoint(_game.View.CellWorld(cell));

        private void SetMouse(Vector2 position, MouseButton? pressed = null)
        {
            var state = new MouseState { position = position };
            if (pressed.HasValue) { state = state.WithButton(pressed.Value); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        private IEnumerator Click(Vector2 position)
        {
            SetMouse(position);
            yield return null;
            SetMouse(position, MouseButton.Left);
            yield return null;
            SetMouse(position);
            yield return null;
        }

        private IEnumerator StartMatch()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        // 灯の演出は実時間で進むので、フレーム数ではなく実時間の上限で待つ。
        private IEnumerator WaitForGlowsToSettle()
        {
            var limit = Time.realtimeSinceStartup + 3f;
            while (!_game.View.IsSettledTo(Game.Board) && Time.realtimeSinceStartup < limit) { yield return null; }
            Assert.That(_game.View.IsSettledTo(Game.Board), Is.True);
        }

        // バッチ実行ではUIイベントが配送されないので、実際の座標でボタンに当たることを確かめてから送る。
        private IEnumerator ClickButton(string name)
        {
            yield return null;
            yield return null;
            var button = _game.HudRoot.Q<Button>(name);
            Assert.That(IsShown(name), Is.True, name + " が表示されていない");
            var panelPoint = button.worldBound.center;
            var panel = _game.HudRoot.panel;
            var rootSize = panel.visualTree.worldBound.size;
            var point = new Vector2(panelPoint.x / rootSize.x * Screen.width, Screen.height - (panelPoint.y / rootSize.y * Screen.height));
            Assert.That(panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, new Vector2(point.x, Screen.height - point.y))), Is.SameAs(button));

            SetMouse(point);
            yield return null;
            SetMouse(point, MouseButton.Left);
            yield return null;
            SendButtonClick(name);
            SetMouse(point);
            yield return null;
            yield return null;
        }

        // ボタンへポインタの押下と離しを送る（ClickEvent では Button.clicked が呼ばれない）。
        private void SendButtonClick(string name)
        {
            var button = _game.HudRoot.Q<Button>(name);
            var panelPoint = button.worldBound.center;
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

        [UnityTest]
        public IEnumerator Title_ShowsDecorativeBoardAndInstructionsWithoutAGame()
        {
            yield return null;
            Assert.That(Game, Is.Null);
            Assert.That(Text("headline"), Is.EqualTo("FIREFLY HUSH"));
            StringAssert.Contains("クリック", Text("message"));
            StringAssert.Contains("矢印キー", Text("message"));
            Assert.That(_game.View.IsCursorVisible, Is.False);
            var lit = 0;
            for (var cell = 0; cell < LightsBoard.CellCount; cell++) { lit += _game.View.GlowLevel(cell) > 0 ? 1 : 0; }
            Assert.That(lit, Is.GreaterThan(0), "タイトルでも盤の灯が見える");
        }

        [UnityTest]
        public IEnumerator Start_ShowsTheGeneratedBoard()
        {
            yield return StartMatch();
            yield return WaitForGlowsToSettle();
            Assert.That(Text("moves"), Is.EqualTo("手数  0"));
            Assert.That(_game.View.IsCursorVisible, Is.True);
            Assert.That(_game.View.CursorCell, Is.EqualTo(BoardCursor.Center));
        }

        [UnityTest]
        public IEnumerator ArrowKeysAndSpace_PressTheCursorCell()
        {
            yield return StartMatch();
            var before = Game.Board;
            yield return _keyboard.Tap(Key.LeftArrow);
            yield return _keyboard.Tap(Key.UpArrow);
            Assert.That(_game.View.CursorCell, Is.EqualTo(6));
            yield return _keyboard.Tap(Key.Space);

            Assert.That(Game.Board, Is.EqualTo(before.Press(6)));
            Assert.That(Game.Moves, Is.EqualTo(1));
            yield return null;
            Assert.That(Text("moves"), Is.EqualTo("手数  1"));
            yield return WaitForGlowsToSettle();
        }

        [UnityTest]
        public IEnumerator Click_PressesTheClickedCell()
        {
            yield return StartMatch();
            var before = Game.Board;
            yield return Click(CellPoint(24));
            Assert.That(Game.Board, Is.EqualTo(before.Press(24)));
            Assert.That(Game.Moves, Is.EqualTo(1));
            Assert.That(_game.View.CursorCell, Is.EqualTo(24));
        }

        [UnityTest]
        public IEnumerator RepeatedClicks_CountOneMoveEach()
        {
            yield return StartMatch();
            var expected = Game.Board;
            int[] cells = { 0, 0, 12, 12, 12, 24 };
            foreach (var cell in cells)
            {
                yield return Click(CellPoint(cell));
                expected = expected.Press(cell);
            }

            Assert.That(Game.Moves, Is.EqualTo(cells.Length));
            Assert.That(Game.Board, Is.EqualTo(expected));
        }

        [UnityTest]
        public IEnumerator Click_OnGapOrOutsideBoard_IsIgnored()
        {
            yield return StartMatch();
            var before = Game.Board;
            var gap = (Vector2)_game.ViewCamera.WorldToScreenPoint((_game.View.CellWorld(0) + _game.View.CellWorld(1)) / 2);
            yield return Click(gap);
            yield return Click(new Vector2(2, Screen.height / 2f));
            Assert.That(Game.Board, Is.EqualTo(before));
            Assert.That(Game.Moves, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Hover_MovesTheCursorWithoutPressing()
        {
            yield return StartMatch();
            SetMouse(CellPoint(3));
            yield return null;
            yield return null;
            Assert.That(_game.View.CursorCell, Is.EqualTo(3));
            Assert.That(Game.Moves, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Click_OnTitle_DoesNothing()
        {
            yield return Click(CellPoint(12));
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(Game, Is.Null);
        }

        [UnityTest]
        public IEnumerator ClickingTheSolution_ClearsAndShowsResult()
        {
            yield return StartMatch();
            for (var guard = 0; guard < LightsBoard.CellCount && _game.Flow.Screen == AppScreen.Playing; guard++)
            {
                yield return Click(CellPoint(Autopilot.NextPress(Game)));
            }

            Assert.That(Game.Status, Is.EqualTo(GameStatus.Cleared));
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("夜が静まった"));
            Assert.That(Text("moves"), Is.EqualTo($"手数  {Game.OptimalMoves}（最短 {Game.OptimalMoves}）"));
            Assert.That(_game.View.IsCursorVisible, Is.False);
            yield return WaitForGlowsToSettle();

            var cleared = Game;
            yield return Click(CellPoint(0));
            Assert.That(cleared.Board.IsDark, Is.True, "終了後のクリックは無視する");
            Assert.That(cleared.Moves, Is.EqualTo(cleared.OptimalMoves));
        }

        [UnityTest]
        public IEnumerator PauseAndGiveUp_MarksTheAnswerCells()
        {
            yield return StartMatch();
            yield return Click(CellPoint(0));
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return _keyboard.Tap(Key.G);

            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(Game.Status, Is.EqualTo(GameStatus.GaveUp));
            yield return null;
            var answer = _game.Flow.Answer;
            Assert.That(Game.Board.PressAll(answer).IsDark, Is.True);
            for (var cell = 0; cell < LightsBoard.CellCount; cell++)
            {
                Assert.That(_game.View.IsMarked(cell), Is.EqualTo((answer & (1u << cell)) != 0), $"マス {cell}");
            }

            Assert.That(Text("headline"), Is.EqualTo("解答"));

            var seed = Game.Seed;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Game.Seed, Is.Not.EqualTo(seed), "次の問題は新しいシード");
            yield return null;
            for (var cell = 0; cell < LightsBoard.CellCount; cell++) { Assert.That(_game.View.IsMarked(cell), Is.False); }
        }

        [UnityTest]
        public IEnumerator PauseAndRetry_RestoresThePuzzle()
        {
            yield return StartMatch();
            yield return Click(CellPoint(7));
            yield return Click(CellPoint(8));
            yield return _keyboard.Tap(Key.Escape);
            yield return Click(CellPoint(9));
            Assert.That(Game.Moves, Is.EqualTo(2), "一時停止中は押せない");
            yield return _keyboard.Tap(Key.R);

            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Game.Board, Is.EqualTo(Game.Puzzle.Board));
            Assert.That(Game.Moves, Is.Zero);
            yield return null;
            Assert.That(Text("moves"), Is.EqualTo("手数  0"));
        }

        [UnityTest]
        public IEnumerator Buttons_DriveTheWholeLoop()
        {
            yield return ClickButton("submit");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Game.Moves, Is.Zero, "ボタンのクリックで盤を押さない");
            yield return ClickButton("pause");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return ClickButton("giveUp");
            Assert.That(Game.Status, Is.EqualTo(GameStatus.GaveUp));
            var seed = Game.Seed;
            yield return ClickButton("submit");
            Assert.That(Game.Seed, Is.Not.EqualTo(seed));
            yield return ClickButton("pause");
            yield return ClickButton("newPuzzle");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return ClickButton("pause");
            yield return ClickButton("title");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(Game, Is.Null);
        }

        [UnityTest]
        public IEnumerator QuitButton_RequestsQuitOnce()
        {
            var requests = 0;
            _game.QuitHandler = () => requests++;
            yield return ClickButton("quit");
            Assert.That(requests, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Buttons_MatchScreen()
        {
            yield return null;
            yield return null;
            AssertButtons("submit", "quit");
            yield return StartMatch();
            yield return null;
            AssertButtons("pause");
            yield return _keyboard.Tap(Key.Escape);
            yield return null;
            AssertButtons("submit", "retry", "newPuzzle", "giveUp", "title");
            yield return _keyboard.Tap(Key.G);
            yield return null;
            AssertButtons("submit", "title");
        }

        private void AssertButtons(params string[] shown)
        {
            foreach (var name in new[] { "submit", "pause", "retry", "newPuzzle", "giveUp", "title", "quit" })
            {
                Assert.That(IsShown(name), Is.EqualTo(Array.IndexOf(shown, name) >= 0), name);
            }
        }

        [UnityTest]
        public IEnumerator Panel_DoesNotCoverBoard()
        {
            yield return null;
            yield return null;
            var right = _game.ViewCamera.WorldToScreenPoint(_game.View.CellWorld(4) + (Vector3.right * _game.View.Layout.HitSize / 2));
            var panel = _game.HudRoot.panel;
            var boardRight = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(right.x, Screen.height - right.y)).x;
            Assert.That(_game.HudRoot.Q<VisualElement>("panel").worldBound.xMin, Is.GreaterThan(boardRight));
        }

        [UnityTest]
        public IEnumerator FocusLoss_PausesAndDiscardsPendingClick()
        {
            yield return StartMatch();
            SetMouse(CellPoint(6));
            yield return null;
            SetMouse(CellPoint(6), MouseButton.Left);
            InputSystem.Update();
            _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            Assert.That(Game.Moves, Is.Zero);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _game.SendMessage("OnApplicationFocus", true);
            SetMouse(CellPoint(6));
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
        }

        // プレイ中はフォーカスを失うと一時停止するので、復帰のクリックが問題になるのはボタン。タイトルの終了ボタンで確かめる。
        [UnityTest]
        public IEnumerator ClickThatRestoresFocus_DoesNotQuit_ButNextClickDoes()
        {
            var requests = 0;
            _game.QuitHandler = () => requests++;
            yield return null;
            _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            SendButtonClick("quit");
            yield return null;
            yield return null;
            Assert.That(requests, Is.Zero, "フォーカスを戻すクリックでは終了しない");

            yield return ClickButton("quit");
            Assert.That(requests, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ClickBeforeFocusIsRestored_DoesNotQuit()
        {
            // クリックが先に届き、その後で復帰の通知が来る順序。
            var requests = 0;
            _game.QuitHandler = () => requests++;
            yield return null;
            _game.SendMessage("OnApplicationFocus", false);
            SendButtonClick("quit");
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            yield return null;
            Assert.That(requests, Is.Zero);
        }

        [UnityTest]
        public IEnumerator KeyAfterFocusRestored_IsAccepted()
        {
            yield return StartMatch();
            _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing), "キーボードでは再開できる");
        }

        [Test]
        public void Settings_RejectInvalidValues()
        {
            AssertRejected(s => s.Volume = float.NaN);
            AssertRejected(s => s.Volume = 1.5f);
            AssertRejected(s => s.FadeSeconds = -.1f);
            AssertRejected(s => s.FadeSeconds = float.PositiveInfinity);
            AssertRejected(s => s.Puzzle.ScramblePresses = 0);
            AssertRejected(s => s.Puzzle.ScramblePresses = 25);
        }

        private static void AssertRejected(Action<FireflyHushSettings> change)
        {
            var settings = ScriptableObject.CreateInstance<FireflyHushSettings>();
            Assert.DoesNotThrow(settings.Validate);
            change(settings);
            Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
            Object.DestroyImmediate(settings);
        }
    }
}
