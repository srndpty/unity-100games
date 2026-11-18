using System.Collections;
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
    public sealed class MazeSceneTests
    {
        private TestKeyboard _keyboard;
        private MazeGame _game;

        [UnitySetUp]
        public IEnumerator Load()
        {
            _keyboard = new TestKeyboard();
            yield return SceneManager.LoadSceneAsync("Maze");
            yield return null;
            _game = Object.FindAnyObjectByType<MazeGame>();
            _game.ManualTicks = true;
            for (var i = 0; i < 30 && _game.HudRoot == null; i++) { yield return null; }
            Assert.That(_game.HudRoot, Is.Not.Null);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            _keyboard.Dispose();
            yield return null;
        }

        private IEnumerator StartMatch()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        private void RunToActive()
        {
            for (var i = 0; i < 600 && _game.Flow.Game.Phase == GamePhase.Ready; i++) { _game.Step(); }
            Assert.That(_game.Flow.Game.Phase, Is.EqualTo(GamePhase.Active));
        }

        private void RunToGameOver()
        {
            for (var i = 0; i < 60000 && _game.Flow.Screen == AppScreen.Playing; i++) { _game.Step(); }
            _game.Render();
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
        }

        private void Focus(bool focus) =>
            typeof(MazeGame).GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_game, new object[] { focus });

        private string Text(string name) => _game.HudRoot.Q<Label>(name).text;

        [UnityTest]
        public IEnumerator Scene_ShowsStageBeforePlay()
        {
            var pellets = new MazeGameState(_game.Settings.Game, 1).PelletsTotal;
            Assert.That(_game.View.PelletCount, Is.EqualTo(pellets));
            Assert.That(_game.View.VisiblePelletCount, Is.EqualTo(pellets));
            foreach (var renderer in _game.View.GetComponentsInChildren<SpriteRenderer>()) { Assert.That(renderer.sprite, Is.Not.Null, renderer.name); }
            Assert.That(Text("headline"), Is.EqualTo("NIBBLE MAZE"));
            StringAssert.Contains("方向キー", Text("message"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Keyboard_StartMovePauseResume()
        {
            yield return StartMatch();
            RunToActive();
            yield return _keyboard.Tap(Key.LeftArrow);
            _game.Step();
            Assert.That(_game.Flow.Game.Player.Heading, Is.EqualTo(Direction.Left));
            Assert.That(_game.Flow.Game.IsPlayerMoving, Is.True);
            yield return _keyboard.Tap(Key.P);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [UnityTest]
        public IEnumerator Wasd_ControlsWork()
        {
            yield return StartMatch();
            RunToActive();
            yield return _keyboard.Tap(Key.D);
            _game.Step();
            Assert.That(_game.Flow.Game.Player.Heading, Is.EqualTo(Direction.Right));
        }

        [UnityTest]
        public IEnumerator EatingPellet_HidesItInView()
        {
            yield return StartMatch();
            RunToActive();
            var before = _game.Flow.Game.PelletsRemaining;
            for (var i = 0; i < 30; i++) { _game.Step(Direction.Left); }
            _game.Render();
            Assert.That(_game.Flow.Game.PelletsRemaining, Is.LessThan(before));
            Assert.That(_game.View.VisiblePelletCount, Is.EqualTo(_game.Flow.Game.PelletsRemaining));
        }

        [UnityTest]
        public IEnumerator Tab_TogglesTargetMarkers()
        {
            yield return StartMatch();
            RunToActive();
            _game.Step();
            _game.Render();
            Assert.That(_game.View.IsMarkerVisible(0), Is.False);
            yield return _keyboard.Tap(Key.Tab);
            _game.Render();
            Assert.That(_game.View.IsMarkerVisible(0), Is.True);
        }

        [UnityTest]
        public IEnumerator TargetMarkers_ShowAllFourCatsOnceRoaming()
        {
            // 自機が捕まる前に全員が外へ出るよう、すぐ退出する設定にする。
            var config = _game.Settings.Game.Copy();
            config.ReleaseTicks = new[] { 0, 0, 0, 0 };
            config.LeavingSpeed = MazeGameConfig.MaxSpeed;
            _game.UseConfig(config);
            yield return StartMatch();
            RunToActive();
            _game.ShowTargets = true;
            var game = _game.Flow.Game;
            for (var i = 0; i < 120 && !AllRoaming(game); i++) { _game.Step(); }
            Assert.That(AllRoaming(game), Is.True);
            _game.Render();
            for (var i = 0; i < MazeGameConfig.CatCount; i++) { Assert.That(_game.View.IsMarkerVisible(i), Is.True, "猫 " + i); }
        }

        private static bool AllRoaming(MazeGameState game)
        {
            for (var i = 0; i < MazeGameConfig.CatCount; i++)
            {
                if (game.Cat(i).Mode != CatMode.Roaming) { return false; }
            }

            return game.Phase == GamePhase.Active;
        }

        [UnityTest]
        public IEnumerator Title_ShowsPelletTotal()
        {
            _game.Render();
            StringAssert.Contains(_game.View.PelletCount.ToString(), Text("pellets"));
            yield return null;
        }

        [UnityTest]
        public IEnumerator GameOver_ShowsResultAndReplayStartsFresh()
        {
            yield return StartMatch();
            RunToGameOver();
            Assert.That(Text("headline"), Is.EqualTo("つかまった…"));
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_game.Flow.Game.Lives, Is.EqualTo(_game.Settings.Game.Lives));
            Assert.That(_game.Flow.Game.TickCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Result_IgnoresMovementKeys()
        {
            yield return StartMatch();
            RunToGameOver();
            var tick = _game.Flow.Game.TickCount;
            yield return _keyboard.Tap(Key.UpArrow);
            _game.Step();
            Assert.That(_game.Flow.Game.TickCount, Is.EqualTo(tick));
        }

        [UnityTest]
        public IEnumerator FocusLoss_PausesAndNeedsManualResume()
        {
            yield return StartMatch();
            Focus(false);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return _keyboard.Tap(Key.P);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            Focus(true);
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return _keyboard.Tap(Key.P);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [UnityTest]
        public IEnumerator PauseKeyThenFocusLossInSameFrame_DoesNotResume()
        {
            yield return StartMatch();
            yield return _keyboard.Tap(Key.P);
            _keyboard.Hold(Key.P);
            Focus(false);
            yield return null;
            _keyboard.Hold();
            yield return null;
            Focus(true);
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
        }

        [UnityTest]
        public IEnumerator SceneReload_StartsAtTitle()
        {
            yield return StartMatch();
            yield return SceneManager.LoadSceneAsync("Maze");
            yield return null;
            _game = Object.FindAnyObjectByType<MazeGame>();
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(_game.Flow.Game, Is.Null);
        }
    }
}
