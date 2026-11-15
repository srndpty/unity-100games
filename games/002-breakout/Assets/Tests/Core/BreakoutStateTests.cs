using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class BreakoutStateTests
    {
        // 真上に打つとボールは x = 0（列 5 と 6 の継ぎ目）を往復し、2 回で 2 個とも壊れる。
        private const string Pair = ".....NN.....";

        // 列 3 のブロックには、パドルの中央・両端（x = 0、±5）からの真上の打球が当たらないので、面が終わらない。
        private const string Unreachable = "...N........";

        [Test]
        public void NewGame_StartsServingOnFirstStage()
        {
            var state = TestStages.NewState(new BreakoutConfig());

            Assert.That(state.Phase, Is.EqualTo(GamePhase.Serving));
            Assert.That(state.StageIndex, Is.EqualTo(0));
            Assert.That(state.Lives, Is.EqualTo(3));
            Assert.That(state.Score, Is.EqualTo(0));
            Assert.That(state.Ball.InPlay, Is.False);
            Assert.That(state.Events, Is.EqualTo(new[] { new GameEvent(GameEventKind.StageStarted, 0) }));
        }

        [Test]
        public void Serving_BallFollowsPaddleUntilLaunch()
        {
            var state = TestStages.NewState(TestStages.Config(Unreachable));

            state.Tick(1f, launch: false);
            state.Tick(1f, launch: false);

            Assert.That(state.Phase, Is.EqualTo(GamePhase.Serving));
            Assert.That(state.Ball.Position.X, Is.EqualTo(state.Paddle.X));
            Assert.That(state.Ball.Position.Y, Is.EqualTo(state.Paddle.Top + state.Ball.HalfSize));

            state.Tick(0f, launch: true);

            Assert.That(state.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(state.Events, Is.EqualTo(new[] { new GameEvent(GameEventKind.Launched) }));
        }

        [Test]
        public void LaunchWhilePlaying_IsIgnored()
        {
            var state = TestStages.NewState(TestStages.Config(Unreachable));
            state.Tick(0f, launch: true);
            var velocity = state.Ball.Velocity;

            state.Tick(0f, launch: true);

            Assert.That(state.Events, Has.None.Matches<GameEvent>(e => e.Kind == GameEventKind.Launched));
            Assert.That(state.Ball.Velocity, Is.EqualTo(velocity));
        }

        [Test]
        public void ClearingOnlyStage_WinsOnce()
        {
            var state = TestStages.NewState(TestStages.Config(Pair));

            var events = TestStages.RunUntil(state, s => s.IsOver);

            Assert.That(state.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(Count(events, GameEventKind.BrickDestroyed), Is.EqualTo(2));
            Assert.That(Count(events, GameEventKind.StageCleared), Is.EqualTo(1));
            Assert.That(Count(events, GameEventKind.GameWon), Is.EqualTo(1));
            Assert.That(state.Score, Is.EqualTo(20));
        }

        [Test]
        public void ClearingStage_AdvancesToNextStageAfterDelay()
        {
            var config = TestStages.Configs(new StageDefinition(Pair), new StageDefinition(Unreachable, Pair));
            var state = TestStages.NewState(config);

            var events = TestStages.RunUntil(state, s => s.Phase == GamePhase.StageClear);
            Assert.That(Count(events, GameEventKind.StageCleared), Is.EqualTo(1));
            Assert.That(state.Ball.InPlay, Is.False);

            var waited = 0;
            while (state.Phase == GamePhase.StageClear)
            {
                state.Tick(0f, launch: true);
                waited++;
                Assert.That(state.Events, Has.None.Matches<GameEvent>(e => e.Kind == GameEventKind.StageCleared), "面クリアは一度だけ");
            }

            Assert.That(waited, Is.EqualTo(config.StageClearDelayTicks));
            Assert.That(state.Events, Is.EqualTo(new[] { new GameEvent(GameEventKind.StageStarted, 1) }));
            Assert.That(state.Phase, Is.EqualTo(GamePhase.Serving));
            Assert.That(state.StageIndex, Is.EqualTo(1));
            Assert.That(state.Field.AliveCount, Is.EqualTo(3));
            Assert.That(state.Paddle.X, Is.EqualTo(0f));
        }

        [Test]
        public void PierceBreakingLastTwoBricksInOneTick_ClearsStageOnce()
        {
            var state = TestStages.NewState(TestStages.Config(Pair));
            state.Tick(0f, launch: true);
            state.Effects.Activate(EffectKind.Pierce);

            var events = TestStages.RunUntil(state, s => s.IsOver);

            Assert.That(state.Events, Is.EqualTo(new[]
            {
                new GameEvent(GameEventKind.BrickDestroyed, 0),
                new GameEvent(GameEventKind.BrickDestroyed, 1),
                new GameEvent(GameEventKind.StageCleared, 0),
                new GameEvent(GameEventKind.GameWon),
            }));
            Assert.That(Count(events, GameEventKind.StageCleared), Is.EqualTo(1));
        }

        [Test]
        public void ClearingFinalStage_WinsAfterAllStages()
        {
            var state = TestStages.NewState(TestStages.Configs(new StageDefinition(Pair), new StageDefinition(Pair), new StageDefinition(Pair)));

            var events = TestStages.RunUntil(state, s => s.IsOver);

            Assert.That(state.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(state.StageIndex, Is.EqualTo(2));
            Assert.That(Count(events, GameEventKind.StageCleared), Is.EqualTo(3));
            Assert.That(Count(events, GameEventKind.GameWon), Is.EqualTo(1));
        }

        [Test]
        public void MissingBall_LosesLifeAndServesAgain()
        {
            var state = TestStages.NewState(TestStages.Config(Unreachable));

            TestStages.RunUntil(state, s => s.Lives < 3, Dodge);

            Assert.That(state.Lives, Is.EqualTo(2));
            Assert.That(state.Events, Has.Member(new GameEvent(GameEventKind.LifeLost, 2)));
            Assert.That(state.Phase, Is.EqualTo(GamePhase.Serving));
            Assert.That(state.Ball.InPlay, Is.False);
            Assert.That(state.Ball.Position.X, Is.EqualTo(state.Paddle.X));
        }

        [Test]
        public void LosingLastLife_EndsGameAndIgnoresFurtherInput()
        {
            var state = TestStages.NewState(TestStages.Config(Unreachable));

            var events = TestStages.RunUntil(state, s => s.IsOver, Dodge);

            Assert.That(state.Phase, Is.EqualTo(GamePhase.Lost));
            Assert.That(state.Lives, Is.EqualTo(0));
            Assert.That(Count(events, GameEventKind.LifeLost), Is.EqualTo(3));
            Assert.That(Count(events, GameEventKind.GameOver), Is.EqualTo(1));

            var ticks = state.Ticks;
            var paddleX = state.Paddle.X;
            state.Tick(1f, launch: true);

            Assert.That(state.Events, Is.Empty);
            Assert.That(state.Ticks, Is.EqualTo(ticks));
            Assert.That(state.Paddle.X, Is.EqualTo(paddleX));
            Assert.That(state.Phase, Is.EqualTo(GamePhase.Lost));
        }

        [Test]
        public void CatchingCapsule_ActivatesEffectForItsDuration()
        {
            // 真上の打球が列 6 の拡張ブロックを壊し、カプセルは中央のパドルに落ちる。
            var state = TestStages.NewState(TestStages.Config("N.....E....."));

            var events = TestStages.RunUntil(state, s => s.Effects.IsActive(EffectKind.Expand));

            Assert.That(events, Has.Member(new GameEvent(GameEventKind.CapsuleDropped, (int)EffectKind.Expand)));
            Assert.That(state.Events, Has.Member(new GameEvent(GameEventKind.EffectStarted, (int)EffectKind.Expand)));
            Assert.That(state.Capsules, Is.Empty);
            Assert.That(state.Paddle.HalfWidth, Is.EqualTo(1.5f));
            Assert.That(state.Score, Is.EqualTo(state.Config.ItemScore));

            var activeTicks = 0;
            do
            {
                state.Tick(0f, launch: false);
                activeTicks++;
            }
            while (state.Effects.IsActive(EffectKind.Expand));

            Assert.That(activeTicks, Is.EqualTo(state.Config.ExpandDurationTicks));
            Assert.That(state.Events, Has.Member(new GameEvent(GameEventKind.EffectExpired, (int)EffectKind.Expand)));
            Assert.That(state.Paddle.HalfWidth, Is.EqualTo(1f));
        }

        [Test]
        public void CatchingPierceCapsule_ActivatesPierce()
        {
            var state = TestStages.NewState(TestStages.Config("N.....P....."));

            TestStages.RunUntil(state, s => s.Effects.IsActive(EffectKind.Pierce));

            Assert.That(state.Effects.Remaining(EffectKind.Pierce), Is.EqualTo(state.Config.PierceDurationTicks));
        }

        [Test]
        public void LosingLife_ClearsEffectsAndCapsules()
        {
            var state = TestStages.NewState(TestStages.Config(Unreachable));
            state.Tick(0f, launch: true);
            state.Effects.Activate(EffectKind.Pierce);
            state.Effects.Activate(EffectKind.Expand);

            var events = TestStages.RunUntil(state, s => s.Lives < 3, Dodge);

            Assert.That(state.Effects.IsActive(EffectKind.Pierce), Is.False);
            Assert.That(state.Effects.IsActive(EffectKind.Expand), Is.False);
            Assert.That(Count(events, GameEventKind.EffectExpired), Is.EqualTo(0), "一括解除では時間切れを出さない");
        }

        [Test]
        public void SameSeedAndInputs_ProduceSameGame()
        {
            var inputs = RecordInputs(seed: 7, count: 20000);

            var first = Replay(seed: 42, inputs);
            var second = Replay(seed: 42, inputs);
            var other = Replay(seed: 43, inputs);

            Assert.That(second, Is.EqualTo(first));
            Assert.That(other, Is.Not.EqualTo(first), "シードが違えば発射角が変わる");
        }

        [Test]
        public void AutoPlayer_ClearsAllDefaultStages()
        {
            // ボールを追い、打ち返すたびに乱数で選んだずれで当てて角度をばらつかせる（周期的な往復に入らないように）。
            var state = TestStages.NewState(new BreakoutConfig());
            var aim = new SeededRandom(1);
            var offset = 0f;
            var rising = false;
            float Track(BreakoutState s)
            {
                var up = s.Ball.Velocity.Y > 0f;
                if (up && !rising)
                {
                    offset = aim.Range(-0.8f, 0.8f) * s.Paddle.HalfWidth;
                }

                rising = up;
                return Math.Clamp((s.Ball.Position.X - offset - s.Paddle.X) * 8f, -1f, 1f);
            }

            var events = TestStages.RunUntil(state, s => s.IsOver, Track, maxTicks: 60 * 60 * 30);

            Assert.That(state.Phase, Is.EqualTo(GamePhase.Won), $"残機 {state.Lives}、面 {state.StageIndex + 1}");
            Assert.That(Count(events, GameEventKind.StageCleared), Is.EqualTo(3));
            TestContext.WriteLine($"{state.Ticks} Tick で全面クリア");
        }

        // 打球の着地点から逃げる（真上に打った後、遠い方の壁へ向かう）。
        private static float Dodge(BreakoutState state) =>
            state.Phase != GamePhase.Playing ? 0f : state.Ball.Position.X > 0f ? -1f : 1f;

        private static (int Score, int Lives, GamePhase Phase, int Stage, long Ticks, Vec2 Ball, int Alive) Replay(int seed, List<(float Move, bool Launch)> inputs)
        {
            var state = new BreakoutState(new BreakoutConfig(), seed);
            foreach (var (move, launch) in inputs)
            {
                state.Tick(move, launch);
            }

            return (state.Score, state.Lives, state.Phase, state.StageIndex, state.Ticks, state.Ball.Position, state.Field.AliveCount);
        }

        // 人間の操作を模した入力列（同じ向きにしばらく押し続け、ときどき発射を押す）。
        private static List<(float Move, bool Launch)> RecordInputs(int seed, int count)
        {
            var random = new SeededRandom(seed);
            var inputs = new List<(float, bool)>(count);
            var current = 0f;
            for (var i = 0; i < count; i++)
            {
                if (i % 20 == 0)
                {
                    current = random.Range(-1f, 1f);
                }

                inputs.Add((current, i % 90 == 0));
            }

            return inputs;
        }

        private static int Count(List<GameEvent> events, GameEventKind kind) => events.FindAll(e => e.Kind == kind).Count;
    }
}
