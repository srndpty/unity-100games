using System.Collections;
using System.Diagnostics;
using System.Reflection;
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
    public sealed class FroggerSceneTests
    {
        private TestKeyboard _keyboard;
        private FroggerGame _game;
        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard(); yield return SceneManager.LoadSceneAsync("Frogger"); yield return null;
            _game = Object.FindAnyObjectByType<FroggerGame>(); _game.ManualTicks = true;
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }
        [UnityTearDown]
        public IEnumerator Unload() { _keyboard.Dispose(); yield return null; }
        private IEnumerator Start() { yield return _keyboard.Tap(Key.Enter); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing)); }
        private void Cross()
        {
            for (var i = 0; i < 100 && _game.Flow.Screen == AppScreen.Playing; i++) { _game.Step(Autopilot.Next(_game.Flow.Game)); }
            _game.Render();
        }
        private void Focus(bool focus) => typeof(FroggerGame).GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_game, new object[] { focus });
        [UnityTest]
        public IEnumerator Scene_HasVisibleStageAndFixedRendererCount()
        {
            Assert.That(_game.View.ObjectCount, Is.EqualTo(24));
            Assert.That(_game.View.GetComponentsInChildren<SpriteRenderer>().Length, Is.EqualTo(26));
            Assert.That(_game.View.FrogPosition, Is.EqualTo(new Vector3(0, -4, 0)));
            foreach (var renderer in _game.View.GetComponentsInChildren<SpriteRenderer>()) { Assert.That(renderer.sprite, Is.Not.Null); }
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("向こう岸へ")); yield return null;
            StringAssert.Contains("\n", _game.HudRoot.Q<Label>("instructions").text);
            StringAssert.DoesNotContain("\\n", _game.HudRoot.Q<Label>("instructions").text);
        }
        [UnityTest]
        public IEnumerator Keyboard_StartMovePauseResume()
        {
            yield return Start(); yield return _keyboard.Tap(Key.UpArrow); _game.Step();
            Assert.That(_game.Flow.Game.Row, Is.EqualTo(1));
            yield return _keyboard.Tap(Key.P); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return _keyboard.Tap(Key.Escape); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }
        [UnityTest]
        public IEnumerator Wasd_ControlsWork()
        {
            yield return Start(); yield return _keyboard.Tap(Key.A); _game.Step(); Assert.That(_game.Flow.Game.X, Is.EqualTo(4.5));
            yield return _keyboard.Tap(Key.D); _game.Step(); Assert.That(_game.Flow.Game.X, Is.EqualTo(5.5));
            yield return _keyboard.Tap(Key.W); _game.Step(); Assert.That(_game.Flow.Game.Row, Is.EqualTo(1));
            yield return _keyboard.Tap(Key.S); _game.Step(); Assert.That(_game.Flow.Game.Row, Is.Zero);
        }
        [UnityTest]
        public IEnumerator HeldKey_DoesNotRepeat()
        {
            yield return Start(); _keyboard.Hold(Key.UpArrow); yield return null; _game.Step();
            for (var i = 0; i < 4; i++) { yield return null; _game.Step(); }
            Assert.That(_game.Flow.Game.Row, Is.EqualTo(1));
        }
        [UnityTest]
        public IEnumerator StartWithHeldMove_RequiresRelease()
        {
            _keyboard.Hold(Key.Enter, Key.UpArrow); yield return null; _game.Step();
            _keyboard.Hold(Key.UpArrow); yield return null; _game.Step(); Assert.That(_game.Flow.Game.Row, Is.Zero);
            _keyboard.Hold(); yield return null; yield return _keyboard.Tap(Key.UpArrow); _game.Step(); Assert.That(_game.Flow.Game.Row, Is.EqualTo(1));
        }
        [UnityTest]
        public IEnumerator SimultaneousKeys_UseUpPriority()
        {
            yield return Start(); _keyboard.Hold(Key.RightArrow, Key.UpArrow); yield return null; _game.Step();
            Assert.That(_game.Flow.Game.Row, Is.EqualTo(1)); Assert.That(_game.Flow.Game.X, Is.EqualTo(5.5));
        }
        [UnityTest]
        public IEnumerator DefaultScene_CrossWinReplay()
        {
            yield return Start(); Cross(); Assert.That(_game.Flow.Game.Result, Is.EqualTo(RoundResult.Won));
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("渡れた！"));
            yield return _keyboard.Tap(Key.Enter); Assert.That(_game.Flow.Game.Row, Is.Zero); Assert.That(_game.Flow.Game.TickCount, Is.Zero);
        }
        [UnityTest]
        public IEnumerator CarFailure_HasCorrectMessage()
        {
            yield return Start(); _game.Step(MoveDirection.Up);
            for (var i = 0; i < 300 && _game.Flow.Screen == AppScreen.Playing; i++) { _game.Step(); }
            _game.Render(); Assert.That(_game.Flow.Game.Result, Is.EqualTo(RoundResult.CarHit));
            StringAssert.Contains("車", _game.HudRoot.Q<Label>("message").text);
        }
        [UnityTest]
        public IEnumerator WaterFailure_HasCorrectMessage()
        {
            yield return Start(); for (var i = 0; i < 4; i++) { _game.Step(MoveDirection.Up); }
            _game.Step(MoveDirection.Right); _game.Step(MoveDirection.Right); _game.Step(MoveDirection.Up); _game.Render();
            Assert.That(_game.Flow.Game.Result, Is.EqualTo(RoundResult.Drowned)); StringAssert.Contains("川", _game.HudRoot.Q<Label>("message").text);
        }
        [UnityTest]
        public IEnumerator LogCarry_TracksCoreAndFailsAtEdge()
        {
            yield return Start(); for (var i = 0; i < 5; i++) { _game.Step(MoveDirection.Up); }
            for (var i = 0; i < 100; i++) { _game.Step(); }
            _game.Render();
            Assert.That(_game.View.FrogPosition.x, Is.EqualTo((float)_game.Flow.Game.X - 5.5f).Within(.0001));
            for (var i = 0; i < 700 && _game.Flow.Screen == AppScreen.Playing; i++) { _game.Step(); }
            _game.Render();
            Assert.That(_game.Flow.Game.Result, Is.EqualTo(RoundResult.CarriedOut)); StringAssert.Contains("外", _game.HudRoot.Q<Label>("message").text);
        }
        [UnityTest]
        public IEnumerator FocusLoss_ClearsPendingPauseAndMovement()
        {
            yield return Start(); _keyboard.Hold(Key.UpArrow, Key.P); InputSystem.Update(); Focus(false); yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused)); Assert.That(_game.Flow.PendingMove, Is.EqualTo(MoveDirection.None));
            Focus(true); _keyboard.Hold(); yield return null; _game.Step(); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return _keyboard.Tap(Key.P); _game.Step(); Assert.That(_game.Flow.Game.Row, Is.Zero);
        }
        [UnityTest]
        public IEnumerator PauseThenFocusLoss_RemainsPaused()
        {
            yield return Start(); yield return _keyboard.Tap(Key.P); Focus(false); Focus(true); yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
        }
        [UnityTest]
        public IEnumerator UiButtons_StartPauseBack()
        {
            Click("submit");
            yield return null; Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return _keyboard.Tap(Key.P);
            Click("resume");
            yield return null; Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            yield return _keyboard.Tap(Key.P);
            Click("back");
            yield return null; Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
        }
        private void Click(string name)
        {
            var button = _game.HudRoot.Q<Button>(name); var point = button.worldBound.center;
            Assert.That(_game.HudRoot.panel.Pick(point), Is.SameAs(button));
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = point, button = 0 }))
            { down.target = button; button.SendEvent(down); }
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = point, button = 0 }))
            { up.target = button; button.SendEvent(up); }
        }
        [UnityTest]
        public IEnumerator Result_IgnoresMovement()
        {
            yield return Start(); Cross(); var x = _game.Flow.Game.X; yield return _keyboard.Tap(Key.DownArrow); _game.Step();
            Assert.That(_game.Flow.Game.X, Is.EqualTo(x)); Assert.That(_game.Flow.Game.Row, Is.EqualTo(8));
        }
        [UnityTest]
        public IEnumerator Reload_DoesNotAccumulateStages()
        {
            yield return SceneManager.LoadSceneAsync("Frogger"); yield return null;
            Assert.That(Object.FindObjectsByType<FroggerView>().Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<FroggerGame>().Length, Is.EqualTo(1));
        }
        [UnityTest]
        public IEnumerator LandscapeAspect_FitsBoard()
        {
            var camera = Object.FindAnyObjectByType<Camera>(); var original = camera.aspect;
            foreach (var aspect in new[] { 1.6f, 16f / 9, 21f / 9 })
            {
                camera.aspect = aspect; _game.Render();
                Assert.That(camera.orthographicSize, Is.GreaterThanOrEqualTo(5));
                Assert.That(camera.orthographicSize * aspect, Is.GreaterThanOrEqualTo(5.94f));
            }
            camera.aspect = original; yield return null;
        }
        [UnityTest]
        public IEnumerator TitleRender_DoesNotAllocateAndUsesCurrentSettings()
        {
            var config = _game.Settings.Game.Copy();
            for (var i = 0; i < 100; i++) { _game.View.Render(null, config); }
            var before = System.GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) { _game.View.Render(null, config); }
            var allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero, "タイトル描画で管理ヒープを確保しない");
            config.Lanes[0].Phase = 3;
            _game.View.Render(null, config);
            var car = _game.View.transform.Find("Lane0Object0");
            Assert.That(car.localPosition.x, Is.EqualTo((float)config.Lanes[0].PositionAt(0, 0) - 5.5f));
            Assert.That(_game.View.FrogPosition, Is.EqualTo(new Vector3(0, -4, 0)));
            _game.Render(); yield return null;
        }
        [UnityTest]
        public IEnumerator MaximumSettings_ProfileCpuRenderAndObjectCount()
        {
            var maximum = _game.Settings.Game.Copy();
            foreach (var lane in maximum.Lanes) { lane.Speed = lane.Speed < 0 ? -12 : 12; lane.Width = 3.5f; }
            maximum.FrogRadius = .4f;
            // 川では最後の丸太で支持される位相にし、探索4回を通す。
            maximum.Lanes[3].Phase = 12; maximum.Validate();
            foreach (var row in new[] { 0, 1, 5 })
            {
                var template = new FroggerState(maximum);
                // 道路は最大幅・半幅では必ず衝突するため、ゲーム用APIを増やさずテスト内で場面を作る。
                typeof(FroggerState).GetProperty(nameof(FroggerState.Row)).SetValue(template, row);
                var states = new FroggerState[1100];
                for (var i = 0; i < states.Length; i++) { states[i] = template.Copy(); }
                var samples = new double[1000]; var timer = new Stopwatch();
                for (var i = 0; i < 100; i++) { states[i].Step(); _game.View.Render(states[i], maximum); }
                for (var i = 0; i < samples.Length; i++)
                {
                    var state = states[i + 100];
                    Assert.That(state.Result, Is.EqualTo(RoundResult.Playing));
                    timer.Restart(); state.Step(); _game.View.Render(state, maximum); timer.Stop(); samples[i] = timer.Elapsed.TotalMilliseconds;
                    Assert.That(state.TickCount, Is.EqualTo(1));
                    Assert.That(state.Result, Is.EqualTo(row == 1 ? RoundResult.CarHit : RoundResult.Playing));
                    if (row == 5) { Assert.That(state.SupportId, Is.EqualTo(3)); }
                }
                System.Array.Sort(samples);
                UnityEngine.Debug.Log($"[FroggerPerformance] row={row}; CPU step+render p50={samples[500]:F4}ms p95={samples[950]:F4}ms; sprites=26; objects=24; samples=1000; setup excluded; {SystemInfo.processorType}; {Screen.width}x{Screen.height}; {Application.unityVersion}");
                Assert.That(_game.View.ObjectCount, Is.EqualTo(24)); Assert.That(_game.View.GetComponentsInChildren<SpriteRenderer>().Length, Is.EqualTo(26));
            }
            _game.Render(); yield return null;
        }
    }
}
