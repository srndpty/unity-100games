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
    public sealed class GlintAscentSceneTests
    {
        private const int TickLimit = 60 * 60 * 10;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private GlintAscentGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("GlintAscent");
            yield return null;
            _game = Object.FindAnyObjectByType<GlintAscentGame>();
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

        private Climb Climb => _game.Flow.Climb;

        private RoomRun Run => Climb.Run;

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        private bool IsShown(string name) => _game.HudRoot.Q<Button>(name).style.display.value == DisplayStyle.Flex;

        private IEnumerator StartClimb()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));

            // 開始の Tick より前に押したジャンプ・ダッシュは「押したままのキー」として扱われるので、1 Tick 進むのを待つ。
            yield return WaitUntil(() => Climb.ElapsedTicks > 0);
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

        /// <summary>時計を止め（timeScale 0）、Tick を直接進める。</summary>
        private void StepUntil(Func<bool> done)
        {
            Time.timeScale = 0;
            for (var i = 0; i < TickLimit && !done(); i++)
            {
                _game.Flow.StepTick();
                _game.Render();
            }

            Assert.That(done(), Is.True, "Tick の上限までに条件を満たさなかった");
        }

        [UnityTest]
        public IEnumerator Stages_MatchCoreRoomsAndOnlyFirstIsShownAtTitle()
        {
            yield return null;
            var rooms = Rooms.All();
            Assert.That(Object.FindObjectsByType<GlintAscentStage>(FindObjectsInactive.Include), Has.Length.EqualTo(rooms.Count), "Stage のインスタンスは部屋ごとに 1 つ");
            for (var i = 0; i < rooms.Count; i++)
            {
                var stage = _game.StageAt(i);
                Assert.That(stage.RoomIndex, Is.EqualTo(i));
                Assert.That(stage.Mismatch(rooms[i]), Is.Null, rooms[i].Name);
                Assert.That(stage.gameObject.activeSelf, Is.EqualTo(i == 0));
            }

            Assert.That(Text("headline"), Is.EqualTo("GLINT ASCENT"));
            Assert.That(IsShown("submit") && IsShown("quit"), Is.True);
            Assert.That(_game.HudRoot.Q("stats").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator Camera_ShowsTheWholeRoomBelowTheHudBand()
        {
            yield return null;
            var camera = _game.ViewCamera;
            var stage = _game.StageAt(0);
            var bottomLeft = camera.WorldToViewportPoint(stage.transform.TransformPoint(Vector3.zero));
            var topRight = camera.WorldToViewportPoint(stage.transform.TransformPoint(new Vector3(Room.Columns, Room.Rows + GlintAscentGame.HudBandTiles, 0)));
            Assert.That(bottomLeft.x, Is.GreaterThanOrEqualTo(-1e-4f));
            Assert.That(bottomLeft.y, Is.GreaterThanOrEqualTo(-1e-4f));
            Assert.That(topRight.x, Is.LessThanOrEqualTo(1 + 1e-4f));
            Assert.That(topRight.y, Is.LessThanOrEqualTo(1 + 1e-4f));
        }

        [UnityTest]
        public IEnumerator HoldingRightKey_MovesPlayerAndReleaseStops()
        {
            yield return StartClimb();
            var x = Run.Player.Body.X;
            _keyboard.Hold(Key.RightArrow);
            yield return WaitUntil(() => Run.Player.Body.X > x + Units.Tile);
            _keyboard.Hold();
            yield return WaitUntil(() => Run.Player.VelocityX == 0 && Run.Player.Grounded);
            var stopped = Run.Player.Body.X;
            yield return WaitSeconds(.2f);
            Assert.That(Run.Player.Body.X, Is.EqualTo(stopped), "離した後は止まったまま");
        }

        [UnityTest]
        public IEnumerator JumpKey_LeavesTheGround_HoldingDoesNotJumpAgain()
        {
            yield return StartClimb();
            var y = Run.Player.Body.Y;
            _keyboard.Hold(Key.Space);
            yield return WaitUntil(() => Run.Player.Body.Y > y + Units.Tile);
            yield return WaitUntil(() => Run.Player.Grounded);
            yield return WaitSeconds(.3f);
            Assert.That(Run.Player.Grounded, Is.True, "押したまま着地しても跳び直さない");
            _keyboard.Hold();
        }

        [UnityTest]
        public IEnumerator DashKey_DashesOnceAndTintsUntilLanding()
        {
            yield return StartClimb();
            _keyboard.Hold(Key.Space);
            yield return WaitUntil(() => !Run.Player.Grounded);
            _keyboard.Hold(Key.Space, Key.UpArrow, Key.X);
            yield return WaitUntil(() => Run.Player.DashesLeft == 0);
            var stage = _game.CurrentStage;
            yield return WaitUntil(() => !Run.Player.IsDashing);
            yield return null;
            Assert.That(stage.Player.color, Is.EqualTo(stage.SpentTint), "使い切っている間は色を変える");
            _keyboard.Hold();
            yield return WaitUntil(() => Run.Player.Grounded);
            yield return null;
            Assert.That(Run.Player.DashesLeft, Is.EqualTo(_game.Settings.Rules.DashCount));
            Assert.That(stage.Player.color, Is.EqualTo(Color.white), "着地で戻る");
        }

        [UnityTest]
        public IEnumerator InputEvents_AreTimestampedOnTheGameClock()
        {
            // 入力の時刻（CallbackContext.time）が、Flow に渡す時計（GlintAscentGame.Now）と同じ時計であることを確かめる。
            var action = new InputAction("probe", InputActionType.Button, "<Keyboard>/k");
            var times = new System.Collections.Generic.List<double>();
            action.started += context => times.Add(context.time);
            action.Enable();
            try
            {
                yield return null;
                var before = GlintAscentGame.Now;
                _keyboard.Hold(Key.K);
                yield return WaitUntil(() => times.Count > 0);
                var after = GlintAscentGame.Now;
                Assert.That(times[0], Is.InRange(before - .05, after + .05), "押下の時刻はゲームの時計の上にある");
                _keyboard.Hold();
            }
            finally
            {
                action.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator Death_ShowsBurstThenRespawnsWithDeathCount()
        {
            yield return StartClimb();
            // 1 部屋目は列どおりに抜け、2 部屋目で右へ歩き続けてトゲの谷に落ちる。
            _game.Flow.Pilot = new RoutePilot(new[] { Routes.All()[0], RouteScript.Parse("R600") });
            StepUntil(() => Run.Status == RunStatus.Dying);
            Assert.That(Climb.RoomIndex, Is.EqualTo(1));
            var stage = _game.CurrentStage;
            Assert.That(stage.Player.sprite, Is.SameAs(stage.DeadSprite));
            Assert.That(Text("deaths"), Is.EqualTo("死亡  1"));
            StepUntil(() => Run.Status == RunStatus.Playing);
            Assert.That(stage.Player.sprite, Is.Not.SameAs(stage.DeadSprite));
            Assert.That(stage.Player.transform.position, Is.EqualTo(stage.World(Run.Room.StartBox.CenterX, Run.Room.StartBox.Bottom)));
        }

        [UnityTest]
        public IEnumerator Pilot_ClimbsAllRooms_SwitchingStagesAndShowsResult()
        {
            yield return StartClimb();
            _game.Flow.Pilot = new RoutePilot(Routes.All());
            for (var room = 1; room < Rooms.All().Count; room++)
            {
                StepUntil(() => Climb.RoomIndex == room);
                Assert.That(_game.CurrentStage.RoomIndex, Is.EqualTo(room));
                Assert.That(_game.StageAt(room - 1).gameObject.activeSelf, Is.False);
                Assert.That(Text("room"), Does.StartWith($"{room + 1} / 10"));
            }

            StepUntil(() => _game.Flow.Screen == AppScreen.Result);
            Assert.That(Text("headline"), Is.EqualTo("登りきった！"));
            Assert.That(Text("message"), Does.Contain("死亡  0 回").And.Contain(GlintAscentGame.FormatTicks(_game.Flow.ClearTicks!.Value)));
            Assert.That(IsShown("submit") && IsShown("title"), Is.True);
        }

        [UnityTest]
        public IEnumerator BackToTitle_RestoresTheStartupView()
        {
            yield return null;
            var stage0 = _game.StageAt(0);
            var initial = (stage0.Player.transform.position, stage0.Player.sprite, stage0.Player.flipX, stage0.Player.color);

            yield return StartClimb();
            _game.Flow.Pilot = new RoutePilot(Routes.All());
            StepUntil(() => Climb.RoomIndex == 1 && Run.Player.IsDashing);
            Time.timeScale = 1;
            _game.Flow.Pilot = null;
            yield return _keyboard.Tap(Key.Escape);
            yield return _keyboard.Tap(Key.T);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            yield return null;
            Assert.That(stage0.gameObject.activeSelf, Is.True);
            Assert.That(_game.StageAt(1).gameObject.activeSelf, Is.False);
            Assert.That((stage0.Player.transform.position, stage0.Player.sprite, stage0.Player.flipX, stage0.Player.color), Is.EqualTo(initial), "起動直後と同じ表示");
        }

        [UnityTest]
        public IEnumerator PauseKey_StopsTimeAndShowsPanel()
        {
            yield return StartClimb();
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            var ticks = Climb.ElapsedTicks;
            yield return WaitSeconds(.2f);
            Assert.That(Climb.ElapsedTicks, Is.EqualTo(ticks));
            Assert.That(IsShown("restart") && IsShown("title"), Is.True);
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_game.HudRoot.Q("panel").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator HudButtons_StartPauseRestartAndBackToTitle()
        {
            yield return ClickButton("submit");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return ClickButton("pause");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return ClickButton("restart");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Climb.ElapsedTicks, Is.LessThan(30));
            yield return ClickButton("pause");
            yield return ClickButton("title");
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
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

        [UnityTest]
        public IEnumerator FocusLoss_PausesAndDropsHeldKeys()
        {
            yield return StartClimb();
            _keyboard.Hold(Key.RightArrow);
            yield return WaitUntil(() => Run.Player.VelocityX > 0);
            _game.SendMessage("OnApplicationFocus", false);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _keyboard.Hold();
            yield return null;
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            yield return null;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return WaitUntil(() => Run.Player.VelocityX == 0);
        }

        [UnityTest]
        public IEnumerator SettingsAsset_RoutesStillClearEveryRoom()
        {
            // 設定アセットを調整したら、撮影とテストの入力の列が合わなくなっていないかをここで知らせる。
            yield return null;
            var settings = _game.Settings;
            var climb = new Climb(settings.Rules, Rooms.All());
            var pilot = new RoutePilot(Routes.All());
            for (var i = 0; i < TickLimit && !climb.IsWon; i++) { climb.Tick(pilot.Next(climb)); }
            Assert.That(climb.IsWon && climb.Deaths == 0, Is.True, "設定アセットの値では Routes が通らない。App テストの RouteFinder で作り直す");
        }

        [Test]
        public void Settings_RejectInvalidValues()
        {
            AssertRejected(s => s.Volume = float.NaN);
            AssertRejected(s => s.Volume = 1.5f);
            AssertRejected(s => s.Rules.Gravity = 0);
            AssertRejected(s => s.Rules.CoyoteTicks = PrecisionConfig.MaxGraceTicks + 1);
            AssertRejected(s => s.Rules = null, typeof(ArgumentNullException));
        }

        [Test]
        public void FormatTicks_ShowsMinutesSecondsAndHundredths()
        {
            Assert.That(GlintAscentGame.FormatTicks(0), Is.EqualTo("0:00.00"));
            Assert.That(GlintAscentGame.FormatTicks(59), Is.EqualTo("0:00.98"));
            Assert.That(GlintAscentGame.FormatTicks(60 * 75 + 30), Is.EqualTo("1:15.50"));
        }

        private static void AssertRejected(Action<GlintAscentSettings> change, Type exception = null)
        {
            var settings = ScriptableObject.CreateInstance<GlintAscentSettings>();
            Assert.DoesNotThrow(settings.Validate);
            change(settings);
            Assert.Throws(exception ?? typeof(ArgumentOutOfRangeException), settings.Validate);
            Object.DestroyImmediate(settings);
        }

        private void SetMouse(Vector2 position, MouseButton? pressed = null)
        {
            var state = new MouseState { position = position };
            if (pressed.HasValue) { state = state.WithButton(pressed.Value); }
            InputSystem.QueueStateEvent(_mouse, state);
        }

        // 実際の座標でボタンに当たることを確かめ、マウスで押して離す。バッチ実行ではUIイベントが配送されないので、
        // マウスのクリックが届かなかったときだけボタンへ直接送る（GUI のエディタでは届くので、二重に押さない）。
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

            var delivered = false;
            void OnClicked() => delivered = true;
            button.clicked += OnClicked;
            try
            {
                SetMouse(point);
                yield return null;
                SetMouse(point, MouseButton.Left);
                yield return null;
                SetMouse(point);
                yield return null;
                if (!delivered) { SendButtonClick(name); }
                yield return null;
                yield return null;
            }
            finally
            {
                button.clicked -= OnClicked;
            }
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
