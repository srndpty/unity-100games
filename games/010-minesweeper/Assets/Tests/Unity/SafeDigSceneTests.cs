using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class SafeDigSceneTests
    {
        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private SafeDigGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("SafeDig");
            yield return null;
            _game = Object.FindAnyObjectByType<SafeDigGame>();
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

        private MinesweeperGame Game => _game.Flow.Game;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q<Button>(name).style.display.value == DisplayStyle.Flex;

        private Vector2 CellPoint(int x, int y) => _game.ViewCamera.WorldToScreenPoint(_game.View.CellWorld(x, y));

        private void SetMouse(Vector2 position, MouseButton? pressed = null)
        {
            var state = new MouseState { position = position };
            if (pressed.HasValue) { state = state.WithButton(pressed.Value); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        private IEnumerator Click(Vector2 position, MouseButton button)
        {
            SetMouse(position);
            yield return null;
            SetMouse(position, button);
            yield return null;
            SetMouse(position);
            yield return null;
        }

        private IEnumerator StartMatch()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        private void PlayToWin()
        {
            for (var i = 0; i < 300 && _game.Flow.Screen == AppScreen.Playing; i++)
            {
                Assert.That(AnswerKeyPilot.TryNextSafe(Game, out var x, out var y), Is.True);
                _game.Flow.Reveal(x, y);
            }

            Assert.That(Game.Phase, Is.EqualTo(GamePhase.Won));
        }

        private void PlayToLose()
        {
            AnswerKeyPilot.TryNextSafe(Game, out var x, out var y);
            _game.Flow.Reveal(x, y);
            Assert.That(AnswerKeyPilot.TryNextMine(Game, out x, out y), Is.True);
            _game.Flow.Reveal(x, y);
            Assert.That(Game.Phase, Is.EqualTo(GamePhase.Lost));
        }

        [UnityTest]
        public IEnumerator Scene_ShowsBoardAndInstructionsBeforePlay()
        {
            var view = _game.View;
            Assert.That(view.Width * view.Height, Is.EqualTo(_game.Settings.Board.CellCount));
            for (var y = 0; y < view.Height; y++)
            {
                for (var x = 0; x < view.Width; x++) { Assert.That(view.CellSprite(x, y), Is.EqualTo(view.FaceSprite(CellFace.Hidden))); }
            }

            Assert.That(Text("headline"), Is.EqualTo("SAFE DIG"));
            StringAssert.Contains("左クリック", Text("message"));
            StringAssert.Contains("右クリック", Text("message"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Faces_HaveDistinctSprites()
        {
            var sprites = new HashSet<Sprite>();
            foreach (CellFace face in Enum.GetValues(typeof(CellFace)))
            {
                var sprite = _game.View.FaceSprite(face);
                Assert.That(sprite, Is.Not.Null, face.ToString());
                Assert.That(sprites.Add(sprite), Is.True, face.ToString());
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Panel_DoesNotCoverBoard()
        {
            yield return null;
            yield return null;
            var right = _game.ViewCamera.WorldToScreenPoint(_game.View.CellWorld(_game.View.Width - 1, 0) + (Vector3.right * .5f));
            var panel = _game.HudRoot.panel;
            var boardRight = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(right.x, Screen.height - right.y)).x;
            Assert.That(_game.HudRoot.Q<VisualElement>("panel").worldBound.xMin, Is.GreaterThan(boardRight));
        }

        [UnityTest]
        public IEnumerator LeftClick_RevealsClickedCell()
        {
            yield return StartMatch();
            yield return Click(CellPoint(3, 12), MouseButton.Left);
            Assert.That(Game.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(Game.Face(3, 12), Is.EqualTo(CellFace.Open0));
            Assert.That(_game.View.CellSprite(3, 12), Is.EqualTo(_game.View.FaceSprite(CellFace.Open0)));
        }

        [UnityTest]
        public IEnumerator RightClick_TogglesFlagAndUpdatesCount()
        {
            yield return StartMatch();
            yield return Click(CellPoint(5, 5), MouseButton.Right);
            Assert.That(Game.Face(5, 5), Is.EqualTo(CellFace.Flagged));
            Assert.That(Text("mines"), Does.Contain((_game.Settings.Board.MineCount - 1).ToString()));
            yield return Click(CellPoint(5, 5), MouseButton.Right);
            Assert.That(Game.Face(5, 5), Is.EqualTo(CellFace.Hidden));
            Assert.That(Game.Phase, Is.EqualTo(GamePhase.Ready), "旗の操作では爆弾を配置しない");
        }

        [UnityTest]
        public IEnumerator Click_OutsideBoard_IsIgnored()
        {
            yield return StartMatch();
            yield return Click(new Vector2(Screen.width - 2, Screen.height / 2f), MouseButton.Left);
            Assert.That(Game.Phase, Is.EqualTo(GamePhase.Ready));
            Assert.That(Game.FlagCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Click_OnTitle_DoesNothing()
        {
            yield return Click(CellPoint(3, 3), MouseButton.Left);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(Game, Is.Null);
        }

        [UnityTest]
        public IEnumerator HeldButton_MovedOverAnotherCell_DoesNotRevealIt()
        {
            yield return StartMatch();
            SetMouse(CellPoint(0, 0));
            yield return null;
            SetMouse(CellPoint(0, 0), MouseButton.Right);
            yield return null;
            SetMouse(CellPoint(10, 10), MouseButton.Right);
            yield return null;
            yield return null;
            Assert.That(Game.Face(0, 0), Is.EqualTo(CellFace.Flagged));
            Assert.That(Game.Face(10, 10), Is.EqualTo(CellFace.Hidden));
            Assert.That(Game.FlagCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Cursor_IsShownOnlyWhilePlayingOverBoard()
        {
            SetMouse(CellPoint(4, 4));
            yield return null;
            yield return null;
            Assert.That(_game.View.IsCursorVisible, Is.False);
            yield return StartMatch();
            SetMouse(CellPoint(4, 4));
            yield return null;
            yield return null;
            Assert.That(_game.View.IsCursorVisible, Is.True);
            SetMouse(new Vector2(Screen.width - 2, Screen.height / 2f));
            yield return null;
            yield return null;
            Assert.That(_game.View.IsCursorVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator Lose_ShowsResultKeepsBoardAndRetryMakesNewGame()
        {
            yield return StartMatch();
            PlayToLose();
            var lost = Game;
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("ドカン！"));
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(IsShown("submit"), Is.True);

            yield return Click(CellPoint(0, 0), MouseButton.Right);
            Assert.That(lost.FlagCount, Is.Zero, "終了後の操作は無視する");

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Game, Is.Not.SameAs(lost));
            Assert.That(Game.Phase, Is.EqualTo(GamePhase.Ready));
            Assert.That(_game.View.CellSprite(0, 0), Is.EqualTo(_game.View.FaceSprite(CellFace.Hidden)));
        }

        [UnityTest]
        public IEnumerator Win_ShowsResult()
        {
            yield return StartMatch();
            PlayToWin();
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("掘りきった！"));
            StringAssert.Contains("のこり爆弾  0", Text("mines"));
            StringAssert.Contains("未発掘  0", Text("safe"));
        }

        [UnityTest]
        public IEnumerator Escape_ReturnsToTitle()
        {
            yield return StartMatch();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(Game, Is.Null);
            Assert.That(Text("headline"), Is.EqualTo("SAFE DIG"));
        }

        [UnityTest]
        public IEnumerator Enter_AndClickInSameFrame_DoesNotDig()
        {
            SetMouse(CellPoint(2, 2));
            yield return null;
            _keyboard.Hold(Key.Enter);
            SetMouse(CellPoint(2, 2), MouseButton.Left);
            yield return null;
            _keyboard.Hold();
            SetMouse(CellPoint(2, 2));
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Game.Phase, Is.EqualTo(GamePhase.Ready));
        }

        [UnityTest]
        public IEnumerator FocusLoss_DiscardsPendingClick()
        {
            yield return StartMatch();
            SetMouse(CellPoint(6, 6));
            yield return null;
            SetMouse(CellPoint(6, 6), MouseButton.Left);
            InputSystem.Update();
            _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            Assert.That(Game.Phase, Is.EqualTo(GamePhase.Ready));
            _game.SendMessage("OnApplicationFocus", true);
            SetMouse(CellPoint(6, 6));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ClickWhileFocusLost_IsIgnored()
        {
            yield return StartMatch();
            _game.SendMessage("OnApplicationFocus", false);
            yield return Click(CellPoint(6, 6), MouseButton.Left);
            Assert.That(Game.Phase, Is.EqualTo(GamePhase.Ready));
            _game.SendMessage("OnApplicationFocus", true);
        }

        [UnityTest]
        public IEnumerator ClickThatRestoresFocus_DoesNotDig_ButNextClickDoes()
        {
            yield return StartMatch();
            SetMouse(CellPoint(6, 6));
            yield return null;
            _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            SetMouse(CellPoint(6, 6), MouseButton.Left);
            yield return null;
            SetMouse(CellPoint(6, 6));
            yield return null;
            Assert.That(Game.Phase, Is.EqualTo(GamePhase.Ready));

            yield return Click(CellPoint(6, 6), MouseButton.Left);
            Assert.That(Game.Phase, Is.EqualTo(GamePhase.Playing));
        }

        [UnityTest]
        public IEnumerator Buttons_MatchScreen()
        {
            yield return null;
            yield return null;
            Assert.That(IsShown("submit"), Is.True);
            Assert.That(IsShown("quit"), Is.True);
            Assert.That(IsShown("back"), Is.False);
            yield return StartMatch();
            yield return null;
            Assert.That(IsShown("submit"), Is.False);
            Assert.That(IsShown("back"), Is.True);
            Assert.That(IsShown("quit"), Is.False);
        }
    }
}
