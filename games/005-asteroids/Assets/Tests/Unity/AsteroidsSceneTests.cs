using System.Collections;
using System.Collections.Generic;
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
    public sealed class AsteroidsSceneTests
    {
        private TestKeyboard _keyboard;
        private AsteroidsGame _game;
        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard(); yield return SceneManager.LoadSceneAsync("Asteroids"); yield return null;
            _game = Object.FindAnyObjectByType<AsteroidsGame>(); _game.ManualTicks = true;
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }
        [UnityTearDown]
        public IEnumerator Unload() { _keyboard.Dispose(); yield return null; }
        private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Property(object target, string name, object value) => target.GetType().GetProperty(name).SetValue(target, value);

        [UnityTest]
        public IEnumerator Keyboard_StartTurnThrustFireAndPause()
        {
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("ORBIT BREAKER"));
            yield return _keyboard.Tap(Key.Enter);
            _keyboard.Hold(Key.D, Key.W, Key.Space); yield return null;
            Assert.That(_game.Flow.Input.Turn, Is.EqualTo(-1)); Assert.That(_game.Flow.Input.Thrust, Is.True);
            _game.Flow.StepOneTick(); _game.Render();
            Assert.That(_game.Flow.Game.BulletCount, Is.EqualTo(1)); Assert.That(_game.Flow.Game.ShipVelocity.Length, Is.GreaterThan(0));
            yield return _keyboard.Tap(Key.P); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return _keyboard.Tap(Key.P); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }
        [UnityTest]
        public IEnumerator BothDirections_CancelAndHeldControlsNeedRelease()
        {
            _keyboard.Hold(Key.W, Key.Space, Key.Enter); yield return null;
            _keyboard.Hold(Key.W, Key.Space); yield return null;
            Assert.That(_game.Flow.Input.Thrust, Is.False); Assert.That(_game.Flow.Input.Fire, Is.False);
            _keyboard.Hold(); yield return null;
            _keyboard.Hold(Key.A, Key.D); yield return null;
            Assert.That(_game.Flow.Input.Turn, Is.Zero);
            _keyboard.Hold(Key.A); yield return null; Assert.That(_game.Flow.Input.Turn, Is.EqualTo(1));
        }
        [UnityTest]
        public IEnumerator FocusLoss_StopsAndDiscardsQueuedPause()
        {
            yield return _keyboard.Tap(Key.Enter);
            _keyboard.Hold(Key.P, Key.W, Key.Space); InputSystem.Update();
            _game.SendMessage("OnApplicationFocus", false); yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused)); Assert.That(_game.Flow.Input.Fire, Is.False);
            _game.SendMessage("OnApplicationFocus", true); yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _game.Flow.TogglePause(); yield return null; Assert.That(_game.Flow.Input.Thrust, Is.False);
            _keyboard.Hold(); yield return null; _keyboard.Hold(Key.W); yield return null;
            Assert.That(_game.Flow.Input.Thrust, Is.True);
        }
        [UnityTest]
        public IEnumerator PauseThenFocusLoss_DoesNotResume()
        {
            yield return _keyboard.Tap(Key.Enter); yield return _keyboard.Tap(Key.P);
            _game.SendMessage("OnApplicationFocus", false); yield return null;
            _game.SendMessage("OnApplicationFocus", true); yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
        }
        [UnityTest]
        public IEnumerator Hud_WaveVictoryReplayAndButtons()
        {
            _game.Flow.Submit(); var state = _game.Flow.Game;
            Field<List<Asteroid>>(state, "_asteroids").Clear(); _game.Flow.StepOneTick(); _game.Render();
            Assert.That(_game.HudRoot.Q<Label>("status").text, Is.EqualTo("WAVE CLEAR"));
            for (var i = 0; i < 60; i++) { _game.Flow.StepOneTick(); }
            Field<List<Asteroid>>(state, "_asteroids").Clear(); Property(state, "Wave", 3); _game.Flow.StepOneTick(); _game.Render();
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("YOU WIN"));
            var seed = _game.Flow.GameSeed; yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.GameSeed, Is.EqualTo(unchecked(seed + 1))); Assert.That(_game.Flow.Game.Score, Is.Zero);
            Assert.That(_game.Flow.Game.Lives, Is.EqualTo(_game.Settings.Game.Lives));
            yield return _keyboard.Tap(Key.P); _game.Flow.Back(); _game.Render(); yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
        }
        [UnityTest]
        public IEnumerator DamageAndResultHud_MatchPreparedState()
        {
            _game.Flow.Submit(); var state = _game.Flow.Game;
            var rocks = Field<List<Asteroid>>(state, "_asteroids"); rocks.Clear(); rocks.Add(new Asteroid(99, 0, default, default, 0, 0));
            Property(state, "InvincibleTicks", 0); Property(state, "Lives", 1); _game.Flow.StepOneTick(); _game.Render();
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("GAME OVER"));
            Assert.That(_game.HudRoot.Q<Label>("score").text, Does.Contain("LIVES  0"));
            yield return null;
        }
        [UnityTest]
        public IEnumerator EdgeAndCorner_RenderPeriodicCopies()
        {
            _game.Flow.Submit(); var state = _game.Flow.Game;
            Property(state, "ShipPosition", new Point(15.9f, 8.9f)); Property(state, "InvincibleTicks", 0); _game.Render();
            var visible = 0;
            foreach (var renderer in Field<List<SpriteRenderer>>(_game, "_ships")) { if (renderer.enabled) { visible++; } }
            Assert.That(visible, Is.EqualTo(4)); yield return null;
        }
        [UnityTest]
        public IEnumerator CameraFramesLandscape_AndAudioIsMuted()
        {
            var camera = Object.FindAnyObjectByType<Camera>();
            foreach (var aspect in new[] { 16f / 10, 16f / 9, 21f / 9 })
            {
                camera.aspect = aspect; _game.Render(); Assert.That(camera.orthographicSize, Is.GreaterThanOrEqualTo(10));
                Assert.That(camera.orthographicSize * aspect, Is.GreaterThanOrEqualTo(17));
            }
            camera.ResetAspect(); Assert.That(_game.GetComponent<AudioSource>().mute, Is.True); yield return null;
        }
        [UnityTest]
        public IEnumerator Reload_ReleasesOwnedResources()
        {
            var sprite = Field<Sprite>(_game, "_shipSprite"); var texture = sprite.texture; var clip = Field<AudioClip>(_game, "_hit");
            yield return SceneManager.LoadSceneAsync("Asteroids"); yield return null;
            Assert.That(sprite == null, Is.True); Assert.That(texture == null, Is.True); Assert.That(clip == null, Is.True);
        }
        [UnityTest]
        public IEnumerator MaximumSettings_GrowthAndCpuBudget()
        {
            var original = _game.Settings.Game;
            var config = new AsteroidsConfig { FirstWaveCount = 8, WaveIncrease = 0, WaveCount = 5, BulletLimit = 16, FireSeconds = 1f / 60, BulletSpeed = 120, MaxSpeed = 16, SplitSpeed = 3, AsteroidMaxSpeed = 6 };
            var flow = new AsteroidsFlow(config, 1); flow.Submit(); Property(_game, "Flow", flow); _game.Settings.Game = config;
            try
            {
                var state = flow.Game; var rocks = Field<List<Asteroid>>(state, "_asteroids"); var bullets = Field<List<Bullet>>(state, "_bullets");
                for (var stage = 0; stage < 2; stage++)
                {
                    bullets.Clear();
                    for (var i = 0; i < rocks.Count; i++) { bullets.Add(new Bullet(100 + i, rocks[i].Position, default, 100)); }
                    flow.StepOneTick(); _game.Render();
                }
                Assert.That(state.AsteroidCount, Is.EqualTo(32));
                var core = new double[300]; var render = new double[300]; var watch = new Stopwatch();
                for (var sample = 0; sample < 300; sample++)
                {
                    rocks.Clear(); bullets.Clear();
                    for (var i = 0; i < 32; i++) { rocks.Add(new Asteroid(i, 0, new Point(i % 8 * 4 - 14, i / 8 * 4 - 6), new Point(12, 0), 0, 1)); }
                    for (var i = 0; i < 16; i++) { bullets.Add(new Bullet(100 + i, new Point(i % 8 * 4 - 14, i / 8 * 4 - 6), new Point(136, 0), 100)); }
                    Property(state, "InvincibleTicks", 1000); _game.Render();
                    watch.Restart(); flow.StepOneTick(); watch.Stop(); core[sample] = watch.Elapsed.TotalMilliseconds;
                    watch.Restart(); _game.Render(); watch.Stop(); render[sample] = watch.Elapsed.TotalMilliseconds;
                }
                System.Array.Sort(core); System.Array.Sort(render);
                var count = _game.GetComponentsInChildren<SpriteRenderer>().Length;
                UnityEngine.Debug.Log($"[Capacity] {SystemInfo.processorType}; {Screen.width}x{Screen.height}; 32 rocks / 16 bullets; renderers={count}; samples=300; Core p50={core[150]:F3}ms p95={core[285]:F3}ms; Render p50={render[150]:F3}ms p95={render[285]:F3}ms");
                Assert.That(count, Is.LessThanOrEqualTo(551)); Assert.That(core[285] + render[285], Is.LessThan(16.67));
            }
            finally { _game.Settings.Game = original; }
            yield return null;
        }
    }
}
