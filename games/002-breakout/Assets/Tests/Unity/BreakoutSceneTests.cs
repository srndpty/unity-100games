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
    /// <summary>実際のシーンを読み込み、仮想キーボードの入力から画面遷移・発射・パドル操作、描画と HUD までの配線を確認する。</summary>
    public sealed class BreakoutSceneTests
    {
        private const int MaxTicks = 60 * 60 * 30;

        private TestKeyboard _keyboard;
        private BreakoutGame _game;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            _keyboard = new TestKeyboard();
            yield return SceneManager.LoadSceneAsync("Breakout");
            _game = Object.FindAnyObjectByType<BreakoutGame>();
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
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("BREAKOUT"));
        }

        [UnityTest]
        public IEnumerator Audio_IsMutedOnlyInBatchMode()
        {
            yield return null;

            TestContext.Out.WriteLine($"isBatchMode={Application.isBatchMode}");
            Assert.That(_game.GetComponent<AudioSource>().mute, Is.EqualTo(Application.isBatchMode));
        }

        [UnityTest]
        public IEnumerator EnterStarts_SpaceLaunches_EscapeReturnsToTitle()
        {
            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_game.Flow.Game!.Phase, Is.EqualTo(GamePhase.Serving));
            yield return null;
            StringAssert.Contains("launch", _game.HudRoot.Q<Label>("banner").text);

            yield return _keyboard.Tap(Key.Space);
            yield return WaitForTicks(2);
            Assert.That(_game.Flow.Game.Phase, Is.EqualTo(GamePhase.Playing));

            yield return _keyboard.Tap(Key.Escape);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [UnityTest]
        public IEnumerator HoldingD_MovesPaddleRight_AndHeldBallFollows()
        {
            yield return _keyboard.Tap(Key.Enter);

            _keyboard.Hold(Key.D);
            yield return new WaitForSeconds(0.3f);
            _keyboard.Hold();
            yield return null;

            var game = _game.Flow.Game!;
            Assert.That(game.Paddle.X, Is.GreaterThan(0f));
            Assert.That(game.Ball.Position.X, Is.EqualTo(game.Paddle.X));
            Assert.That(_game.transform.Find("Paddle").localPosition.x, Is.GreaterThan(0f));
        }

        [UnityTest]
        public IEnumerator AdvanceUntil_DrawsExactlyThatTick()
        {
            yield return null;
            _game.Flow.Submit();

            _game.AdvanceUntil(s => s.Ticks >= 30, MaxTicks, _ => 0f);

            var current = _game.Flow.Current;
            Assert.That(_game.Flow.Game!.Ticks, Is.EqualTo(30));
            Assert.That(current.BallInPlay, Is.True);
            var ball = _game.transform.Find("Ball").localPosition;
            Assert.That(ball.x, Is.EqualTo(current.Ball.X), "1 Tick 前との補間ではなく、30 Tick 目の位置を描く");
            Assert.That(ball.y, Is.EqualTo(current.Ball.Y));
        }

        [UnityTest]
        public IEnumerator DestroyedBrick_IsHidden()
        {
            yield return null;
            _game.Flow.Submit();
            var total = _game.Flow.Game!.Field.Bricks.Count;

            // 1 面目の最下段は端まで埋まっているので、最初の打ち上げで必ずどれかに当たる。
            _game.AdvanceUntil(s => s.Field.AliveCount < total, MaxTicks, _ => 0f);

            var field = _game.Flow.Game.Field;
            Assert.That(field.AliveCount, Is.EqualTo(total - 1));
            foreach (var brick in field.Bricks)
            {
                var shown = _game.transform.Find($"Brick {brick.Index}").gameObject.activeSelf;
                Assert.That(shown, Is.EqualTo(brick.IsAlive), $"ブロック {brick.Index}");
            }
        }

        [UnityTest]
        public IEnumerator ActiveEffect_IsShownWithRemainingTime()
        {
            yield return null;
            _game.Flow.Submit();
            _game.AdvanceUntil(s => s.Phase == GamePhase.Playing, MaxTicks, _ => 0f);
            var game = _game.Flow.Game!;

            game.Effects.Activate(EffectKind.Pierce);
            _game.AdvanceUntil(_ => true, 1, _ => 0f);
            yield return null;

            var pierce = _game.HudRoot.Q<Label>("pierce");
            Assert.That(pierce.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(pierce.text, Is.EqualTo("PIERCE  8.0 s"));
            Assert.That(_game.HudRoot.Q<Label>("expand").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(_game.transform.Find("Ball").GetComponent<SpriteRenderer>().color, Is.Not.EqualTo(Color.white), "貫通中はボールの色が変わる");
        }

        [UnityTest]
        public IEnumerator GameEnd_ShowsResultMatchingOutcome_ThenEnterStartsNewGame()
        {
            yield return null;
            _game.Flow.Submit();
            var autopilot = new Autopilot(5);

            _game.AdvanceUntil(_ => false, MaxTicks, autopilot.Decide);
            yield return null;

            var game = _game.Flow.Game!;
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(_game.HudRoot.Q<VisualElement>("overlay").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo(game.Phase == GamePhase.Won ? "YOU WIN" : "GAME OVER"));
            StringAssert.Contains(game.Score.ToString(), _game.HudRoot.Q<Label>("message").text);

            yield return _keyboard.Tap(Key.Enter);
            Assert.That(_game.Flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_game.Flow.Game, Is.Not.SameAs(game));
            Assert.That(_game.Flow.Game!.Score, Is.Zero);
        }

        [UnityTest]
        public IEnumerator LosingAllLives_ShowsGameOver()
        {
            yield return null;
            _game.Flow.Submit();

            _game.AdvanceUntil(_ => false, MaxTicks, MissOnPurpose);
            yield return null;

            Assert.That(_game.Flow.Game!.Phase, Is.EqualTo(GamePhase.Lost));
            Assert.That(_game.HudRoot.Q<Label>("headline").text, Is.EqualTo("GAME OVER"));
            Assert.That(_game.HudRoot.Q<Label>("lives").text, Is.EqualTo("0"));
        }

        // ブロックより下を落ちてくるボールの着地点（左右の壁での反射を折り返して求める）から、反対側の端へ逃げる。
        private static float MissOnPurpose(BreakoutState state)
        {
            var ball = state.Ball;
            if (ball.Velocity.Y >= 0f || ball.Position.Y > 3f)
            {
                return 0f;
            }

            var limit = (state.Config.FieldWidth / 2f) - ball.HalfSize;
            var time = (ball.Position.Y - (state.Paddle.Top + ball.HalfSize)) / -ball.Velocity.Y;
            var period = 4f * limit;
            var unfolded = Mathf.Repeat(ball.Position.X + (ball.Velocity.X * time) + limit, period);
            var landing = unfolded <= 2f * limit ? unfolded - limit : (3f * limit) - unfolded;
            var target = landing > 0f ? -limit : limit;
            return Mathf.Clamp((target - state.Paddle.X) * 8f, -1f, 1f);
        }

        private IEnumerator WaitForTicks(int ticks)
        {
            var target = _game.Flow.Game!.Ticks + ticks;
            var deadline = Time.realtimeSinceStartup + 5f;
            while (_game.Flow.Game.Ticks < target && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }
    }
}
