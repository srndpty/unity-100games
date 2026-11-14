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

namespace Game.Unity.Tests
{
    /// <summary>
    /// 実際のシーンを読み込み、キーボード入力から画面遷移とパドル操作までの配線を確認する。
    /// プロジェクト全体の Input Actions をそのまま使うため、InputTestFixture（入力システムを初期化し直す）は使わず、
    /// 仮想キーボードを追加して状態イベントを送る。
    /// </summary>
    public sealed class PongSceneTests
    {
        private Keyboard _keyboard;
        private PongGame _game;
        private InputSettings.BackgroundBehavior _backgroundBehavior;
        private InputSettings.EditorInputBehaviorInPlayMode _editorInputBehavior;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            // バッチモードではゲームビューにフォーカスがなく入力が捨てられるため、テスト中だけフォーカスを無視させる。
            var settings = InputSystem.settings;
            _backgroundBehavior = settings.backgroundBehavior;
            _editorInputBehavior = settings.editorInputBehaviorInPlayMode;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            _keyboard = InputSystem.AddDevice<Keyboard>();
            yield return SceneManager.LoadSceneAsync("Pong");
            _game = Object.FindAnyObjectByType<PongGame>();
        }

        [TearDown]
        public void RestoreInput()
        {
            InputSystem.RemoveDevice(_keyboard);
            InputSystem.settings.backgroundBehavior = _backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = _editorInputBehavior;
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
        public IEnumerator EnterKey_StartsMatch_EscapeReturnsToTitle()
        {
            yield return Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));

            yield return Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [UnityTest]
        public IEnumerator SpaceKey_StartsMatch()
        {
            yield return Tap(Key.Space);

            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [UnityTest]
        public IEnumerator HoldingW_MovesPlayerPaddleUp()
        {
            yield return Tap(Key.Enter);

            Hold(Key.W);
            yield return new WaitForSeconds(0.3f);
            Hold();
            yield return null;

            Assert.That(_game.Flow.Match!.Left.Y, Is.GreaterThan(0f));
        }

        [UnityTest]
        public IEnumerator MatchEnd_ShowsWinnerOnResultOverlay_ThenEnterRematches()
        {
            yield return Tap(Key.Enter);

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
            var root = _game.GetComponent<UIDocument>().rootVisualElement;
            var expected = _game.Flow.Match!.Winner == Side.Left ? "YOU WIN" : "CPU WINS";
            Assert.That(root.Q<VisualElement>("overlay").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(root.Q<Label>("headline").text, Is.EqualTo(expected));

            yield return Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_game.Flow.Match.LeftScore + _game.Flow.Match.RightScore, Is.Zero);
        }

        private IEnumerator Tap(Key key)
        {
            Hold(key);
            yield return null;
            Hold();
            yield return null;
        }

        // 指定したキーだけが押されている状態にする（引数なしで全キーを離す）。
        private void Hold(params Key[] keys)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(keys));
        }
    }
}
