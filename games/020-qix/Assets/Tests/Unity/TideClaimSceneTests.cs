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
    public sealed class TideClaimSceneTests
    {
        private const long TickLimit = 60L * 60 * 20;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private TideClaimGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("TideClaim");
            yield return null;
            _game = Object.FindAnyObjectByType<TideClaimGame>();
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

        private QixGame Game => _game.Flow.Game;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q<Button>(name).style.display.value == DisplayStyle.Flex;

        private IEnumerator StartMatch()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        // バッチ実行はフレームが極端に短いので、フレーム数ではなく実時間の上限で待つ。
        private static IEnumerator WaitUntil(Func<bool> condition, float seconds = 5f)
        {
            var limit = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < limit) { yield return null; }
            Assert.That(condition(), Is.True, "時間内に条件を満たさなかった");
        }

        private static IEnumerator WaitSeconds(float seconds)
        {
            var limit = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < limit) { yield return null; }
        }

        // 実時間で動く試合の入力を確かめる前に、渦が自機の近く（下の縁の中央）から離れた試合にする。
        private void RestartUntilEddyIsFar()
        {
            for (var i = 0; i < 100; i++)
            {
                var dx = (Game.Enemy.X / (float)QixConfig.SubCell) - Game.PlayerX;
                var dy = Game.Enemy.Y / (float)QixConfig.SubCell;
                if ((dx * dx) + (dy * dy) > 30 * 30) { return; }
                _game.Flow.Apply(Intent.Of(IntentKind.TogglePause));
                _game.Flow.Apply(Intent.Of(IntentKind.Restart));
            }

            Assert.Fail("渦が離れた試合を作れなかった");
        }

        private void StepUntil(Func<bool> condition)
        {
            var start = Game.Ticks;
            while (!condition())
            {
                Assert.That(Game.Ticks - start, Is.LessThan(TickLimit));
                _game.Flow.StepTick();
            }
        }

        [UnityTest]
        public IEnumerator Title_ShowsInitialFieldAndInstructionsWithoutAGame()
        {
            yield return null;

            Assert.That(Game, Is.Null);
            Assert.That(Text("headline"), Is.EqualTo("TIDE CLAIM"));
            Assert.That(Text("message"), Does.Contain("Space"));
            Assert.That(_game.HudRoot.Q("stats").style.display.value, Is.EqualTo(DisplayStyle.None));
            Assert.That(_game.View.Player.enabled, Is.False);
            Assert.That(_game.View.Enemy.enabled, Is.False);
            Assert.That(_game.View.PixelAt(0, 0), Is.EqualTo(TideClaimPalette.Rim));
            Assert.That(_game.View.PixelAt(40, 28), Is.EqualTo(TideClaimPalette.InitialColor(40, 28, 80, 56)));
        }

        [UnityTest]
        public IEnumerator Enter_StartsWithPlayerOnBottomRim()
        {
            yield return StartMatch();
            yield return null;

            Assert.That((Game.PlayerX, Game.PlayerY), Is.EqualTo((40, 0)));
            Assert.That(Vector3.Distance(_game.View.Player.transform.position, _game.View.CellWorld(40, 0)), Is.LessThan(.001f));
            Assert.That(_game.View.Enemy.enabled, Is.True);
            Assert.That(Text("ratio"), Does.StartWith("陸  0.0%"));
            Assert.That(Text("lives"), Is.EqualTo("残機  ●●●"));
        }

        [UnityTest]
        public IEnumerator HoldingArrow_WalksAlongRimAndStopsOnRelease()
        {
            yield return StartMatch();

            _keyboard.Hold(Key.LeftArrow);
            yield return WaitUntil(() => Game.PlayerX <= 36);
            _keyboard.Hold();
            yield return null;
            var x = Game.PlayerX;
            yield return WaitSeconds(.3f);

            Assert.That(Game.PlayerX, Is.EqualTo(x), "離すと止まる");
            Assert.That(Game.PlayerY, Is.Zero);
            Assert.That(Game.IsDrawing, Is.False);

            _keyboard.Hold(Key.UpArrow);
            yield return WaitSeconds(.3f);
            _keyboard.Hold();
            Assert.That(Game.PlayerY, Is.Zero, "Space なしでは海へ出ない");
        }

        [UnityTest]
        public IEnumerator HoldingSpaceAndUp_DrawsLineAndStopsWhenArrowReleased()
        {
            yield return StartMatch();
            RestartUntilEddyIsFar();

            _keyboard.Hold(Key.Space, Key.UpArrow);
            yield return WaitUntil(() => Game.Trail.Count >= 4);
            _keyboard.Hold(Key.Space);
            yield return null;
            var y = Game.PlayerY;
            yield return WaitSeconds(.2f);

            Assert.That(Game.IsDrawing, Is.True);
            Assert.That(Game.PlayerY, Is.EqualTo(y), "向きを離すとその場で止まる");
            Assert.That(_game.View.PixelAt(40, 2), Is.EqualTo(TideClaimPalette.Trail));
            Assert.That(_game.View.Player.color, Is.Not.EqualTo(Color.white), "描いている間は自機の色が変わる");
            Assert.That(Text("ratio"), Does.StartWith("陸  0.0%"), "閉じるまで占有率は増えない");

            _keyboard.Hold(Key.DownArrow, Key.Space);
            yield return WaitSeconds(.2f);
            _keyboard.Hold();
            Assert.That(Game.PlayerY, Is.EqualTo(y), "自分の線へは戻れない");
        }

        [UnityTest]
        public IEnumerator Closing_PaintsEveryCellAndUpdatesHud()
        {
            yield return StartMatch();
            Time.timeScale = 0;
            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => Game.ClaimedCells > 0);
            yield return null;

            for (var y = 0; y < Game.Height; y++)
            {
                for (var x = 0; x < Game.Width; x++)
                {
                    var cell = Game.CellAt(x, y);
                    var expected = TideClaimPalette.CellColor(cell, cell == Cell.Claimed && Game.IsBoundary(x, y), x, y);
                    Assert.That(_game.View.PixelAt(x, y), Is.EqualTo(expected), $"({x}, {y})");
                }
            }

            Assert.That(Text("ratio"), Does.StartWith("陸  " + TideClaimGame.Percent(Game)));
            Assert.That(Text("score"), Does.Contain(Game.Score.ToString()));
            Assert.That(_game.HudRoot.Q("ratioFill").style.width.value.value, Is.GreaterThan(0));

            var uploads = _game.View.Uploads;
            yield return null;
            yield return null;
            Assert.That(_game.View.Uploads, Is.EqualTo(uploads), "盤が変わらなければ書き直さない");
        }

        /// <summary>
        /// 線を閉じた Tick（Flood Fill を含む）と、その直後の描画（盤全体のテクスチャの書き直し）の時間。
        /// 自動操縦で 20 試合を勝つまで進め、閉じた Tick だけを集める。閉じない Tick は計測に含めない。
        /// </summary>
        [UnityTest]
        public IEnumerator Perf_CloseTickAndRepaint()
        {
            yield return StartMatch();
            Time.timeScale = 0;
            var ticks = new System.Collections.Generic.List<double>();
            var paints = new System.Collections.Generic.List<double>();
            var watch = new System.Diagnostics.Stopwatch();
            for (var match = 0; match < 20; match++)
            {
                // 自動操縦の計画の時間を含めないよう、入力は計測の外で渡し、試合の Tick だけを測る。
                var pilot = new Autopilot();
                while (!Game.IsOver)
                {
                    var claimed = Game.ClaimedCells;
                    var (direction, draw) = pilot.Next(Game);
                    Game.SetInput(direction, draw);
                    watch.Restart();
                    Game.Tick();
                    var tick = watch.Elapsed.TotalMilliseconds;
                    if (Game.ClaimedCells == claimed) { continue; }
                    var uploads = _game.View.Uploads;
                    watch.Restart();
                    _game.Render();
                    paints.Add(watch.Elapsed.TotalMilliseconds);
                    ticks.Add(tick);
                    Assert.That(_game.View.Uploads, Is.EqualTo(uploads + 1), "計測した描画が盤を書き直している");
                }

                Assert.That(Game.Status, Is.EqualTo(GameStatus.Won));
                _game.Flow.StepTick();
                Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
                _game.Flow.Apply(Intent.Of(IntentKind.Restart));
            }

            ticks.Sort();
            paints.Sort();
            static double At(System.Collections.Generic.List<double> values, double q) => values[(int)Math.Min(values.Count - 1, Math.Floor(q * values.Count))];
            Debug.Log($"[Perf] 閉じた回数 {ticks.Count}。Tick p50 {At(ticks, .5):F3}ms p95 {At(ticks, .95):F3}ms 最大 {ticks[ticks.Count - 1]:F3}ms。"
                + $"描画 p50 {At(paints, .5):F3}ms p95 {At(paints, .95):F3}ms 最大 {paints[paints.Count - 1]:F3}ms");
            Assert.That(At(ticks, .95) + At(paints, .95), Is.LessThan(16), "閉じる処理と描画は 1 フレームに収まる");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Pause_StopsTimeAndKeysHeldDuringPauseDoNotMove()
        {
            yield return StartMatch();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            var ticks = Game.Ticks;

            _keyboard.Hold(Key.LeftArrow);
            yield return WaitSeconds(.2f);
            Assert.That(Game.Ticks, Is.EqualTo(ticks));

            _keyboard.Hold(Key.LeftArrow, Key.Enter);
            yield return null;
            _keyboard.Hold(Key.LeftArrow);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return WaitSeconds(.2f);
            Assert.That(Game.PlayerX, Is.EqualTo(40), "一時停止中から押していたキーでは動かない");

            _keyboard.Hold();
            yield return null;
            _keyboard.Hold(Key.LeftArrow);
            yield return WaitUntil(() => Game.PlayerX < 40);
            _keyboard.Hold();
        }

        [UnityTest]
        public IEnumerator Win_ShowsResultAndEnterStartsANewSeed()
        {
            yield return StartMatch();
            Time.timeScale = 0;
            _game.Flow.Pilot = new Autopilot();
            var seed = Game.Seed;
            StepUntil(() => Game.IsOver);
            yield return null;

            Assert.That(Game.Status, Is.EqualTo(GameStatus.Won));
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(Text("headline"), Is.EqualTo("埋め立て完了"));
            Assert.That(Text("message"), Does.Contain(TideClaimGame.Percent(Game)));

            _game.Flow.Pilot = null;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(Game.Seed, Is.EqualTo(seed + 1));
            Assert.That(Game.ClaimedCells, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Loss_ShowsResult()
        {
            yield return StartMatch();
            Time.timeScale = 0;
            var flow = _game.Flow;
            while (!Game.IsOver)
            {
                var lives = Game.Lives;
                flow.Apply(Intent.Down(Control.Draw));
                flow.Apply(Intent.Down(Control.Up));
                StepUntil(() => Game.Trail.Count >= 10 || Game.Lives < lives);
                flow.Apply(Intent.Up(Control.Up));
                StepUntil(() => Game.Lives < lives);
                flow.Apply(Intent.Up(Control.Draw));
            }

            yield return null;
            Assert.That(Game.Status, Is.EqualTo(GameStatus.Lost));
            Assert.That(Text("headline"), Is.EqualTo("潮に負けた"));
            Assert.That(Text("lives"), Is.EqualTo("残機  ○○○"));
            AssertButtons("submit", "title");
        }

        [UnityTest]
        public IEnumerator Buttons_DriveTheWholeLoop()
        {
            yield return ClickButton("submit");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            var seed = Game.Seed;

            yield return ClickButton("pause");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));

            yield return ClickButton("restart");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Game.Seed, Is.EqualTo(seed + 1));

            yield return ClickButton("pause");
            yield return ClickButton("title");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(Game, Is.Null);
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
            AssertButtons("submit", "restart", "title");
        }

        private void AssertButtons(params string[] shown)
        {
            foreach (var name in new[] { "submit", "pause", "restart", "title", "quit" })
            {
                Assert.That(IsShown(name), Is.EqualTo(Array.IndexOf(shown, name) >= 0), name);
            }
        }

        [UnityTest]
        public IEnumerator Field_FitsOnScreenAndPanelDoesNotCoverIt()
        {
            yield return StartMatch();
            yield return null;
            yield return null;

            var bounds = _game.View.FieldBounds;
            var min = _game.ViewCamera.WorldToViewportPoint(bounds.min);
            var max = _game.ViewCamera.WorldToViewportPoint(bounds.max);
            Assert.That(min.x, Is.GreaterThan(0));
            Assert.That(min.y, Is.GreaterThan(0));
            Assert.That(max.x, Is.LessThan(1));
            Assert.That(max.y, Is.LessThan(1));

            var right = _game.ViewCamera.WorldToScreenPoint(bounds.max);
            var panel = _game.HudRoot.panel;
            var fieldRight = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(right.x, Screen.height - right.y)).x;
            Assert.That(_game.HudRoot.Q<VisualElement>("panel").worldBound.xMin, Is.GreaterThan(fieldRight));
        }

        [UnityTest]
        public IEnumerator FocusLoss_PausesAndHeldKeyMustBePressedAgain()
        {
            yield return StartMatch();
            _keyboard.Hold(Key.LeftArrow);
            yield return WaitUntil(() => Game.PlayerX < 40);

            _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");

            _keyboard.Hold(Key.LeftArrow, Key.Enter);
            yield return null;
            _keyboard.Hold(Key.LeftArrow);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing), "キーボードでは再開できる");
            var x = Game.PlayerX;
            yield return WaitSeconds(.2f);
            Assert.That(Game.PlayerX, Is.EqualTo(x), "フォーカスを失う前から押していたキーでは動かない");
            _keyboard.Hold();
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

        [Test]
        public void Settings_RejectInvalidValues()
        {
            AssertRejected(s => s.Volume = float.NaN);
            AssertRejected(s => s.Volume = 1.5f);
            AssertRejected(s => s.Rules.Width = 60);
            AssertRejected(s => s.Rules.Height = 40);
            AssertRejected(s => s.Rules.TargetPercent = 100);
            AssertRejected(s => s.Rules.EnemySpeed = 0);
        }

        private static void AssertRejected(Action<TideClaimSettings> change)
        {
            var settings = ScriptableObject.CreateInstance<TideClaimSettings>();
            Assert.DoesNotThrow(settings.Validate);
            change(settings);
            Assert.Throws<ArgumentOutOfRangeException>(settings.Validate);
            Object.DestroyImmediate(settings);
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
    }
}
