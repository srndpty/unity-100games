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
    public sealed class PrismTideSceneTests
    {
        private const float WaitLimitSeconds = 10f;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private PrismTideGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("PrismTide");
            yield return null;
            _game = Object.FindAnyObjectByType<PrismTideGame>();
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

        private Match3Flow Flow => _game.Flow;

        private Match3Game Game => Flow.Game;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q(name).style.display.value == DisplayStyle.Flex;

        private Vector2 CellPoint(Point cell) => _game.ViewCamera.WorldToScreenPoint(_game.View.CellWorld(cell.X, cell.Y));

        private void SetFocus(bool focus) => _game.SendMessage("OnApplicationFocus", focus);

        private void SetMouse(Vector2 position, bool pressed = false)
        {
            var state = new MouseState { position = position };
            if (pressed) { state = state.WithButton(MouseButton.Left); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        private IEnumerator Click(Vector2 position)
        {
            SetMouse(position);
            yield return null;
            SetMouse(position, true);
            yield return null;
            SetMouse(position);
            yield return null;
        }

        private IEnumerator Drag(Vector2 from, Vector2 to)
        {
            SetMouse(from);
            yield return null;
            SetMouse(from, true);
            yield return null;
            SetMouse(Vector2.Lerp(from, to, .5f), true);
            yield return null;
            SetMouse(to, true);
            yield return null;
            SetMouse(to);
            yield return null;
        }

        private IEnumerator Begin(Func<int, Match3Game> factory = null)
        {
            if (factory != null) { Flow.MatchFactory = factory; }
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        private IEnumerator BeginTwoChain() => Begin(seed => new Match3Game(Flow.Config, seed, DemoBoards.TwoChain()));

        private IEnumerator WaitUntil(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + WaitLimitSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.That(condition(), Is.True, "待ち時間の上限を超えました。");
        }

        // バッチ実行ではUIイベントが配送されないので、実際の座標でボタンに当たることを確かめてから送る。
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
            Assert.That(panel.Pick(panelPoint), Is.SameAs(button));

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

        private void AssertViewMatchesBoard()
        {
            _game.Render();
            for (var y = 0; y < Board.Height; y++)
            {
                for (var x = 0; x < Board.Width; x++)
                {
                    Assert.That(_game.View.SpriteAt(x, y), Is.SameAs(_game.View.PieceSprite(Game.Board.At(x, y))), $"({x}, {y})");
                    Assert.That((_game.View.PiecePosition(x, y) - _game.View.CellWorld(x, y)).magnitude, Is.LessThan(.001f), $"({x}, {y})");
                }
            }

            Assert.That(_game.View.ShownPieces, Is.EqualTo(Board.CellCount));
        }

        [Test]
        public void SettingsAssetIsValid()
        {
            Assert.DoesNotThrow(_game.Settings.Validate);
        }

        [UnityTest]
        public IEnumerator Title_ShowsNameRulesAndADecorativeBoard()
        {
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(Text("headline"), Is.EqualTo("PRISM TIDE"));
            Assert.That(Text("message"), Does.Contain("3つ以上").And.Contain("Space"));
            Assert.That(IsShown("overlay"), Is.True);
            Assert.That(IsShown("quit"), Is.True);
            Assert.That(IsShown("stats"), Is.False);
            Assert.That(_game.View.ShownPieces, Is.EqualTo(Board.CellCount));
        }

        [UnityTest]
        public IEnumerator Enter_StartsAndShowsTheBoardAndMoves()
        {
            yield return Begin();
            yield return null;
            Assert.That(IsShown("overlay"), Is.False);
            Assert.That(IsShown("stats"), Is.True);
            Assert.That(Text("moves"), Is.EqualTo(Flow.Config.Moves.ToString()));
            Assert.That(Text("score"), Is.EqualTo("0"));
            AssertViewMatchesBoard();
        }

        [UnityTest]
        public IEnumerator Keyboard_SelectsAndSwaps()
        {
            yield return BeginTwoChain();
            var cursor = Flow.Input.Cursor;
            for (var i = 0; i < cursor.X; i++) { yield return _keyboard.Tap(Key.LeftArrow); }
            for (var i = 0; i < cursor.Y - 2; i++) { yield return _keyboard.Tap(Key.DownArrow); }
            Assert.That(Flow.Input.Cursor, Is.EqualTo(new Point(0, 2)));
            Assert.That(_game.View.IsCursorVisible, Is.True);

            yield return _keyboard.Tap(Key.Space);
            Assert.That(Flow.Input.Selected, Is.EqualTo(new Point(0, 2)));
            Assert.That(_game.View.IsSelectionVisible, Is.True);

            yield return _keyboard.Tap(Key.RightArrow);
            Assert.That(Flow.LastVerdict, Is.EqualTo(SwapVerdict.Legal));
            yield return WaitUntil(() => Game.AcceptsSwap);
            Assert.That(Game.MovesLeft, Is.EqualTo(Flow.Config.Moves - 1));
            Assert.That(Game.MaxChain, Is.GreaterThanOrEqualTo(2));
            Assert.That(Text("score"), Is.EqualTo(Game.Score.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)));
            AssertViewMatchesBoard();
        }

        [UnityTest]
        public IEnumerator Mouse_TwoClicksSwap()
        {
            yield return BeginTwoChain();
            yield return Click(CellPoint(DemoBoards.TwoChainSwap.From));
            Assert.That(Flow.Input.Selected, Is.EqualTo(DemoBoards.TwoChainSwap.From));
            Assert.That(_game.View.IsSelectionVisible, Is.True);
            Assert.That(_game.View.IsCursorVisible, Is.False, "マウスで操作したらカーソルは隠す");

            yield return Click(CellPoint(DemoBoards.TwoChainSwap.To));
            Assert.That(Flow.LastVerdict, Is.EqualTo(SwapVerdict.Legal));
            yield return WaitUntil(() => Game.AcceptsSwap);
            Assert.That(Game.MovesLeft, Is.EqualTo(Flow.Config.Moves - 1));
        }

        [UnityTest]
        public IEnumerator Mouse_DragSwaps()
        {
            yield return BeginTwoChain();
            yield return Drag(CellPoint(DemoBoards.TwoChainSwap.From), CellPoint(DemoBoards.TwoChainSwap.To));
            Assert.That(Flow.LastVerdict, Is.EqualTo(SwapVerdict.Legal));
            Assert.That(Flow.Input.Selected, Is.Null);
        }

        [UnityTest]
        public IEnumerator Mouse_NoMatchSwapGoesBackAndKeepsTheMove()
        {
            yield return BeginTwoChain();
            yield return Click(CellPoint(DemoBoards.NoMatchSwap.From));
            yield return Click(CellPoint(DemoBoards.NoMatchSwap.To));
            Assert.That(Flow.LastVerdict, Is.EqualTo(SwapVerdict.NoMatch));
            yield return WaitUntil(() => Game.Phase == GamePhase.Reverting && Game.PhaseRemaining < Game.PhaseDuration / 2);
            _game.Render();
            var from = DemoBoards.NoMatchSwap.From;
            Assert.That((_game.View.PiecePosition(from.X, from.Y) - _game.View.CellWorld(from.X, from.Y)).magnitude, Is.GreaterThan(.05f), "戻る途中は動いて見える");

            yield return WaitUntil(() => Game.AcceptsSwap);
            Assert.That(Game.Board, Is.EqualTo(DemoBoards.TwoChain()));
            Assert.That(Game.MovesLeft, Is.EqualTo(Flow.Config.Moves));
            AssertViewMatchesBoard();
        }

        [UnityTest]
        public IEnumerator ClickAfterFocusReturnIsIgnored()
        {
            yield return BeginTwoChain();
            yield return Click(CellPoint(new Point(2, 2)));
            Assert.That(Flow.Input.Selected, Is.EqualTo(new Point(2, 2)));

            SetFocus(false);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスを失うと一時停止する");
            Assert.That(Flow.Input.Selected, Is.Null, "選択も取り消す");

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
        public IEnumerator EscapePausesAndStopsTime()
        {
            yield return BeginTwoChain();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused));
            var ticks = Game.TickCount;
            yield return Click(CellPoint(new Point(0, 2)));
            Assert.That(Flow.Input.Selected, Is.Null, "一時停止中は選べない");
            Assert.That(Game.TickCount, Is.EqualTo(ticks));
            Assert.That(IsShown("restart"), Is.True);
            Assert.That(IsShown("title"), Is.True);

            yield return _keyboard.Tap(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [UnityTest]
        public IEnumerator Result_ShowsScoreAndEnterReplays()
        {
            var rules = Flow.Config.Copy();
            rules.TargetScore = 60;
            yield return Begin(seed => new Match3Game(rules, seed, DemoBoards.TwoChain()));
            yield return Click(CellPoint(DemoBoards.TwoChainSwap.From));
            yield return Click(CellPoint(DemoBoards.TwoChainSwap.To));
            yield return WaitUntil(() => Flow.Screen == AppScreen.Result);
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("目標達成！"));
            Assert.That(Text("message"), Does.Contain("目標 60"));
            Assert.That(Text("target"), Is.EqualTo("目標  60"), "試合ごとの目標を出す");
            Assert.That(IsShown("overlay"), Is.True);
            var seed = Flow.MatchSeed;

            yield return Click(CellPoint(new Point(3, 3)));
            Assert.That(Flow.Input.Selected, Is.Null, "結果画面では盤面を操作できない");

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Flow.MatchSeed, Is.Not.EqualTo(seed));
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.T);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [UnityTest]
        public IEnumerator Result_ShowsLossWhenMovesRunOut()
        {
            var rules = Flow.Config.Copy();
            rules.Moves = 1;
            rules.TargetScore = 1_000_000;
            yield return Begin(seed => new Match3Game(rules, seed, DemoBoards.Cross()));
            yield return Click(CellPoint(DemoBoards.CrossSwap.From));
            yield return Click(CellPoint(DemoBoards.CrossSwap.To));
            yield return WaitUntil(() => Flow.Screen == AppScreen.Result);
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("手数切れ"));
            Assert.That(Text("moves"), Is.EqualTo("0"));
            Assert.That(Text("message"), Does.Contain("使った手数  1 手").And.Contain("目標 1,000,000"));
        }

        [UnityTest]
        public IEnumerator SubmitButton_StartsTheMatch()
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
