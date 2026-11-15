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
    /// <summary>実際のシーンを読み込み、仮想キーボードの入力から画面遷移とパドル操作までの配線を確認する。</summary>
    public sealed class PongSceneTests
    {
        private TestKeyboard _keyboard;
        private PongGame _game;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            _keyboard = new TestKeyboard();
            yield return SceneManager.LoadSceneAsync("Pong");
            _game = Object.FindAnyObjectByType<PongGame>();
        }

        [TearDown]
        public void RestoreInput()
        {
            _keyboard.Dispose();
        }

        [UnityTest]
        public IEnumerator Scene_StartsAtTitle()
        {
            yield return null;

            Assert.That(_game, Is.Not.Null);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [UnityTest]
        public IEnumerator Audio_IsMutedOnlyInBatchMode()
        {
            yield return null;

            TestContext.Out.WriteLine($"isBatchMode={Application.isBatchMode}");
            Assert.That(_game.GetComponent<AudioSource>().mute, Is.EqualTo(Application.isBatchMode));
        }

        [UnityTest]
        public IEnumerator AdvanceTicks_DrawsExactlyThatTick()
        {
            yield return null;
            _game.Flow.Submit();

            _game.AdvanceTicks(100); // サーブ（60 Tick）後、ボールが飛んでいる途中

            var current = _game.Flow.Current;
            Assert.That(_game.Flow.Match!.Ticks, Is.EqualTo(100));
            Assert.That(current.BallInPlay, Is.True);
            var ball = _game.transform.Find("Ball").localPosition;
            Assert.That(ball.x, Is.EqualTo(current.Ball.X), "1 Tick 前との補間ではなく、100 Tick 目の位置を描く");
            Assert.That(ball.y, Is.EqualTo(current.Ball.Y));
        }

        [UnityTest]
        public IEnumerator EnterKey_StartsMatch_EscapeReturnsToTitle()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));

            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [UnityTest]
        public IEnumerator SpaceKey_StartsMatch()
        {
            yield return _keyboard.Tap(Key.Space);

            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [UnityTest]
        public IEnumerator HoldingW_MovesPlayerPaddleUp()
        {
            yield return _keyboard.Tap(Key.Enter);

            _keyboard.Hold(Key.W);
            yield return new WaitForSeconds(0.3f);
            _keyboard.Hold();
            yield return null;

            Assert.That(_game.Flow.Match!.Left.Y, Is.GreaterThan(0f));
        }

        [UnityTest]
        public IEnumerator MatchEnd_ShowsWinnerOnResultOverlay_ThenEnterRematches()
        {
            yield return _keyboard.Tap(Key.Enter);

            // 何もしなければ CPU が勝つ。時間を早送りして決着まで進める（1 フレームの Tick 数は上限で抑えられる）。
            Time.timeScale = 20f;
            try
            {
                var deadline = Time.realtimeSinceStartup + 120f;
                while (_game.Flow.Screen == AppScreen.Playing && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
            }
            finally
            {
                Time.timeScale = 1f;
            }

            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            yield return null;
            var root = _game.HudRoot;
            var expected = _game.Flow.Match!.Winner == Side.Left ? "YOU WIN" : "CPU WINS";
            Assert.That(root.Q<VisualElement>("overlay").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q<Label>("headline").text, Is.EqualTo(expected));

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_game.Flow.Match.LeftScore + _game.Flow.Match.RightScore, Is.Zero);
        }
    }
}
