using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Game.App;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Game.Unity.Tests
{
    public sealed class FlappySceneTests
    {
        private TestKeyboard _keyboard;
        private Mouse _mouse;
        private FlappyGame _game;
        private FlappyConfig _originalConfig;
        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard(); _mouse = InputSystem.AddDevice<Mouse>();
            yield return SceneManager.LoadSceneAsync("Flappy"); yield return null;
            _game = Object.FindAnyObjectByType<FlappyGame>(); _game.ManualTicks = true;
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            _originalConfig = _game.Settings.Game.Copy();
            Assert.That(_game.HudRoot, Is.Not.Null);
        }
        [UnityTearDown]
        public IEnumerator Unload()
        {
            if (_game != null) { _game.Settings.Game = _originalConfig; }
            InputSystem.RemoveDevice(_mouse); _keyboard.Dispose(); yield return null;
        }
        private IEnumerator StartReady() { yield return _keyboard.Tap(Key.Enter); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Ready)); }
        private void MouseState(Vector2 position, bool pressed)
        {
            var state = new MouseState { position = position }; state = state.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left, pressed);
            InputSystem.QueueStateEvent(_mouse, state);
        }
        [UnityTest]
        public IEnumerator Keyboard_StartFlapPauseResume()
        {
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("SKY HOPPER"));
            yield return StartReady(); yield return _keyboard.Tap(Key.Space);
            Assert.That(_game.Flow.PendingFlap, Is.True); _game.Flow.StepOneTick(); _game.Render();
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing)); Assert.That(_game.Flow.Game.Velocity, Is.GreaterThan(0));
            yield return _keyboard.Tap(Key.P); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return _keyboard.Tap(Key.Escape); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }
        [UnityTest]
        public IEnumerator HeldSpace_DoesNotRepeat_AndTransitionsRequireRelease()
        {
            _keyboard.Hold(Key.Enter, Key.Space); yield return null;
            _keyboard.Hold(Key.Space); yield return null; Assert.That(_game.Flow.PendingFlap, Is.False);
            _keyboard.Hold(); yield return null;
            _keyboard.Hold(Key.Space); yield return null; _game.Flow.StepOneTick();
            yield return null; Assert.That(_game.Flow.PendingFlap, Is.False);
            _game.Flow.StepOneTick(); Assert.That(_game.Flow.Game.Velocity, Is.EqualTo(5.4f).Within(.0001));
        }
        [UnityTest]
        public IEnumerator Mouse_FlapsOnceWithoutHoldingRepeat()
        {
            yield return StartReady(); MouseState(new Vector2(100, 100), true); yield return null;
            Assert.That(_game.Flow.PendingFlap, Is.True); _game.Flow.StepOneTick(); yield return null;
            Assert.That(_game.Flow.PendingFlap, Is.False); MouseState(new Vector2(100, 100), false); yield return null;
        }
        [UnityTest]
        public IEnumerator UiClick_StartDoesNotLeakFlap()
        {
            yield return null; yield return null;
            var button = _game.HudRoot.Q<Button>("submit"); var panelPoint = button.worldBound.center;
            var panel = _game.HudRoot.panel; var rootSize = panel.visualTree.worldBound.size;
            var point = new Vector2(panelPoint.x / rootSize.x * Screen.width, Screen.height - panelPoint.y / rootSize.y * Screen.height);
            // バッチではGameViewへのUIイベント配送が抑止されるため、
            // 座標のヒットテストとゲーム側マウス入力を確かめてからUIへ配送する。
            Assert.That(panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, new Vector2(point.x, Screen.height - point.y))), Is.SameAs(button));
            MouseState(point, false); yield return null; MouseState(point, true); yield return null;
            Assert.That(_game.Flow.PendingFlap, Is.False);
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, mousePosition = panelPoint, button = 0 }))
            { down.target = button; button.SendEvent(down); }
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, mousePosition = panelPoint, button = 0 }))
            { up.target = button; button.SendEvent(up); }
            MouseState(point, false); yield return null; yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Ready)); Assert.That(_game.Flow.PendingFlap, Is.False);
            Assert.That(_game.Flow.Game.TickCount, Is.Zero);
        }
        [UnityTest]
        public IEnumerator FocusLoss_DiscardsQueuedPauseAndFlap()
        {
            yield return StartReady(); _game.Step(new FlappyCommand(true));
            _keyboard.Hold(Key.P, Key.Space); InputSystem.Update(); _game.SendMessage("OnApplicationFocus", false); yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused)); Assert.That(_game.Flow.PendingFlap, Is.False);
            _game.SendMessage("OnApplicationFocus", true); yield return null; Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _keyboard.Hold(); yield return null; yield return _keyboard.Tap(Key.P);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing)); Assert.That(_game.Flow.PendingFlap, Is.False);
        }
        [UnityTest]
        public IEnumerator FocusLossAfterPause_DoesNotResume()
        {
            yield return StartReady(); _game.Step(new FlappyCommand(true));
            yield return _keyboard.Tap(Key.P); _game.SendMessage("OnApplicationFocus", false); yield return null;
            _game.SendMessage("OnApplicationFocus", true); yield return null; Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
        }
        [UnityTest]
        public IEnumerator ResultReplay_HudMatchesState()
        {
            yield return StartReady(); _game.Step(new FlappyCommand(true));
            for (var i = 0; i < 200; i++) { _game.Step(default); }
            _game.Render(); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("GAME OVER"));
            Assert.That(_game.HudRoot.Q<Label>("message").text, Does.Contain("SCORE  " + _game.Flow.Game.Score));
            var seed = _game.Flow.GameSeed; yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Ready)); Assert.That(_game.Flow.GameSeed, Is.EqualTo(unchecked(seed + 1)));
            Assert.That(_game.Flow.Game.Score, Is.Zero); Assert.That(_game.HudRoot.Q<Label>("status").text, Does.Contain("READY"));
        }
        [UnityTest]
        public IEnumerator BackButton_ReturnsToTitle()
        {
            yield return StartReady(); yield return _keyboard.Tap(Key.P);
            var field = typeof(FlappyGame).GetField("_backRequested", BindingFlags.NonPublic | BindingFlags.Instance); field.SetValue(_game, true);
            yield return null; Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title)); Assert.That(_game.Flow.Game, Is.Null);
        }
        [UnityTest]
        public IEnumerator Pipes_AreClippedToFieldBeforeTheyAreRecycled()
        {
            yield return StartReady(); _game.Render();
            foreach (var renderer in _game.GetComponentsInChildren<SpriteRenderer>())
            {
                if (!renderer.name.StartsWith("Pipe") || !renderer.enabled) { continue; }
                Assert.That(renderer.bounds.min.x, Is.GreaterThanOrEqualTo(-12.0001f));
                Assert.That(renderer.bounds.max.x, Is.LessThanOrEqualTo(12.0001f));
            }
        }
        [UnityTest]
        public IEnumerator Audio_IsMutedInBatch_AndHasLowVolume()
        {
            Assert.That(_game.GetComponent<AudioSource>().mute, Is.EqualTo(AudioPolicy.ShouldMute));
            Assert.That(_game.GetComponent<AudioSource>().volume, Is.EqualTo(.15f)); yield return null;
        }
        [UnityTest]
        public IEnumerator LandscapeCamera_KeepsFieldInside()
        {
            var camera = Camera.main; var before = camera.aspect;
            foreach (var aspect in new[] { 16f / 10, 16f / 9, 21f / 9 })
            {
                camera.aspect = aspect; _game.Render();
                var min = camera.WorldToViewportPoint(new Vector3(-12, -7.3f)); var max = camera.WorldToViewportPoint(new Vector3(12, 7.3f));
                Assert.That(min.x, Is.GreaterThanOrEqualTo(0)); Assert.That(min.y, Is.GreaterThanOrEqualTo(0));
                Assert.That(max.x, Is.LessThanOrEqualTo(1)); Assert.That(max.y, Is.LessThanOrEqualTo(1));
            }
            camera.aspect = before; yield return null;
        }
        [UnityTest]
        public IEnumerator Reload_DestroysOwnedSpritesTexturesAndAudio()
        {
            var sprite = _game.transform.Find("Bird").GetComponent<SpriteRenderer>().sprite;
            var texture = sprite.texture; var clip = (AudioClip)typeof(FlappyGame).GetField("_flapSound", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(_game);
            yield return SceneManager.LoadSceneAsync("Flappy"); yield return null;
            Assert.That(sprite == null, Is.True); Assert.That(texture == null, Is.True); Assert.That(clip == null, Is.True);
        }
        [UnityTest]
        public IEnumerator MaximumCapacity_LongRunKeepsRendererCountAndMeasuresCpu()
        {
            // 間隔最小・幅最大が最大容量。物理も上限側へ寄せる。
            var c = new FlappyConfig { BirdRadius = .6f, Gravity = -30, FlapSpeed = 9, TerminalSpeed = 16, PipeWidth = 2, Spacing = 5, Speed = 5, SpeedIncrease = .4f, Gap = 4.75f, GapDecrease = .25f, WallMargin = 1.5f };
            _game.Settings.Game = c; _game.Flow.Back();
            typeof(FlappyGame).GetProperty("Flow").SetValue(_game, new FlappyFlow(c, 71));
            _game.Flow.Submit(); _game.Step(new FlappyCommand(true));
            // 描画は最大容量8組を初期確保。再配置で生成物数を増やさない。
            var count = _game.GetComponentsInChildren<SpriteRenderer>().Length; var times = new double[1000];
            for (var i = 0; i < 10000 && !_game.Flow.Game.Lost; i++)
            {
                _game.Step(Autopilot.Next(_game.Flow.Game, c));
                if (i % 1000 == 0) { yield return null; }
            }
            Assert.That(_game.Flow.Game.Lost, Is.False); Assert.That(_game.Flow.Game.Score, Is.GreaterThan(100));
            var clock = new Stopwatch();
            for (var i = 0; i < times.Length; i++) { clock.Restart(); _game.Render(); clock.Stop(); times[i] = clock.Elapsed.TotalMilliseconds; }
            System.Array.Sort(times);
            Assert.That(times[950], Is.LessThan(16.67), "CPU描画更新p95の予算");
            Assert.That(count, Is.EqualTo(59));
            Assert.That(_game.PipeRendererCount, Is.EqualTo(c.Capacity * 4)); Assert.That(_game.GetComponentsInChildren<SpriteRenderer>().Length, Is.EqualTo(count));
            UnityEngine.Debug.Log($"[FlappyCapacity] CPU={SystemInfo.processorType}; GPU={SystemInfo.graphicsDeviceName}; resolution={Screen.width}x{Screen.height}; pipes={c.Capacity}; sprites={count}; score={_game.Flow.Game.Score}; samples={times.Length}; render p50={times[500]:F4}ms p95={times[950]:F4}ms");
        }
    }
}
