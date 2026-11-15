using System.Collections;
using Game.App;
using Game.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Game.Unity.Tests
{
    public sealed class SnakeSceneTests
    {
        private TestKeyboard _keyboard;
        private SnakeGame _game;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            _keyboard = new TestKeyboard();
            yield return SceneManager.LoadSceneAsync("Snake");
            _game = Object.FindAnyObjectByType<SnakeGame>();
            _game.ManualTicks = true;
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
        }

        [TearDown]
        public void RestoreKeyboard() => _keyboard.Dispose();

        [UnityTest]
        public IEnumerator PauseKeyAndFocusLoss_InSameFrame_StayPausedUntilManualResume()
        {
            _game.Flow.Submit();
            _game.Flow.RequestDirection(Direction.Up);
            _keyboard.Hold(Key.P);
            InputSystem.Update(); // 停止要求を先に立て、Updateより前にフォーカス喪失を届ける。
            _game.SendMessage("OnApplicationFocus", false);
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(_game.Flow.PendingTurns, Is.Zero);
            var tick = _game.Flow.Game.TickCount;
            _keyboard.Hold();
            InputSystem.Update();
            yield return _keyboard.Tap(Key.Escape); // フォーカスのない間の再開入力も無視。
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(_game.Flow.Game.TickCount, Is.EqualTo(tick));
            _game.SendMessage("OnApplicationFocus", true);
            yield return null;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            yield return _keyboard.Tap(Key.P);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_game.Flow.Game.TickCount, Is.EqualTo(tick));
        }

        [UnityTest]
        public IEnumerator FocusLossBeforePauseKey_AlsoStaysPaused()
        {
            _game.Flow.Submit();
            _game.SendMessage("OnApplicationFocus", false);
            yield return _keyboard.Tap(Key.P);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            _game.SendMessage("OnApplicationFocus", true);
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [UnityTest]
        public IEnumerator Title_Start_Turn_Pause_Resume()
        {
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("SNAKE"));
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            var head = _game.Flow.Game.Head;
            yield return _keyboard.Tap(Key.W);
            Assert.That(_game.Flow.PendingTurns, Is.EqualTo(1));
            _game.Flow.StepOneTick();
            _game.Render();
            Assert.That(_game.Flow.Game.Head, Is.EqualTo(head.Move(Direction.Up)));
            var sprite = _game.transform.Find("Segment0");
            Assert.That(sprite.localPosition, Is.EqualTo(new Vector3(head.X, head.Y + 1, 0)));
            yield return _keyboard.Tap(Key.P);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Paused));
            var tick = _game.Flow.Game.TickCount;
            _game.Flow.Advance(10);
            yield return _keyboard.Tap(Key.A);
            Assert.That(_game.Flow.Game.TickCount, Is.EqualTo(tick));
            Assert.That(_game.Flow.PendingTurns, Is.Zero);
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("PAUSED"));
            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [UnityTest]
        public IEnumerator Result_Replay_AndHudMatchState()
        {
            yield return _keyboard.Tap(Key.Space);
            while (_game.Flow.Screen == AppScreen.Playing) { _game.Step(Direction.Right); }
            yield return null;
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("GAME OVER"));
            Assert.That(_game.HudRoot.Q<Label>("score").text, Is.EqualTo("SCORE  " + _game.Flow.Game.Score));
            var tick = _game.Flow.Game.TickCount;
            yield return _keyboard.Tap(Key.W);
            _game.Step(Direction.Up);
            Assert.That(_game.Flow.Game.TickCount, Is.EqualTo(tick));
            var seed = _game.Flow.GameSeed;
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_game.Flow.GameSeed, Is.EqualTo(unchecked(seed + 1)));
            Assert.That(_game.Flow.Game.Score, Is.Zero);
        }

        [UnityTest]
        public IEnumerator FixedSeed_Wins_AndShowsAllSegments()
        {
            // 結果は実行ごとのシードに依存しない巡回路で作る。
            _game.Flow.Submit();
            for (var i = 0; i < 110000 && _game.Flow.Screen == AppScreen.Playing; i++)
            {
                _game.Flow.RequestDirection(Autopilot.Next(_game.Flow.Game));
                _game.Flow.StepOneTick();
            }
            Assert.That(_game.Flow.Game.Phase, Is.EqualTo(GamePhase.Won));
            _game.Render();
            yield return null;
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("YOU WIN"));
            for (var i = 0; i < _game.Flow.Game.Length; i++)
            {
                var renderer = _game.transform.Find("Segment" + i).GetComponent<SpriteRenderer>();
                var cell = _game.Flow.Game.SegmentAt(i);
                Assert.That(renderer.enabled, Is.True);
                Assert.That(renderer.transform.localPosition, Is.EqualTo(new Vector3(cell.X, cell.Y, 0)));
            }
            Assert.That(_game.transform.Find("Food").GetComponent<SpriteRenderer>().enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator Camera_FitsLandscapeBoards_AndAudioPolicy()
        {
            var camera = Camera.main;
            foreach (var aspect in new[] { 16f / 10f, 16f / 9f, 21f / 9f })
            {
                camera.aspect = aspect;
                _game.Render();
                var bottom = camera.WorldToViewportPoint(new Vector3(-.5f, -.5f, 0));
                var top = camera.WorldToViewportPoint(new Vector3(19.5f, 15.5f, 0));
                Assert.That(bottom.x, Is.GreaterThanOrEqualTo(0));
                Assert.That(bottom.y, Is.GreaterThanOrEqualTo(0));
                Assert.That(top.x, Is.LessThanOrEqualTo(1));
                Assert.That(top.y, Is.LessThanOrEqualTo(.94f));
            }
            camera.ResetAspect();
            Assert.That(_game.GetComponent<AudioSource>().mute, Is.EqualTo(Application.isBatchMode));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneReload_DestroysGeneratedResources_AndStartsAtTitle()
        {
            var texture = _game.transform.Find("Board").GetComponent<SpriteRenderer>().sprite.texture;
            var oldGame = _game;
            yield return SceneManager.LoadSceneAsync("Snake");
            yield return null;
            Assert.That(oldGame == null, Is.True);
            Assert.That(texture == null, Is.True);
            _game = Object.FindAnyObjectByType<SnakeGame>();
            _game.ManualTicks = true;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
        }
    }
}
