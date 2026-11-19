using System;
using System.Collections;
using Game.App;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    public sealed class SporeBloomSceneTests
    {
        private const float WaitLimitSeconds = 10f;

        // 長押しの開始遅延（10 Tick）より長く待つ。
        private const float ReleaseSettleSeconds = .3f;

        private TestKeyboard _keyboard;
        private SporeBloomGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            yield return SceneManager.LoadSceneAsync("SporeBloom");
            yield return null;
            _game = Object.FindAnyObjectByType<SporeBloomGame>();
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            _keyboard.Dispose();
            yield return null;
        }

        private VersusFlow Flow => _game.Flow;

        private PlayerField Player => Flow.Match.Player;

        private ActivePair Active => Player.Active.Value;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q(name).style.display.value == DisplayStyle.Flex;

        private IEnumerator Begin()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        private IEnumerator WaitUntil(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartup + WaitLimitSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) { yield return null; }
            Assert.That(condition(), Is.True, "待ち時間の上限を超えました。");
        }

        private void SetFocus(bool focus) => _game.SendMessage("OnApplicationFocus", focus);

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

        private void AssertViewMatches(SporeFieldView view, PlayerField field)
        {
            _game.Render();
            var shown = 0;
            for (var y = 0; y < Board.VisibleHeight; y++)
            {
                for (var x = 0; x < Board.Width; x++)
                {
                    var cell = field.Board.At(x, y);
                    if (cell != Cell.Empty) { shown++; }
                    var expected = cell != Cell.Empty ? view.SporeSprite(cell) : null;
                    Assert.That(view.SpriteAt(x, y), Is.SameAs(expected), $"({x}, {y})");
                }
            }

            Assert.That(view.ShownCells, Is.EqualTo(shown));
            if (field.Active is not ActivePair pair) { return; }
            Assert.That((view.ActivePosition(0) - view.CellPosition(pair.X, pair.Y)).magnitude, Is.LessThan(.001f), "軸");
            Assert.That((view.ActivePosition(1) - view.CellPosition(pair.ChildX, pair.ChildY)).magnitude, Is.LessThan(.001f), "子");
        }

        [Test]
        public void SettingsAssetIsValid()
        {
            Assert.DoesNotThrow(_game.Settings.Validate);
        }

        [UnityTest]
        public IEnumerator Title_ShowsNameAndControls()
        {
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(Text("headline"), Is.EqualTo("SPORE BLOOM"));
            Assert.That(Text("message"), Does.Contain("回転"));
            Assert.That(IsShown("overlay"), Is.True);
            Assert.That(IsShown("quit"), Is.True);
            Assert.That(_game.View.Player.ShownActive, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator Enter_StartsTheMatchOnBothFields()
        {
            yield return Begin();
            yield return null;
            Assert.That(IsShown("overlay"), Is.False);
            Assert.That(IsShown("stats"), Is.True);
            Assert.That(_game.View.Player.ShownNext, Is.EqualTo(SporeFieldView.PreviewPairs * 2));
            Assert.That(_game.View.Cpu.ShownNext, Is.EqualTo(SporeFieldView.PreviewPairs * 2));
        }

        [UnityTest]
        public IEnumerator ArrowKeys_MoveThePair()
        {
            yield return Begin();
            var startX = Active.X;
            yield return _keyboard.Tap(Key.LeftArrow);
            Assert.That(Active.X, Is.EqualTo(startX - 1));
            yield return _keyboard.Tap(Key.D);
            Assert.That(Active.X, Is.EqualTo(startX));
            AssertViewMatches(_game.View.Player, Player);
        }

        [UnityTest]
        public IEnumerator HeldKey_RepeatsAndStopsWhenReleased()
        {
            yield return Begin();
            yield return _keyboard.Tap(Key.RightArrow);
            yield return _keyboard.Tap(Key.RightArrow);
            var startX = Active.X;

            _keyboard.Hold(Key.LeftArrow);
            yield return WaitUntil(() => Active.X <= startX - 2);

            _keyboard.Hold();
            yield return null;
            yield return null;
            var releasedX = Active.X;
            var settle = Time.realtimeSinceStartup + ReleaseSettleSeconds;
            while (Time.realtimeSinceStartup < settle) { yield return null; }
            Assert.That(Active.X, Is.EqualTo(releasedX));
        }

        [UnityTest]
        public IEnumerator RotateKeys_TurnThePairBothWays()
        {
            yield return Begin();
            yield return _keyboard.Tap(Key.UpArrow);
            Assert.That(Active.Orientation, Is.EqualTo(PairOrientation.Right));
            yield return _keyboard.Tap(Key.Z);
            Assert.That(Active.Orientation, Is.EqualTo(PairOrientation.Up));
            yield return _keyboard.Tap(Key.X);
            Assert.That(Active.Orientation, Is.EqualTo(PairOrientation.Right));
        }

        [UnityTest]
        public IEnumerator Time_MakesThePairFall()
        {
            yield return Begin();
            var startY = Active.Y;
            yield return WaitUntil(() => Active.Y < startY);
        }

        [UnityTest]
        public IEnumerator DownKey_DropsAndLocksThePair()
        {
            yield return Begin();
            _keyboard.Hold(Key.DownArrow);
            yield return WaitUntil(() => Player.PairsSpawned == 2 && Player.Phase == FieldPhase.Falling);
            _keyboard.Hold();
            yield return null;
            AssertViewMatches(_game.View.Player, Player);
            Assert.That(_game.View.Player.ShownCells, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Keys_BuildAnIntentionalTwoChainAndAttackTheCpu()
        {
            Flow.MatchFactory = seed => DemoMatches.TwoChain(_game.Settings.Rules, seed);
            yield return Begin();
            yield return _keyboard.Tap(Key.LeftArrow);
            _keyboard.Hold(Key.DownArrow);
            yield return WaitUntil(() => Player.Chain == 2);
            Assert.That(Text("player-chain"), Does.Contain("2"));
            _keyboard.Hold();

            yield return WaitUntil(() => Player.Phase == FieldPhase.Falling);
            Assert.That(Player.MaxChain, Is.EqualTo(2));
            Assert.That(Player.Score, Is.EqualTo(200));
            AssertViewMatches(_game.View.Player, Player);
            Assert.That(Text("player-chain"), Is.Empty, "次のペアを操作し始めたら連鎖の表示を消す");
        }

        [UnityTest]
        public IEnumerator Overlay_HidesTheMatchHudAroundThePanel()
        {
            yield return Begin();
            yield return null;
            Assert.That(_game.View.Player.ShownNext, Is.EqualTo(SporeFieldView.PreviewPairs * 2));
            Assert.That(IsShown("controls"), Is.True);

            yield return _keyboard.Tap(Key.Escape);
            yield return null;
            Assert.That(IsShown("stats"), Is.False);
            Assert.That(IsShown("controls"), Is.False);
            Assert.That(_game.View.Player.ShownNext, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator PendingGarbage_IsShownOnTheHud()
        {
            yield return Begin();
            Flow.Match.Player.ReceiveGarbage(3);
            yield return null;
            Assert.That(Text("player-pending"), Does.Contain("3"));
        }

        [UnityTest]
        public IEnumerator Escape_PausesAndEnterResumes()
        {
            yield return Begin();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(Text("headline"), Is.EqualTo("一時停止"));
            Assert.That(IsShown("restart"), Is.True);

            var ticks = Player.TickCount;
            yield return null;
            yield return null;
            Assert.That(Player.TickCount, Is.EqualTo(ticks));

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [UnityTest]
        public IEnumerator PauseScreen_RestartsAndReturnsToTitle()
        {
            yield return Begin();
            var seed = Flow.MatchSeed;
            yield return _keyboard.Tap(Key.P);
            yield return _keyboard.Tap(Key.R);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Flow.MatchSeed, Is.Not.EqualTo(seed));

            yield return _keyboard.Tap(Key.P);
            yield return _keyboard.Tap(Key.T);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            Assert.That(_game.View.Player.ShownCells, Is.EqualTo(0));
            Assert.That(_game.View.Player.ShownNext, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator CpuTopOut_ShowsTheWinScreen()
        {
            Flow.MatchFactory = seed => DemoMatches.CpuAboutToLose(_game.Settings.Rules, seed);
            yield return Begin();
            Flow.Match.Cpu.Apply(GameCommand.SoftDropOn);
            yield return WaitUntil(() => Flow.Screen == AppScreen.Result);
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("あなたの勝ち"));
            Assert.That(IsShown("overlay"), Is.True);
        }

        [UnityTest]
        public IEnumerator FullCycle_LoseResultAndReplay()
        {
            Flow.MatchFactory = seed => DemoMatches.PlayerAboutToLose(_game.Settings.Rules, seed);
            yield return Begin();
            var seed = Flow.MatchSeed;
            _keyboard.Hold(Key.DownArrow);
            yield return WaitUntil(() => Flow.Screen == AppScreen.Result);
            _keyboard.Hold();
            yield return null;
            Assert.That(Text("headline"), Is.EqualTo("CPU の勝ち"));
            Assert.That(Text("message"), Does.Contain("連鎖"));

            yield return _keyboard.Tap(Key.LeftArrow);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Result), "結果画面の操作は試合に届かない");

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Flow.MatchSeed, Is.Not.EqualTo(seed));
            yield return null;
            Assert.That(IsShown("overlay"), Is.False);
        }

        [UnityTest]
        public IEnumerator FocusLoss_PausesAndStaysPausedAfterReturning()
        {
            yield return Begin();
            SetFocus(false);
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused));

            SetFocus(true);
            yield return null;
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused));
        }

        [UnityTest]
        public IEnumerator FocusLoss_DropsAPauseKeyPressedInTheSameFrame()
        {
            yield return Begin();
            _keyboard.Hold(Key.Escape);
            yield return null;
            SetFocus(false);
            _keyboard.Hold();
            yield return null;
            SetFocus(true);
            yield return null;
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Paused));
        }

        [UnityTest]
        public IEnumerator FocusReturn_IgnoresTheClickThatSelectedTheWindow()
        {
            var quits = 0;
            _game.QuitHandler = () => quits++;
            yield return null;
            yield return null;
            var quit = _game.HudRoot.Q<Button>("quit");

            SetFocus(false);
            yield return null;
            SetFocus(true);
            SendClick(quit);
            yield return null;
            yield return null;
            Assert.That(quits, Is.EqualTo(0));

            SendClick(quit);
            yield return null;
            yield return null;
            Assert.That(quits, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator QuitButton_IsHiddenOutsideOfTheTitle()
        {
            yield return Begin();
            yield return null;
            Assert.That(IsShown("quit"), Is.False);

            Flow.TogglePause();
            yield return null;
            Assert.That(IsShown("quit"), Is.False);
        }

        [UnityTest]
        public IEnumerator Buttons_FollowTheSameRulesAsTheKeys()
        {
            yield return null;
            yield return null;
            SendClick(_game.HudRoot.Q<Button>("submit"));
            yield return null;
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Playing));

            Flow.TogglePause();
            yield return null;
            yield return null;
            yield return null;
            SendClick(_game.HudRoot.Q<Button>("title"));
            yield return null;
            yield return null;
            Assert.That(Flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [UnityTest]
        public IEnumerator View_FollowsALongerMatch()
        {
            yield return Begin();
            for (var i = 0; i < 12 && Flow.Screen == AppScreen.Playing; i++)
            {
                for (var step = 0; step < 2000 && Flow.Screen == AppScreen.Playing && Player.Phase != FieldPhase.Falling; step++) { Flow.StepTick(); }
                if (Flow.Screen != AppScreen.Playing) { yield break; }
                AssertViewMatches(_game.View.Player, Player);

                var pair = Player.PairsSpawned;
                foreach (var command in PairPlanner.Plan(_game.Settings.Rules, Player)) { Flow.Perform(command); }
                for (var step = 0; step < 2000 && Flow.Screen == AppScreen.Playing && Player.PairsSpawned == pair; step++) { Flow.StepTick(); }
                yield return null;
            }
        }
    }
}
