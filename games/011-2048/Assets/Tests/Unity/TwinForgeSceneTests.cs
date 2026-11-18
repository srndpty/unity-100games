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
    public sealed class TwinForgeSceneTests
    {
        private static readonly BoardState _winBoard = BoardState.Empty.With(0, 1024).With(1, 1024);

        private static readonly BoardState _loseBoard = BoardState.FromValues(
            2, 2, 8, 16,
            8, 16, 32, 64,
            4, 8, 16, 32,
            8, 16, 32, 64);

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private TwinForgeGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("TwinForge");
            yield return null;
            _game = Object.FindAnyObjectByType<TwinForgeGame>();
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

        private ForgeGame Game => _game.Flow.Game;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q<Button>(name).style.display.value == DisplayStyle.Flex;

        private IEnumerator StartMatch()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        private IEnumerator StartFrom(BoardState board)
        {
            _game.Flow.StartFrom(board);
            yield return null;
        }

        private void AssertViewMatchesBoard(BoardState board)
        {
            _game.Render();
            for (var cell = 0; cell < BoardState.CellCount; cell++)
            {
                var expected = board[cell] == 0 ? null : _game.View.PlateSprite(board[cell]);
                Assert.That(_game.View.SpriteAt(cell), Is.SameAs(expected), "マス " + cell);
            }
        }

        private void SetMouse(Vector2 position, MouseButton? pressed = null)
        {
            var state = new MouseState { position = position };
            if (pressed.HasValue) { state = state.WithButton(pressed.Value); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        // バッチ実行ではUIイベントが配送されないので、実際の座標でボタンに当たることを確かめてから送る。
        private IEnumerator ClickButton(string name)
        {
            yield return null;
            yield return null;
            var button = _game.HudRoot.Q<Button>(name);
            var panelPoint = button.worldBound.center;
            var panel = _game.HudRoot.panel;
            var rootSize = panel.visualTree.worldBound.size;
            var point = new Vector2(panelPoint.x / rootSize.x * Screen.width, Screen.height - (panelPoint.y / rootSize.y * Screen.height));
            Assert.That(panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, new Vector2(point.x, Screen.height - point.y))), Is.SameAs(button));

            SetMouse(point);
            yield return null;
            SetMouse(point, MouseButton.Left);
            yield return null;
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

            SetMouse(point);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Scene_ShowsEmptyBoardAndInstructionsBeforePlay()
        {
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(Text("headline"), Is.EqualTo("TWIN FORGE"));
            Assert.That(Text("message"), Does.Contain("矢印キー"));
            Assert.That(IsShown("submit"), Is.True);
            Assert.That(IsShown("quit"), Is.True);
            Assert.That(IsShown("back"), Is.False);
            AssertViewMatchesBoard(BoardState.Empty);
        }

        [UnityTest]
        public IEnumerator Plates_HaveDistinctSpritesForEveryValue()
        {
            yield return null;
            var sprites = new HashSet<Sprite>();
            for (var value = 2; value <= ForgeRules.TargetValue; value *= 2)
            {
                Assert.That(_game.View.PlateSprite(value), Is.Not.Null);
                sprites.Add(_game.View.PlateSprite(value));
            }

            Assert.That(sprites.Count, Is.EqualTo(11));
        }

        [UnityTest]
        public IEnumerator Panel_DoesNotCoverBoard()
        {
            yield return null;
            yield return null;
            var boardRight = _game.View.transform.position + (Vector3.right * _game.View.BoardHalfWidth);
            var boardRightRatio = _game.ViewCamera.WorldToScreenPoint(boardRight).x / Screen.width;
            var panelLeftRatio = _game.HudRoot.Q("panel").worldBound.xMin / _game.HudRoot.panel.visualTree.worldBound.width;
            Assert.That(boardRightRatio, Is.LessThan(panelLeftRatio));
        }

        [UnityTest]
        public IEnumerator Enter_StartsWithTwoTilesShown()
        {
            yield return StartMatch();
            Assert.That(Game.Board.TileCount, Is.EqualTo(ForgeRules.InitialTiles));
            AssertViewMatchesBoard(Game.Board);
        }

        [UnityTest]
        public IEnumerator ArrowsAndWasd_MoveInTheirDirections()
        {
            var cases = new (Key Key, int Expected)[]
            {
                (Key.RightArrow, 7), (Key.D, 7), (Key.DownArrow, 13), (Key.S, 13),
            };
            foreach (var (key, expected) in cases)
            {
                yield return StartFrom(BoardState.Empty.With(5, 1024));
                yield return _keyboard.Tap(key);
                Assert.That(Game.Board[expected], Is.EqualTo(1024), key.ToString());
            }

            var opposite = new (Key Key, int Expected)[]
            {
                (Key.LeftArrow, 4), (Key.A, 4), (Key.UpArrow, 1), (Key.W, 1),
            };
            foreach (var (key, expected) in opposite)
            {
                yield return StartFrom(BoardState.Empty.With(5, 1024));
                yield return _keyboard.Tap(key);
                Assert.That(Game.Board[expected], Is.EqualTo(1024), key.ToString());
            }
        }

        [UnityTest]
        public IEnumerator HeldKey_MovesOnlyOnce()
        {
            yield return StartFrom(BoardState.Empty.With(5, 2));
            _keyboard.Hold(Key.RightArrow);
            for (var i = 0; i < 10; i++) { yield return null; }
            _keyboard.Hold();
            yield return null;
            Assert.That(Game.MoveCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator UnchangedMove_KeepsBoardAndCount()
        {
            yield return StartFrom(BoardState.Empty.With(0, 2));
            yield return _keyboard.Tap(Key.LeftArrow);
            Assert.That(Game.MoveCount, Is.Zero);
            Assert.That(Game.Board.TileCount, Is.EqualTo(1));
            Assert.That(_game.View.IsAnimating, Is.False);
        }

        [UnityTest]
        public IEnumerator Merge_UpdatesScoreOnHud()
        {
            yield return StartFrom(BoardState.Empty.With(0, 2).With(1, 2));
            yield return _keyboard.Tap(Key.LeftArrow);
            Assert.That(Game.Score, Is.EqualTo(4));
            Assert.That(Text("score"), Does.Contain("4"));
            Assert.That(Text("moves"), Does.Contain("1"));
        }

        [UnityTest]
        public IEnumerator InputDuringAnimation_IsNotLostAndViewEndsOnFinalBoard()
        {
            var settings = _game.Settings;
            var slide = settings.SlideSeconds;
            settings.SlideSeconds = 1;
            try
            {
                yield return StartFrom(BoardState.Empty.With(0, 2));
                yield return _keyboard.Tap(Key.RightArrow);
                Assert.That(_game.View.IsAnimating, Is.True);
                yield return _keyboard.Tap(Key.LeftArrow);
                Assert.That(_game.View.IsAnimating, Is.True);
                yield return _keyboard.Tap(Key.RightArrow);
                Assert.That(Game.MoveCount, Is.EqualTo(3));
                AssertViewMatchesBoard(Game.Board);
                Assert.That(_game.View.IsAnimating, Is.False);
            }
            finally
            {
                settings.SlideSeconds = slide;
            }
        }

        [UnityTest]
        public IEnumerator Animation_FinishesOnItsOwn()
        {
            yield return StartFrom(BoardState.Empty.With(0, 2));
            yield return _keyboard.Tap(Key.RightArrow);

            // バッチ実行はフレームが極端に短いので、フレーム数ではなく実時間で待つ。
            var deadline = Time.realtimeSinceStartup + 5f;
            while (_game.View.IsAnimating && Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.That(_game.View.IsAnimating, Is.False);
            for (var cell = 0; cell < BoardState.CellCount; cell++)
            {
                var expected = Game.Board[cell] == 0 ? null : _game.View.PlateSprite(Game.Board[cell]);
                Assert.That(_game.View.SpriteAt(cell), Is.SameAs(expected));
            }
        }

        [UnityTest]
        public IEnumerator Win_ShowsResultKeepsBoardAndIgnoresMoves()
        {
            yield return StartFrom(_winBoard);
            yield return _keyboard.Tap(Key.LeftArrow);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(Text("headline"), Is.EqualTo("2048 完成！"));
            Assert.That(IsShown("submit"), Is.True);
            var board = Game.Board;
            yield return _keyboard.Tap(Key.RightArrow);
            Assert.That(Game.Board, Is.EqualTo(board));
            AssertViewMatchesBoard(board);
        }

        [UnityTest]
        public IEnumerator Lose_ShowsResultAndRetryMakesNewGame()
        {
            yield return StartFrom(_loseBoard);
            yield return _keyboard.Tap(Key.LeftArrow);

            // 出現する板が 2 でも 4 でも手詰まりになる盤面なので、結果は変わらない。
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(Text("headline"), Is.EqualTo("手詰まり"));
            var finished = Game;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Game, Is.Not.SameAs(finished));
            Assert.That(Game.Seed, Is.Not.EqualTo(finished.Seed));
            AssertViewMatchesBoard(Game.Board);
        }

        [UnityTest]
        public IEnumerator Escape_ReturnsToTitleAndClearsBoard()
        {
            yield return StartMatch();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            AssertViewMatchesBoard(BoardState.Empty);
        }

        [UnityTest]
        public IEnumerator SubmitButton_StartsMatch()
        {
            yield return ClickButton("submit");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [UnityTest]
        public IEnumerator BackButton_ReturnsToTitle()
        {
            yield return StartMatch();
            yield return ClickButton("back");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [UnityTest]
        public IEnumerator RetryButton_StartsNewMatchFromResult()
        {
            yield return StartFrom(_winBoard);
            yield return _keyboard.Tap(Key.LeftArrow);
            var finished = Game;
            yield return ClickButton("submit");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Game, Is.Not.SameAs(finished));
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
        public IEnumerator FocusLoss_DiscardsPendingMove()
        {
            yield return StartFrom(BoardState.Empty.With(0, 2));
            _keyboard.Hold(Key.RightArrow);
            InputSystem.Update();
            _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            _keyboard.Hold();
            yield return null;
            yield return null;
            Assert.That(Game.MoveCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator KeyWhileFocusLost_IsIgnored()
        {
            yield return StartFrom(BoardState.Empty.With(0, 2));
            _game.SendMessage("OnApplicationFocus", false);
            yield return _keyboard.Tap(Key.RightArrow);
            Assert.That(Game.MoveCount, Is.Zero);
            _game.SendMessage("OnApplicationFocus", true);
            yield return _keyboard.Tap(Key.RightArrow);
            Assert.That(Game.MoveCount, Is.EqualTo(1));
        }

        [Test]
        public void Settings_RejectInvalidValues()
        {
            var settings = ScriptableObject.CreateInstance<TwinForgeSettings>();
            try
            {
                settings.Validate();
                settings.SlideSeconds = float.NaN;
                Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
                settings.SlideSeconds = .1f;
                settings.PopSeconds = -1;
                Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
                settings.PopSeconds = .1f;
                settings.Volume = 2;
                Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
                settings.Volume = .1f;
                settings.Rules.FourChancePercent = 101;
                Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
