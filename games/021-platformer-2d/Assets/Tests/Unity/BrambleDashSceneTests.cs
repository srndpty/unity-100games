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
    public sealed class BrambleDashSceneTests
    {
        private const int TickLimit = 60 * 60 * 10;

        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private BrambleDashGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("BrambleDash");
            yield return null;
            _game = Object.FindAnyObjectByType<BrambleDashGame>();
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

        private Campaign Campaign => _game.Flow.Campaign;

        private LevelRun Run => Campaign.Run;

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

        private void StepUntil(Func<bool> done)
        {
            for (var i = 0; i < TickLimit && !done(); i++)
            {
                _game.Flow.StepTick();
                _game.Render();
            }

            Assert.That(done(), Is.True, "Tick の上限までに条件を満たさなかった");
        }

        [UnityTest]
        public IEnumerator Stages_MatchCoreLevelsAndOnlyFirstIsShownAtTitle()
        {
            yield return null;
            var levels = Levels.All();
            Assert.That(Object.FindObjectsByType<BrambleDashStage>(FindObjectsInactive.Include), Has.Length.EqualTo(levels.Length), "Stage のインスタンスは面ごとに 1 つ");
            for (var i = 0; i < levels.Length; i++)
            {
                var stage = _game.StageAt(i);
                Assert.That(stage.LevelIndex, Is.EqualTo(i));
                Assert.That(stage.Mismatch(levels[i]), Is.Null, levels[i].Name);
                Assert.That(stage.gameObject.activeSelf, Is.EqualTo(i == 0));
            }

            Assert.That(Text("headline"), Is.EqualTo("BRAMBLE DASH"));
            Assert.That(IsShown("submit") && IsShown("quit"), Is.True);
            Assert.That(_game.HudRoot.Q("stats").style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator HoldingRightKey_MovesPlayerAndReleaseStops()
        {
            yield return StartMatch();
            var start = Run.Player.Body.X;
            _keyboard.Hold(Key.RightArrow);
            yield return WaitUntil(() => Run.Player.Body.X > start + Units.Tile);
            Assert.That(Run.Player.Facing, Is.EqualTo(1));

            _keyboard.Hold();
            yield return WaitUntil(() => Run.Player.VelocityX == 0);
            var stopped = Run.Player.Body.X;
            yield return WaitSeconds(.2f);
            Assert.That(Run.Player.Body.X, Is.EqualTo(stopped), "離した後は止まったまま");
            Assert.That(_game.CurrentStage.Player.transform.position.x, Is.EqualTo(_game.CurrentStage.World(Run.Player.Body.CenterX, 0).x).Within(1e-4));
        }

        [UnityTest]
        public IEnumerator HoldingShift_RunsFaster()
        {
            yield return StartMatch();
            _keyboard.Hold(Key.RightArrow, Key.LeftShift);
            yield return WaitUntil(() => Run.Player.VelocityX == _game.Settings.Rules.RunSpeed);
            _keyboard.Hold(Key.RightArrow);
            yield return WaitUntil(() => Run.Player.VelocityX == _game.Settings.Rules.WalkSpeed);
            _keyboard.Hold();
        }

        [UnityTest]
        public IEnumerator JumpKey_HeldJumpsHigherThanTap()
        {
            yield return StartMatch();

            // 面の開始時に押していたジャンプでは跳ばないので、離した状態の Tick を先に通す。
            yield return WaitUntil(() => Run.Ticks > 2 && Run.Player.Grounded);
            var floor = Run.Player.Body.Bottom;

            // 押し続ける：着地するまでの最高点。
            _keyboard.Hold(Key.Space);
            yield return WaitUntil(() => !Run.Player.Grounded);
            var high = floor;
            while (!Run.Player.Grounded)
            {
                high = Math.Max(high, Run.Player.Body.Bottom);
                yield return null;
            }

            _keyboard.Hold();
            yield return WaitSeconds(.1f);

            // すぐ離す。
            yield return _keyboard.Tap(Key.Space);
            yield return WaitUntil(() => !Run.Player.Grounded);
            var low = floor;
            while (!Run.Player.Grounded)
            {
                low = Math.Max(low, Run.Player.Body.Bottom);
                yield return null;
            }

            Assert.That(high - floor, Is.GreaterThan(4 * Units.Tile));
            Assert.That(low - floor, Is.LessThan((high - floor) / 2), "短く押すと低い");
        }

        [UnityTest]
        public IEnumerator JumpKey_HeldDoesNotJumpAgainAfterLanding()
        {
            yield return StartMatch();
            yield return WaitUntil(() => Run.Ticks > 2 && Run.Player.Grounded);
            _keyboard.Hold(Key.Space);
            yield return WaitUntil(() => !Run.Player.Grounded);
            yield return WaitUntil(() => Run.Player.Grounded);
            yield return WaitSeconds(.3f);
            Assert.That(Run.Player.Grounded, Is.True, "押したままでは跳び直さない");
            _keyboard.Hold();
        }

        [UnityTest]
        public IEnumerator Pilot_PlaysThroughAllLevels_SwitchingStagesAndReplay()
        {
            yield return StartMatch();
            Time.timeScale = 0;
            _game.Flow.Pilot = new Autopilot();
            for (var level = 0; level < 3; level++)
            {
                StepUntil(() => Campaign.LevelIndex == level && _game.Flow.Screen == AppScreen.Playing);
                Assert.That(_game.CurrentStage.LevelIndex, Is.EqualTo(level));
                for (var i = 0; i < 3; i++) { Assert.That(_game.StageAt(i).gameObject.activeSelf, Is.EqualTo(i == level)); }
                Assert.That(Text("level"), Does.StartWith($"{level + 1} 面"));
                StepUntil(() => _game.Flow.Screen != AppScreen.Playing);
                if (level < 2)
                {
                    Assert.That(Text("headline"), Is.EqualTo("面クリア！"));
                }
            }

            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(Text("headline"), Is.EqualTo("ぜんぶ抜けた！"));
            Assert.That(Text("best"), Is.EqualTo($"最高  {Campaign.Score}"));
            Assert.That(IsShown("submit") && IsShown("title"), Is.True);

            _game.Flow.Pilot = null;
            Time.timeScale = 1;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(Campaign.LevelIndex, Is.Zero);
            yield return null;
            Assert.That(_game.StageAt(0).gameObject.activeSelf, Is.True);
            Assert.That(_game.StageAt(2).gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator Camera_FollowsPlayerAndStaysInsideLevel()
        {
            yield return StartMatch();
            Time.timeScale = 0;
            _game.Flow.Pilot = new Autopilot();
            var camera = _game.ViewCamera;
            var level = Levels.All()[0];
            var moved = false;
            for (var i = 0; i < TickLimit && _game.Flow.Screen == AppScreen.Playing; i++)
            {
                _game.Flow.StepTick();
                _game.Render();
                var halfWidth = camera.orthographicSize * camera.aspect;
                var x = camera.transform.position.x;
                Assert.That(x - halfWidth, Is.GreaterThanOrEqualTo(-1e-3f), "左端より外を映さない");
                Assert.That(x + halfWidth, Is.LessThanOrEqualTo(level.Map.Width + 1e-3f), "右端より外を映さない");
                var player = _game.CurrentStage.Player.transform.position.x;
                Assert.That(player, Is.InRange(x - halfWidth, x + halfWidth), "自機はいつも画面の中");
                Assert.That(camera.transform.position.y, Is.EqualTo(7.5f).Within(1e-4), "縦は動かない");
                moved |= x > halfWidth + 1;
            }

            Assert.That(moved, Is.True, "右へ進むとカメラも進む");
        }

        [UnityTest]
        public IEnumerator StompedEnemy_ShowsSquashedThenDisappears()
        {
            yield return StartMatch();
            Time.timeScale = 0;
            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => Run.Stomps > 0);
            var index = -1;
            for (var i = 0; i < Run.Enemies.Count; i++)
            {
                if (Run.Enemies[i].State == EnemyState.Stomped) { index = i; }
            }

            var renderer = _game.CurrentStage.EnemyAt(index);
            Assert.That(renderer.enabled, Is.True, "踏まれた直後はつぶれた姿で残る");
            Assert.That(renderer.sprite.name, Does.Contain("Squashed"));
            var ended = Run.Enemies[index].EndedTick;
            StepUntil(() => Run.Ticks >= ended + BrambleDashStage.SquashTicks);
            Assert.That(renderer.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator WalkingIntoWalker_ShowsMissThenRetries()
        {
            yield return StartMatch();
            Time.timeScale = 0;
            _game.Flow.Apply(Intent.Down(Control.Right));
            StepUntil(() => _game.Flow.Screen == AppScreen.Interlude);
            Assert.That(Run.MissReason, Is.EqualTo(MissReason.Enemy));
            Assert.That(Text("headline"), Is.EqualTo("ミス"));
            Assert.That(Text("message"), Does.Contain("敵に触れた"));
            Assert.That(_game.CurrentStage.Player.sprite.name, Does.Contain("Miss"));
            Assert.That(Text("lives"), Does.Contain("●●○"));

            StepUntil(() => _game.Flow.Screen == AppScreen.Playing);
            Assert.That(Run.Ticks, Is.Zero);
            Assert.That(Campaign.LevelIndex, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PauseKey_StopsTimeAndShowsPanel()
        {
            yield return StartMatch();
            yield return WaitSeconds(.1f);
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            var ticks = Run.Ticks;
            yield return WaitSeconds(.2f);
            Assert.That(Run.Ticks, Is.EqualTo(ticks));
            Assert.That(_game.HudRoot.Q("panel").style.display.value, Is.EqualTo(DisplayStyle.Flex));
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
            Assert.That(Run.Ticks, Is.LessThan(30));
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
            yield return StartMatch();
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

        [Test]
        public void Settings_RejectInvalidValues()
        {
            AssertRejected(s => s.Volume = float.NaN);
            AssertRejected(s => s.Volume = 1.5f);
            AssertRejected(s => s.CameraDeadZoneTiles = float.PositiveInfinity);
            AssertRejected(s => s.CameraDeadZoneTiles = -1);
            AssertRejected(s => s.InterludeTicks = 0);
            AssertRejected(s => s.Rules.Gravity = 0);
        }

        private static void AssertRejected(Action<BrambleDashSettings> change)
        {
            var settings = ScriptableObject.CreateInstance<BrambleDashSettings>();
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
