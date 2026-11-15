using System;
using System.Collections.Generic;

namespace Game.Core.Tests
{
    /// <summary>
    /// テスト用の設定と進行ヘルパー。既定の設定はフィールド 12 × 16、ブロック 1 × 0.5、最上段の上端 y = 6.5。
    /// 列 c の x 範囲は [-6 + c, -5 + c]（列 5 と 6 の継ぎ目が x = 0）、段 r の y 範囲は [6 - 0.5r, 6.5 - 0.5r]。
    /// </summary>
    internal static class TestStages
    {
        public const int Seed = 12345;

        /// <summary>通しプレイが進行しているとみなす上限。これを超えたら止まっているとみなす。</summary>
        public const int MaxTicks = 60 * 60 * 10;

        /// <summary>1 面だけの設定。発射は真上（パドル中央 x = 0 から）。</summary>
        public static BreakoutConfig Config(params string[] rows) => Configs(new StageDefinition(rows));

        /// <summary>複数面の設定。発射は真上。</summary>
        public static BreakoutConfig Configs(params StageDefinition[] stages) => new()
        {
            MaxServeAngleDegrees = 0f,
            Stages = stages,
        };

        public static BreakoutState NewState(BreakoutConfig config) => new(config, Seed);

        /// <summary>
        /// <paramref name="until"/> が成り立つまで進め、その間に起きた出来事をすべて返す。発射待ちなら毎回すぐ発射する。
        /// パドルは <paramref name="move"/>（省略時は動かさない）に従う。
        /// </summary>
        public static List<GameEvent> RunUntil(BreakoutState state, Func<BreakoutState, bool> until, Func<BreakoutState, float>? move = null, int maxTicks = MaxTicks)
        {
            var events = new List<GameEvent>();
            for (var i = 0; i < maxTicks; i++)
            {
                state.Tick(move?.Invoke(state) ?? 0f, launch: state.Phase == GamePhase.Serving);
                events.AddRange(state.Events);
                if (until(state))
                {
                    return events;
                }
            }

            throw new InvalidOperationException("条件に達しませんでした");
        }

        /// <summary>CollisionResolver を直接使う場の部品。</summary>
        public sealed class Rig
        {
            public Rig(params string[] rows)
            {
                Config = TestStages.Config(rows);
                Config.Validate();
                Field = new BrickField(Config, StageParser.Parse(Config.Stages[0], Config.Columns, 0));
                Paddle = new Paddle(Config);
                Ball = new Ball(Config);
                Resolver = new CollisionResolver(Config);
            }

            public BreakoutConfig Config { get; }

            public BrickField Field { get; }

            public Paddle Paddle { get; }

            public Ball Ball { get; }

            public CollisionResolver Resolver { get; }

            public List<GameEvent> Events { get; } = new();

            public void Step(bool pierce = false, float paddleMove = 0f)
            {
                Paddle.Move(paddleMove);
                Resolver.Step(Ball, Paddle, Field, pierce, Events);
            }

            /// <summary><paramref name="until"/> が成り立つまで進める。</summary>
            public void StepUntil(Func<Rig, bool> until, bool pierce = false, int maxTicks = 600)
            {
                for (var i = 0; i < maxTicks; i++)
                {
                    Step(pierce);
                    if (until(this))
                    {
                        return;
                    }
                }

                throw new InvalidOperationException("条件に達しませんでした");
            }

            public int Count(GameEventKind kind) => Events.FindAll(e => e.Kind == kind).Count;
        }
    }
}
