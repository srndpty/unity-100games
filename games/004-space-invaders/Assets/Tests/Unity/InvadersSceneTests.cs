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
    public sealed class InvadersSceneTests
    {
        private TestKeyboard _keyboard;
        private InvadersGame _game;
        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            _keyboard = new TestKeyboard();
            yield return SceneManager.LoadSceneAsync("Invaders");
            _game = Object.FindAnyObjectByType<InvadersGame>(); _game.ManualTicks = true;
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
        }
        [TearDown]
        public void Restore() => _keyboard.Dispose();
        [UnityTest]
        public IEnumerator ConfiguredLives_MatchTitleAndStartedHud()
        {
            var original = _game.Settings.Game;
            try
            {
                foreach (var lives in new[] { 1, 5 })
                {
                    var config = original.Copy(); config.Lives = lives;
                    _game.Settings.Game = config;
                    typeof(InvadersGame).GetProperty("Flow").SetValue(_game, new InvadersFlow(config, 1));
                    _game.Render();
                    Assert.That(_game.HudRoot.Q<Label>("score").text, Does.Contain("LIVES  " + lives));
                    Assert.That(_game.HudRoot.Q<Label>("message").text, Does.Contain("Protect your " + lives + " lives."));
                    yield return _keyboard.Tap(Key.Enter);
                    Assert.That(_game.Flow.Game.Lives, Is.EqualTo(lives));
                    Assert.That(_game.HudRoot.Q<Label>("score").text, Does.Contain("LIVES  " + lives));
                    _game.Flow.Back(); _game.Render();
                    Assert.That(_game.HudRoot.Q<Label>("score").text, Does.Contain("LIVES  " + lives));
                }
            }
            finally { _game.Settings.Game = original; }
        }
        [UnityTest]
        public IEnumerator InputMovesFiresAndPauses()
        {
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("STAR GUARD"));
            yield return _keyboard.Tap(Key.Enter);
            _keyboard.Hold(Key.D, Key.Space); yield return null;
            _game.Flow.StepOneTick(); _game.Render();
            Assert.That(_game.Flow.Game.PlayerX, Is.GreaterThan(0)); Assert.That(_game.Flow.Game.BulletCount, Is.EqualTo(1));
            Assert.That(_game.transform.Find("Player").localPosition.x, Is.EqualTo(_game.Flow.Game.PlayerX));
            yield return _keyboard.Tap(Key.P);
            var tick = _game.Flow.Game.TickCount; _game.Flow.Advance(1);
            Assert.That(_game.Flow.Game.TickCount, Is.EqualTo(tick)); Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("PAUSED"));
            yield return _keyboard.Tap(Key.Escape); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }
        [UnityTest]
        public IEnumerator PauseBeforeFocusLoss_RequiresManualResume()
        {
            _game.Flow.Submit();
            _keyboard.Hold(Key.P); InputSystem.Update(); _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _keyboard.Hold(); InputSystem.Update(); _game.SendMessage("OnApplicationFocus", true);
            yield return null; Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return _keyboard.Tap(Key.P); Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }
        [UnityTest]
        public IEnumerator FocusLossBeforePause_DiscardsHeldInput()
        {
            _game.Flow.Submit(); _game.SendMessage("OnApplicationFocus", false);
            yield return _keyboard.Tap(Key.P);
            _keyboard.Hold(Key.D, Key.Space); yield return null;
            _game.SendMessage("OnApplicationFocus", true); yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused)); Assert.That(_game.Flow.Input.Fire, Is.False);
            _game.Flow.TogglePause(); yield return null;
            Assert.That(_game.Flow.Input.Move, Is.Zero);
            _keyboard.Hold(); yield return null;
            _keyboard.Hold(Key.D); yield return null;
            Assert.That(_game.Flow.Input.Move, Is.EqualTo(1));
        }
        [UnityTest]
        public IEnumerator ResultHudAndReplay_MatchCore()
        {
            yield return _keyboard.Tap(Key.Enter);
            for (var i = 0; i < 60000 && _game.Flow.Screen == AppScreen.Playing; i++) { _game.Flow.StepOneTick(); }
            _game.Render();
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("GAME OVER"));
            var seed = _game.Flow.GameSeed;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.GameSeed, Is.EqualTo(unchecked(seed + 1)));
            Assert.That(_game.Flow.Game.Lives, Is.EqualTo(3)); Assert.That(_game.Flow.Game.Score, Is.Zero);
        }
        [UnityTest]
        public IEnumerator WaveAndVictoryHud_MatchPreparedCoreState()
        {
            _game.Flow.Submit(); var state = _game.Flow.Game;
            var enemies = (bool[])typeof(InvadersState).GetField("_enemies", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(state);
            System.Array.Clear(enemies, 0, enemies.Length);
            typeof(InvadersState).GetProperty("EnemyCount").SetValue(state, 0);
            _game.Flow.StepOneTick(); _game.Render();
            Assert.That(_game.HudRoot.Q<Label>("status").text, Is.EqualTo("WAVE CLEAR"));
            for (var i = 0; i < 60; i++) { _game.Flow.StepOneTick(); }
            System.Array.Clear(enemies, 0, enemies.Length);
            typeof(InvadersState).GetProperty("EnemyCount").SetValue(state, 0);
            typeof(InvadersState).GetProperty("Wave").SetValue(state, 3);
            _game.Flow.StepOneTick(); _game.Render();
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("YOU WIN"));
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            yield return null;
        }
        [UnityTest]
        public IEnumerator CameraFramesLandscape_AndAudioIsMuted()
        {
            var camera = Object.FindAnyObjectByType<Camera>();
            foreach (var aspect in new[] { 16f / 10, 16f / 9, 21f / 9 })
            {
                camera.aspect = aspect; _game.Render();
                Assert.That(camera.orthographicSize, Is.GreaterThanOrEqualTo(8));
                Assert.That(camera.orthographicSize * aspect, Is.GreaterThanOrEqualTo(10.3f));
            }
            camera.ResetAspect(); Assert.That(_game.GetComponent<AudioSource>().mute, Is.True);
            yield return null;
        }
        [UnityTest]
        public IEnumerator Reload_ReleasesGeneratedResources()
        {
            var sprite = _game.transform.Find("Player").GetComponent<SpriteRenderer>().sprite;
            var texture = sprite.texture;
            var source = _game.GetComponent<AudioSource>();
            yield return SceneManager.LoadSceneAsync("Invaders"); yield return null;
            Assert.That(sprite == null, Is.True); Assert.That(texture == null, Is.True); Assert.That(source == null, Is.True);
        }
        [UnityTest]
        public IEnumerator MaximumSettings_CpuAndRendererBudget()
        {
            var original = _game.Settings.Game;
            var maximum = new InvadersConfig { Columns = 10, Rows = 5, PlayerBulletLimit = 8, EnemyBulletLimit = 24, PlayerSpeed = 30, PlayerBulletSpeed = 120, EnemyBulletSpeed = 60, FormationSpeed = 5, WaveSpeedIncrease = 2, Descent = 1, PlayerFireSeconds = 1f / 60, EnemyFireSeconds = .1f, WaveFireMultiplier = .5f };
            var flow = new InvadersFlow(maximum, 1); flow.Submit();
            typeof(InvadersGame).GetProperty("Flow").SetValue(_game, flow);
            _game.Settings.Game = maximum;
            try
            {
                var bullets = (List<Bullet>)typeof(InvadersState).GetField("_bullets", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(flow.Game);
                for (var i = 0; i < 32; i++) { bullets.Add(new Bullet(i, i < 8 ? BulletOwner.Player : BulletOwner.Enemy, new Point((i % 10 - 5) * 1.5f, 0))); }
                _game.Render();
                var timings = new double[300]; var watch = new Stopwatch();
                for (var sample = 0; sample < timings.Length; sample++)
                {
                    bullets.Clear();
                    for (var i = 0; i < 32; i++) { bullets.Add(new Bullet(i, i < 8 ? BulletOwner.Player : BulletOwner.Enemy, new Point((i % 10 - 5) * 1.5f, 0))); }
                    watch.Restart(); flow.StepOneTick(); _game.Render(); watch.Stop(); timings[sample] = watch.Elapsed.TotalMilliseconds;
                }
                System.Array.Sort(timings);
                var renderers = _game.GetComponentsInChildren<SpriteRenderer>().Length;
                UnityEngine.Debug.Log($"[Capacity] {SystemInfo.processorType}; {Screen.width}x{Screen.height}; 50 enemies / 216 cells / 32 bullets; renderers={renderers}; samples=300; CPU p50={timings[150]:F3}ms p95={timings[285]:F3}ms");
                Assert.That(renderers, Is.LessThanOrEqualTo(356)); Assert.That(timings[285], Is.LessThan(16.67));
            }
            finally { _game.Settings.Game = original; }
            yield return null;
        }
    }
}
