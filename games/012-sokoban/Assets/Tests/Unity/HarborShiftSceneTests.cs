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
    public sealed class HarborShiftSceneTests
    {
        private const float WaitLimitSeconds = 5f;

        // 第4問。プレイヤーの上下左右が空いていて、箱を押せる面。
        private const int OpenLevel = 3;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private HarborShiftGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("HarborShift");
            yield return null;
            _game = Object.FindAnyObjectByType<HarborShiftGame>();
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

        private HarborFlow Flow => _game.Flow;

        private SokobanGame Game => _game.Flow.Game;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q<Button>(name).style.display.value == DisplayStyle.Flex;

        private IEnumerator StartLevel(int level)
        {
            if (Flow.Screen == AppScreen.Title) { Flow.Submit(); }
            Assert.That(Flow.SelectLevel(level), Is.True);
            yield return null;
        }

        private void AssertViewMatchesBoard()
        {
            _game.Render();
            var board = Game.Board;
            AssertNear(_game.View.PlayerPosition, _game.View.CellPosition(board.Player));
            Assert.That(_game.View.ShownBoxes, Is.EqualTo(board.Boxes.Count));
            foreach (var box in board.Boxes)
            {
                var expected = Game.Map.IsGoal(box) ? _game.View.BoxPlacedSprite : _game.View.BoxSprite;
                Assert.That(_game.View.BoxSpriteAt(box), Is.SameAs(expected), "箱 " + box);
            }
        }

        private static void AssertNear(Vector3 actual, Vector3 expected) =>
            Assert.That((actual - expected).magnitude, Is.LessThan(.001f), $"{actual} と {expected}");

        private void SetMouse(Vector2 position, MouseButton? pressed = null)
        {
            var state = new MouseState { position = position };
            if (pressed.HasValue) { state = state.WithButton(pressed.Value); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        private static void SendClick(Button button)
        {
            var point = button.worldBound.center;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = point, button = 0 }))
            {
                down.target = button;
                button.SendEvent(down);
            }

            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = point, button = 0 }))
            {
                up.target = button;
                button.SendEvent(up);
            }
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
            SendClick(button);
            SetMouse(point);
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Title_ShowsInstructions()
        {
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(Text("headline"), Is.EqualTo("HARBOR SHIFT"));
            Assert.That(Text("message"), Does.Contain("矢印キー"));
            Assert.That(Text("message"), Does.Contain("1手戻す"));
            Assert.That(IsShown("submit"), Is.True);
            Assert.That(IsShown("quit"), Is.True);
            Assert.That(IsShown("back"), Is.False);
        }

        [UnityTest]
        public IEnumerator Hud_IsRewrittenOnlyWhenItsStateChanges()
        {
            const string marker = "書き換え検出";
            yield return StartLevel(OpenLevel);
            var moves = _game.HudRoot.Q<Label>("moves");
            moves.text = marker;
            yield return null;
            yield return null;
            Assert.That(moves.text, Is.EqualTo(marker), "状態が同じなら描き直さない");

            Flow.Move(Direction.Up);
            yield return null;
            Assert.That(moves.text, Does.Contain("1"), "手数が変わったら描き直す");
        }

        [UnityTest]
        public IEnumerator Keyboard_PlaysFromTitleToTheNextLevel()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.LevelSelect));
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Flow.SelectedLevel, Is.Zero);

            yield return _keyboard.Tap(Key.RightArrow);
            yield return _keyboard.Tap(Key.RightArrow);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Cleared));
            Assert.That(Text("headline"), Is.EqualTo("荷おろし完了！"));
            Assert.That(Text("message"), Does.Contain("2手"));

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Flow.SelectedLevel, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ArrowsAndWasd_MoveInTheirDirections()
        {
            var cases = new (Key Key, Direction Direction)[]
            {
                (Key.UpArrow, Direction.Up), (Key.W, Direction.Up),
                (Key.LeftArrow, Direction.Left), (Key.A, Direction.Left),
                (Key.RightArrow, Direction.Right), (Key.D, Direction.Right),
            };
            yield return StartLevel(OpenLevel);
            foreach (var (key, direction) in cases)
            {
                Flow.Restart();
                var expected = Game.Board.Player.Step(direction);
                yield return _keyboard.Tap(key);
                Assert.That(Game.Board.Player, Is.EqualTo(expected), key.ToString());
            }

            foreach (var key in new[] { Key.DownArrow, Key.S })
            {
                Flow.Restart();
                Flow.Move(Direction.Up);
                var expected = Game.Board.Player.Step(Direction.Down);
                yield return _keyboard.Tap(key);
                Assert.That(Game.Board.Player, Is.EqualTo(expected), key.ToString());
            }
        }

        [UnityTest]
        public IEnumerator View_MatchesTheBoardAfterPushUndoAndRestart()
        {
            yield return StartLevel(OpenLevel);
            AssertViewMatchesBoard();

            foreach (var direction in new[] { Direction.Up, Direction.Left, Direction.Left })
            {
                Flow.Move(direction);
                yield return null;
                AssertViewMatchesBoard();
            }

            Assert.That(Game.PushCount, Is.EqualTo(1));
            Flow.Undo();
            yield return null;
            AssertViewMatchesBoard();
            Assert.That(Game.PushCount, Is.Zero);

            Flow.Restart();
            yield return null;
            AssertViewMatchesBoard();
            Assert.That(Game.Board, Is.EqualTo(Game.Map.Initial));
        }

        [UnityTest]
        public IEnumerator View_ShowsEveryLayerOfEveryLevel()
        {
            Flow.Submit();
            for (var level = 0; level < LevelCatalog.Count; level++)
            {
                Assert.That(Flow.SelectLevel(level), Is.True);
                yield return null;
                _game.Render();

                var map = Game.Map;
                var floors = 0;
                var walls = 0;
                for (var y = 0; y < map.Height; y++)
                {
                    for (var x = 0; x < map.Width; x++)
                    {
                        if (map.IsFloor(new Cell(x, y))) { floors++; }
                        if (map.IsWall(new Cell(x, y))) { walls++; }
                    }
                }

                Assert.That(_game.View.ShownFloors, Is.EqualTo(floors), "床 第" + (level + 1) + "問");
                Assert.That(_game.View.ShownWalls, Is.EqualTo(walls), "壁 第" + (level + 1) + "問");
                Assert.That(_game.View.ShownGoals, Is.EqualTo(map.Goals.Count), "目標 第" + (level + 1) + "問");
                AssertViewMatchesBoard();
                Flow.Back();
            }
        }

        [UnityTest]
        public IEnumerator Panel_DoesNotCoverAnyLevel()
        {
            yield return null;
            yield return null;
            Flow.Submit();
            var panelLeftRatio = _game.HudRoot.Q("panel").worldBound.xMin / _game.HudRoot.panel.visualTree.worldBound.width;
            for (var level = 0; level < LevelCatalog.Count; level++)
            {
                Assert.That(Flow.SelectLevel(level), Is.True);
                _game.Render();
                var right = _game.View.transform.position + (Vector3.right * _game.View.BoardHalfWidth);
                var rightRatio = _game.ViewCamera.WorldToScreenPoint(right).x / Screen.width;
                var left = _game.View.transform.position + (Vector3.left * _game.View.BoardHalfWidth);
                Assert.That(rightRatio, Is.LessThan(panelLeftRatio), "第" + (level + 1) + "問");
                Assert.That(_game.ViewCamera.WorldToScreenPoint(left).x, Is.GreaterThan(0), "第" + (level + 1) + "問");
                Assert.That(_game.View.BoardHalfHeight, Is.LessThan(_game.ViewCamera.orthographicSize), "第" + (level + 1) + "問");
                Flow.Back();
            }
        }

        [UnityTest]
        public IEnumerator Preview_FollowsTheCursorOnTheSelectScreen()
        {
            yield return _keyboard.Tap(Key.Enter);
            yield return _keyboard.Tap(Key.DownArrow);
            Assert.That(Flow.SelectedLevel, Is.EqualTo(HarborFlow.SelectColumns));
            _game.Render();
            Assert.That(_game.View.ShownGoals, Is.EqualTo(LevelCatalog.Maps[HarborFlow.SelectColumns].Goals.Count));
            Assert.That(Text("level"), Does.Contain(LevelCatalog.Names[HarborFlow.SelectColumns]));
        }

        [UnityTest]
        public IEnumerator SameFrame_MoveThenUndoAreAppliedInOrder()
        {
            yield return StartLevel(OpenLevel);
            _keyboard.Hold(Key.UpArrow);
            _keyboard.Hold(Key.UpArrow, Key.Z);
            yield return null;
            _keyboard.Hold();
            yield return null;
            Assert.That(Game.Board, Is.EqualTo(Game.Map.Initial));
            Assert.That(Game.MoveCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SameFrame_UndoThenMoveAreAppliedInOrder()
        {
            yield return StartLevel(OpenLevel);
            _keyboard.Hold(Key.Z);
            _keyboard.Hold(Key.Z, Key.UpArrow);
            yield return null;
            _keyboard.Hold();
            yield return null;
            Assert.That(Game.MoveCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator UndoKeys_RevertOneMoveEach()
        {
            yield return StartLevel(OpenLevel);
            foreach (var key in new[] { Key.Z, Key.Backspace })
            {
                Flow.Move(Direction.Up);
                yield return _keyboard.Tap(key);
                Assert.That(Game.Board, Is.EqualTo(Game.Map.Initial), key.ToString());
            }
        }

        [UnityTest]
        public IEnumerator RestartKey_ReturnsToTheInitialBoard()
        {
            yield return StartLevel(OpenLevel);
            Flow.Move(Direction.Up);
            Flow.Move(Direction.Left);
            yield return _keyboard.Tap(Key.R);
            Assert.That(Game.Board, Is.EqualTo(Game.Map.Initial));
            Assert.That(Game.MoveCount, Is.Zero);
            AssertViewMatchesBoard();
        }

        [UnityTest]
        public IEnumerator HeldKey_RepeatsUntilTheLevelIsCleared()
        {
            yield return StartLevel(0);
            _keyboard.Hold(Key.RightArrow);
            var deadline = Time.realtimeSinceStartup + WaitLimitSeconds;
            while (Flow.Screen != AppScreen.Cleared && Time.realtimeSinceStartup < deadline) { yield return null; }
            _keyboard.Hold();
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Cleared));
        }

        [UnityTest]
        public IEnumerator HeldKey_DoesNotCarryIntoTheNextLevel()
        {
            yield return StartLevel(0);
            Flow.Move(Direction.Right);
            _keyboard.Hold(Key.RightArrow);
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Cleared));

            _keyboard.Hold(Key.RightArrow, Key.Enter);
            yield return null;
            Assert.That(Flow.SelectedLevel, Is.EqualTo(1));
            var deadline = Time.realtimeSinceStartup + (_game.Settings.RepeatDelay * 3);
            while (Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.That(Game.MoveCount, Is.Zero);
            _keyboard.Hold();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Escape_StepsBackToTheTitle()
        {
            yield return StartLevel(OpenLevel);
            Flow.Move(Direction.Up);
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.LevelSelect));
            Assert.That(Flow.Game, Is.Null);
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [UnityTest]
        public IEnumerator AfterClear_KeysDoNotChangeTheBoard()
        {
            yield return StartLevel(0);
            Flow.Move(Direction.Right);
            Flow.Move(Direction.Right);
            yield return null;
            var board = Game.Board;

            yield return _keyboard.Tap(Key.LeftArrow);
            yield return _keyboard.Tap(Key.Z);

            Assert.That(Game.Board, Is.EqualTo(board));
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Cleared));
            AssertViewMatchesBoard();
            Assert.That(_game.View.BoxSpriteAt(board.Boxes[0]), Is.SameAs(_game.View.BoxPlacedSprite));
        }

        [UnityTest]
        public IEnumerator ClearScreen_OffersNextRetryAndSelect()
        {
            yield return StartLevel(0);
            Flow.Move(Direction.Right);
            Flow.Move(Direction.Right);
            yield return null;
            Assert.That(IsShown("submit"), Is.True);
            Assert.That(_game.HudRoot.Q<Button>("submit").text, Does.Contain("次の面"));
            Assert.That(_game.HudRoot.Q<Button>("restart").text, Does.Contain("もう一度"));
            Assert.That(IsShown("back"), Is.True);
            Assert.That(IsShown("undo"), Is.False);
            Assert.That(Text("best"), Does.Contain("2"));
        }

        [UnityTest]
        public IEnumerator LastLevel_ClearOffersTheSelectScreen()
        {
            yield return StartLevel(LevelCatalog.Count - 1);
            for (var level = 0; level < LevelCatalog.Count - 1; level++) { Flow.Records.Record(level, 1); }
            foreach (var symbol in "RUULUURDRRRLDRRRRDRRUULDUULLLDLDDLDLLUULURRRRDRRRDDRRULRUULDUULLLDLDRRRLLLDLDRRRR")
            {
                var direction = symbol switch
                {
                    'U' => Direction.Up,
                    'D' => Direction.Down,
                    'L' => Direction.Left,
                    _ => Direction.Right,
                };
                Assert.That(Flow.Move(direction), Is.Not.EqualTo(MoveResult.Rejected));
            }

            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Cleared));
            Assert.That(Text("headline"), Is.EqualTo("全問クリア！"));
            Assert.That(_game.HudRoot.Q<Button>("submit").text, Does.Contain("面選択へ"));
            Assert.That(IsShown("back"), Is.False, "同じ行き先のボタンを並べない");

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.LevelSelect));
        }

        [UnityTest]
        public IEnumerator SelectScreen_MarksClearedLevels()
        {
            yield return StartLevel(0);
            Flow.Move(Direction.Right);
            Flow.Move(Direction.Right);
            Flow.Back();
            yield return null;
            var first = _game.HudRoot.Q<Button>("level-0");
            var second = _game.HudRoot.Q<Button>("level-1");
            Assert.That(first.text, Does.Contain("済"));
            Assert.That(first.ClassListContains("level--cleared"), Is.True);
            Assert.That(second.ClassListContains("level--cleared"), Is.False);
        }

        [UnityTest]
        public IEnumerator Animation_FinishesWithinTheRealTimeLimit()
        {
            yield return StartLevel(OpenLevel);
            Flow.Move(Direction.Up);
            yield return null;
            var deadline = Time.realtimeSinceStartup + WaitLimitSeconds;
            while (_game.View.IsAnimating && Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.That(_game.View.IsAnimating, Is.False);
            AssertNear(_game.View.PlayerPosition, _game.View.CellPosition(Game.Board.Player));
        }

        [UnityTest]
        public IEnumerator LevelButton_StartsThatLevel()
        {
            Flow.Submit();
            yield return ClickButton("level-6");
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Flow.SelectedLevel, Is.EqualTo(6));
        }

        [UnityTest]
        public IEnumerator HudButtons_UndoAndRestart()
        {
            yield return StartLevel(OpenLevel);
            Flow.Move(Direction.Up);
            Flow.Move(Direction.Left);
            yield return ClickButton("undo");
            Assert.That(Game.MoveCount, Is.EqualTo(1));
            yield return ClickButton("restart");
            Assert.That(Game.MoveCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator QuitButton_CallsTheHandlerOnce()
        {
            var calls = 0;
            _game.QuitHandler = () => calls++;
            yield return ClickButton("quit");
            Assert.That(calls, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator FocusLost_IgnoresKeys()
        {
            yield return StartLevel(OpenLevel);
            _game.SendMessage("OnApplicationFocus", false);
            yield return _keyboard.Tap(Key.UpArrow);
            yield return _keyboard.Tap(Key.R);
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Game.MoveCount, Is.Zero);
            _game.SendMessage("OnApplicationFocus", true);
        }

        [UnityTest]
        public IEnumerator FocusLost_InTheSameFrameDropsThePendingMove()
        {
            yield return StartLevel(OpenLevel);
            _keyboard.Hold(Key.UpArrow);
            InputSystem.Update();
            _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            Assert.That(Game.MoveCount, Is.Zero);
            _keyboard.Hold();
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ClickThatRestoresFocus_IsDiscarded_ButTheNextClickWorks()
        {
            yield return StartLevel(OpenLevel);
            Flow.Move(Direction.Up);
            yield return null;
            yield return null;
            _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            SendClick(_game.HudRoot.Q<Button>("undo"));
            yield return null;
            yield return null;
            Assert.That(Game.MoveCount, Is.EqualTo(1), "復帰のクリックでは戻さない");

            yield return ClickButton("undo");
            Assert.That(Game.MoveCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator HeldKey_DoesNotRepeatAfterFocusReturns()
        {
            yield return StartLevel(OpenLevel);
            _keyboard.Hold(Key.UpArrow);
            yield return null;
            Assert.That(Game.MoveCount, Is.EqualTo(1));
            _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            var deadline = Time.realtimeSinceStartup + (_game.Settings.RepeatDelay * 3);
            while (Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.That(Game.MoveCount, Is.EqualTo(1));
            _keyboard.Hold();
            yield return null;
        }
    }
}
