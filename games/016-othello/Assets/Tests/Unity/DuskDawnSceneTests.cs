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
using Position = Game.Core.Position;

namespace Game.Unity.Tests
{
    public sealed class DuskDawnSceneTests
    {
        private const float WaitLimitSeconds = 10f;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private DuskDawnGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("DuskDawn");
            yield return null;
            _game = Object.FindAnyObjectByType<DuskDawnGame>();
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            Time.timeScale = 1;
            InputSystem.RemoveDevice(_mouse);
            _keyboard.Dispose();
            yield return null;
        }

        private OthelloFlow Flow => _game.Flow;

        private OthelloGame Game => Flow.Game;

        private static Square S(string name) => Square.Parse(name);

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q(name).style.display.value == DisplayStyle.Flex;

        private bool IsSelected(string name) => _game.HudRoot.Q(name).ClassListContains("option--selected");

        private Vector2 CellPoint(Square square) => _game.ViewCamera.WorldToScreenPoint(_game.View.CellWorld(square));

        private void SetFocus(bool focus) => _game.SendMessage("OnApplicationFocus", focus);

        private void SetMouse(Vector2 position, bool pressed = false)
        {
            var state = new MouseState { position = position };
            if (pressed) { state = state.WithButton(MouseButton.Left); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        private IEnumerator Click(Square square)
        {
            var position = CellPoint(square);
            SetMouse(position);
            yield return null;
            SetMouse(position, true);
            yield return null;
            SetMouse(position);
            yield return null;
        }

        private IEnumerator Begin(Func<int, OthelloGame> factory = null)
        {
            if (factory != null) { Flow.GameFactory = factory; }
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        private IEnumerator WaitUntil(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + WaitLimitSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.That(condition(), Is.True, "待ち時間の上限を超えました。");
        }

        private void StepUntil(Func<bool> condition)
        {
            for (var i = 0; i < 10000 && !condition(); i++) { Flow.StepTick(); }
            Assert.That(condition(), Is.True, "Tick の上限を超えました。");
        }

        // 結果画面まで、プレイヤーの手番では実際のクリックで打つ。
        private IEnumerator PlayToTheEndWithClicks()
        {
            var deadline = Time.realtimeSinceStartup + WaitLimitSeconds;
            while (Flow.Screen == AppScreen.Playing && Time.realtimeSinceStartup < deadline)
            {
                if (Flow.IsPlayerTurn) { yield return Click(Searcher.Search(Game.Position, 1).Move); }
                else { yield return null; }
            }

            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Result));
            yield return null;
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

        private void AssertViewMatchesPosition()
        {
            _game.Render();
            var shown = 0;
            for (var i = 0; i < Square.Count; i++)
            {
                var square = Square.FromIndex(i);
                var disc = Game.Position.At(square);
                Assert.That(_game.View.SpriteAt(square), Is.SameAs(_game.View.DiscSprite(disc)), square.Name);
                if (disc != Disc.Empty)
                {
                    shown++;
                    Assert.That(_game.View.WidthAt(square), Is.EqualTo(1f).Within(.001f), square.Name);
                }
            }

            Assert.That(_game.View.ShownDiscs, Is.EqualTo(shown));
        }

        [Test]
        public void SettingsAssetIsValid()
        {
            Assert.DoesNotThrow(_game.Settings.Validate);
        }

        [UnityTest]
        public IEnumerator Title_ShowsNameRulesOptionsAndTheStartingBoard()
        {
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(Text("headline"), Is.EqualTo("DUSK & DAWN"));
            Assert.That(Text("message"), Does.Contain("はさむ").And.Contain("パス").And.Contain("Enter"));
            Assert.That(IsShown("overlay"), Is.True);
            Assert.That(IsShown("options"), Is.True);
            Assert.That(IsShown("quit"), Is.True);
            Assert.That(IsShown("stats"), Is.False);
            Assert.That(IsSelected("level-normal"), Is.True);
            Assert.That(IsSelected("side-black"), Is.True);
            Assert.That(_game.View.ShownDiscs, Is.EqualTo(4));
            Assert.That(_game.View.ShownHints, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator Title_KeysAndButtonsChooseLevelAndSide()
        {
            yield return _keyboard.Tap(Key.RightArrow);
            yield return _keyboard.Tap(Key.DownArrow);
            Assert.That(Flow.Level, Is.EqualTo(CpuLevel.Strong));
            Assert.That(Flow.PlayerSide, Is.EqualTo(Side.White));
            yield return null;
            Assert.That(IsSelected("level-strong"), Is.True);
            Assert.That(IsSelected("level-normal"), Is.False);
            Assert.That(IsSelected("side-white"), Is.True);

            yield return ClickButton("level-weak");
            yield return ClickButton("side-black");
            Assert.That(Flow.Level, Is.EqualTo(CpuLevel.Weak));
            Assert.That(Flow.PlayerSide, Is.EqualTo(Side.Black));

            yield return Begin();
            Assert.That(Flow.Cpu.Depth, Is.EqualTo(1));
            yield return null;
            Assert.That(Text("level"), Does.Contain("弱い").And.Contain("1手読み"));
        }

        [UnityTest]
        public IEnumerator Enter_StartsAndShowsCountsAndLegalMoveHints()
        {
            yield return Begin();
            yield return null;
            Assert.That(IsShown("overlay"), Is.False);
            Assert.That(IsShown("stats"), Is.True);
            Assert.That(Text("black-count"), Is.EqualTo("2"));
            Assert.That(Text("white-count"), Is.EqualTo("2"));
            Assert.That(Text("black-name"), Does.StartWith("あなた"));
            Assert.That(Text("status"), Is.EqualTo("あなたの番です。"));
            Assert.That(_game.HudRoot.Q("black-row").ClassListContains("side-row--turn"), Is.True);
            Assert.That(_game.View.ShownHints, Is.EqualTo(4));
            foreach (var name in new[] { "d3", "c4", "f5", "e6" }) { Assert.That(_game.View.IsHintShown(S(name)), Is.True, name); }
            Assert.That(_game.View.IsCursorVisible, Is.False, "キーを使うまでカーソルは出さない");
            AssertViewMatchesPosition();
        }

        [UnityTest]
        public IEnumerator Mouse_ClickPlaysAndTheCpuAnswers()
        {
            yield return Begin();
            yield return Click(S("d3"));
            Assert.That(Flow.LastVerdict, Is.EqualTo(PlayVerdict.Ok));
            Assert.That(Game.LastMove, Is.EqualTo(S("d3")));
            _game.Render();
            Assert.That(_game.View.ShownHints, Is.EqualTo(0), "CPU の手番では印を出さない");

            yield return null;
            Assert.That(Text("status"), Is.EqualTo("CPU が考えています…"));
            yield return WaitUntil(() => Flow.IsPlayerTurn);
            Assert.That(Game.MovesPlayed, Is.EqualTo(2));
            Assert.That(_game.View.IsLastMoveVisible, Is.True);
            Assert.That((_game.View.LastMovePosition - _game.View.CellWorld(Game.LastMove.Value)).magnitude, Is.LessThan(.001f));
            yield return null;
            Assert.That(Text("black-count"), Is.EqualTo(Game.Count(Side.Black).ToString()));
            Assert.That(Text("white-count"), Is.EqualTo(Game.Count(Side.White).ToString()));
            AssertViewMatchesPosition();
        }

        [UnityTest]
        public IEnumerator Mouse_IllegalClickIsRejectedButALegalClickPlays()
        {
            yield return Begin();
            yield return Click(S("a1"));
            Assert.That(Flow.LastVerdict, Is.EqualTo(PlayVerdict.NoFlip));
            Assert.That(Game.Position, Is.EqualTo(Position.Initial));
            yield return null;
            Assert.That(Text("status"), Does.Contain("そこには打てません"));

            // 同じ手順で、打てるマスなら打てる。
            yield return Click(S("f5"));
            Assert.That(Flow.LastVerdict, Is.EqualTo(PlayVerdict.Ok));
            Assert.That(Game.LastMove, Is.EqualTo(S("f5")));
        }

        [UnityTest]
        public IEnumerator Keyboard_MovesTheCursorAndPlaces()
        {
            yield return Begin();
            yield return _keyboard.Tap(Key.UpArrow);
            Assert.That(Flow.Cursor.Position, Is.EqualTo(S("d3")));
            Assert.That(_game.View.IsCursorVisible, Is.True);
            Assert.That((_game.View.CursorPosition - _game.View.CellWorld(S("d3"))).magnitude, Is.LessThan(.001f));

            yield return _keyboard.Tap(Key.Space);
            Assert.That(Game.LastMove, Is.EqualTo(S("d3")));
        }

        [UnityTest]
        public IEnumerator Flip_ShrinksTheOldColorThenGrowsTheNewColor()
        {
            yield return Begin();

            // 実時間では進めず、Tick をテストから進めて決まった進み具合で描く。
            Time.timeScale = 0;
            try
            {
                yield return Click(S("d3"));
                Assert.That(Game.Phase, Is.EqualTo(GamePhase.Placing));
                var flipped = S("d4");
                var duration = Game.PhaseDuration;
                Assert.That(duration, Is.GreaterThanOrEqualTo(6));
                var white = _game.View.DiscSprite(Disc.White);
                var black = _game.View.DiscSprite(Disc.Black);

                _game.Render();
                Assert.That(_game.View.SpriteAt(flipped), Is.SameAs(white), "始めは元の色");
                Assert.That(_game.View.WidthAt(flipped), Is.EqualTo(1f).Within(.001f));
                Assert.That(_game.View.HeightAt(S("d3")), Is.LessThan(1f), "置いた石は小さく現れる");

                StepUntil(() => Game.PhaseRemaining <= duration * 3 / 4);
                _game.Render();
                Assert.That(_game.View.SpriteAt(flipped), Is.SameAs(white), "前半は元の色");
                Assert.That(_game.View.WidthAt(flipped), Is.InRange(.01f, .99f), "前半は細くなる");

                StepUntil(() => Game.PhaseRemaining <= duration / 4);
                _game.Render();
                Assert.That(_game.View.SpriteAt(flipped), Is.SameAs(black), "後半は新しい色");
                Assert.That(_game.View.WidthAt(flipped), Is.InRange(.01f, .99f), "後半は太くなる");

                StepUntil(() => Game.Phase != GamePhase.Placing);
                AssertViewMatchesPosition();
            }
            finally
            {
                Time.timeScale = 1;
            }
        }

        [UnityTest]
        public IEnumerator ClicksDuringTheCpuTurnAreIgnored()
        {
            Flow.SelectSide(Side.White);
            yield return Begin();
            Assert.That(Flow.IsCpuTurn, Is.True);
            yield return Click(S("c5"));
            Assert.That(Flow.LastVerdict, Is.Null, "CPU の手番のクリックは Core へ渡さない");
            yield return WaitUntil(() => Flow.IsPlayerTurn);
            Assert.That(Game.MovesPlayed, Is.EqualTo(1), "持ち越した操作で打っていない");
        }

        [UnityTest]
        public IEnumerator Pass_IsAnnouncedAndTheTurnReturns()
        {
            yield return Begin(_ => new OthelloGame(Flow.Config, DemoGames.PassSoon()));
            yield return Click(DemoGames.PassMove);
            yield return WaitUntil(() => Game.Phase == GamePhase.Passing);
            yield return null;
            Assert.That(Text("status"), Does.Contain("CPU（白）").And.Contain("パス"));
            yield return WaitUntil(() => Flow.IsPlayerTurn);
            Assert.That(Game.Passes, Is.EqualTo(1));
            Assert.That(Game.ToMove, Is.EqualTo(Side.Black));
        }

        [UnityTest]
        public IEnumerator Result_ShowsTheWinAndEnterReplays()
        {
            yield return Begin(_ => new OthelloGame(Flow.Config, DemoGames.Endgame()));
            yield return PlayToTheEndWithClicks();
            Assert.That(Text("headline"), Is.EqualTo("あなたの勝ち！"));
            Assert.That(Text("message"), Does.Contain($"黒 {Game.Count(Side.Black)}").And.Contain("あなたは黒"));
            Assert.That(IsShown("overlay"), Is.True);
            Assert.That(IsShown("options"), Is.True);
            var finalPosition = Game.Position;
            var seed = Flow.MatchSeed;

            yield return Click(S("a1"));
            Assert.That(Game.Position, Is.EqualTo(finalPosition), "結果画面では盤を操作できない");

            // 次の試合の色を選んでも、終わった試合の表示は変わらない。
            yield return ClickButton("side-white");
            Assert.That(Text("headline"), Is.EqualTo("あなたの勝ち！"));
            Assert.That(Text("message"), Does.Contain("あなたは黒"));

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Flow.MatchSeed, Is.Not.EqualTo(seed));
            Assert.That(Flow.MatchPlayerSide, Is.EqualTo(Side.White));
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.T);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [UnityTest]
        public IEnumerator Result_ShowsTheLossWhenTheCpuHasMoreDiscs()
        {
            Flow.SelectSide(Side.White);
            yield return Begin(_ => new OthelloGame(Flow.Config, DemoGames.Endgame()));
            yield return PlayToTheEndWithClicks();
            Assert.That(Text("headline"), Is.EqualTo("CPU の勝ち"));
            Assert.That(Text("message"), Does.Contain("あなたは白"));
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
        public IEnumerator EscapePausesAndStopsTime()
        {
            yield return Begin();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused));
            var ticks = Game.TickCount;
            yield return Click(S("d3"));
            Assert.That(Game.MovesPlayed, Is.EqualTo(0), "一時停止中は打てない");
            Assert.That(Game.TickCount, Is.EqualTo(ticks));
            Assert.That(IsShown("restart"), Is.True);
            Assert.That(IsShown("title"), Is.True);
            Assert.That(IsShown("options"), Is.False);

            yield return _keyboard.Tap(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
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
