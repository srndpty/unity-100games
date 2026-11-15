using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum GamePhase
    {
        /// <summary>ボールがパドルの上で発射を待っている。</summary>
        Serving,
        Playing,

        /// <summary>面をクリアし、次の面を待っている。</summary>
        StageClear,

        /// <summary>最終面をクリアした（終了）。</summary>
        Won,

        /// <summary>残機がなくなった（終了）。</summary>
        Lost,
    }

    /// <summary>
    /// 1 ゲーム（1 面目から勝利・ゲームオーバーまで）の状態とルール。固定刻みの <see cref="Tick"/> でのみ進行し、描画や実時間には依存しない。
    /// 乱数は発射角にだけ使い、シードから決まるため、同じシードと同じ入力列なら同じ結果になる。
    /// 1 インスタンス = 1 ゲーム。再戦は新しいシードで作り直す。
    /// <para>1 Tick の処理順：パドル移動 → ボール（<see cref="CollisionResolver"/>）→ 面クリア判定 → カプセル → 効果の時間経過 → ミス判定。</para>
    /// </summary>
    public sealed class BreakoutState
    {
        private const float DegreesToRadians = MathF.PI / 180f;

        private readonly SeededRandom _random;
        private readonly StageLayout[] _layouts;
        private readonly CollisionResolver _resolver;
        private readonly List<GameEvent> _events = new();
        private readonly List<Capsule> _capsules = new();
        private readonly List<EffectKind> _expired = new();
        private int _stageClearTicksRemaining;

        public BreakoutState(BreakoutConfig config, int seed)
        {
            config.Validate();
            Config = config;
            _random = new SeededRandom(seed);
            _layouts = new StageLayout[config.Stages.Length];
            for (var i = 0; i < _layouts.Length; i++)
            {
                _layouts[i] = StageParser.Parse(config.Stages[i], config.Columns, i);
            }

            _resolver = new CollisionResolver(config);
            Paddle = new Paddle(config);
            Ball = new Ball(config);
            Effects = new EffectTimers(config);
            Lives = config.Lives;
            Field = StartStage(0);
        }

        public BreakoutConfig Config { get; }

        public Paddle Paddle { get; }

        public Ball Ball { get; }

        public EffectTimers Effects { get; }

        public BrickField Field { get; private set; }

        public IReadOnlyList<Capsule> Capsules => _capsules;

        public GamePhase Phase { get; private set; }

        /// <summary>今の面の番号（0 始まり）。</summary>
        public int StageIndex { get; private set; }

        public int StageCount => _layouts.Length;

        public int Lives { get; private set; }

        public int Score { get; private set; }

        /// <summary>ゲーム開始からの Tick 数。</summary>
        public long Ticks { get; private set; }

        public bool IsOver => Phase is GamePhase.Won or GamePhase.Lost;

        /// <summary>直前の <see cref="Tick"/>（またはゲーム開始）で起きた出来事。</summary>
        public IReadOnlyList<GameEvent> Events => _events;

        /// <summary>
        /// 1 Tick 進める。<paramref name="move"/> は -1（左）〜 +1（右）。<paramref name="launch"/> は発射を待っているときだけ効く。
        /// 終了後は何もしない。
        /// </summary>
        public void Tick(float move, bool launch)
        {
            _events.Clear();
            if (IsOver)
            {
                return;
            }

            Ticks++;
            if (Phase == GamePhase.StageClear)
            {
                _stageClearTicksRemaining--;
                if (_stageClearTicksRemaining <= 0)
                {
                    Field = StartStage(StageIndex + 1);
                }

                return;
            }

            Paddle.Move(move);
            if (Phase == GamePhase.Serving)
            {
                HoldBallOnPaddle();
                if (launch)
                {
                    Launch();
                }

                return;
            }

            var firstEvent = _events.Count;
            _resolver.Step(Ball, Paddle, Field, Effects.IsActive(EffectKind.Pierce), _events);
            for (var i = firstEvent; i < _events.Count; i++)
            {
                if (_events[i].Kind == GameEventKind.BrickDestroyed)
                {
                    OnBrickDestroyed(Field.Bricks[_events[i].Value]);
                }
            }

            // 最後のブロックを壊した Tick は、同じ Tick にボールを落としても面クリアを優先する。
            if (Field.AliveCount == 0)
            {
                ClearStage();
                return;
            }

            StepCapsules();
            AdvanceEffects();
            if (Ball.Position.Y + Ball.HalfSize < -Config.FieldHeight / 2f)
            {
                LoseLife();
            }
        }

        private BrickField StartStage(int stageIndex)
        {
            StageIndex = stageIndex;
            Phase = GamePhase.Serving;
            Paddle.Center();
            HoldBallOnPaddle();
            _events.Add(new GameEvent(GameEventKind.StageStarted, stageIndex));
            return new BrickField(Config, _layouts[stageIndex]);
        }

        private void HoldBallOnPaddle() => Ball.Hold(new Vec2(Paddle.X, Paddle.Top + Ball.HalfSize));

        private void Launch()
        {
            var maxAngle = Config.MaxServeAngleDegrees * DegreesToRadians;
            var angle = _random.Range(-maxAngle, maxAngle);
            Ball.Launch(Ball.Position, new Vec2(MathF.Sin(angle), MathF.Cos(angle)) * Config.LaunchSpeed(StageIndex));
            Phase = GamePhase.Playing;
            _events.Add(new GameEvent(GameEventKind.Launched));
        }

        private void OnBrickDestroyed(Brick brick)
        {
            Score += Config.ScoreOf(brick.Kind);
            if (brick.Kind.DropsEffect() is EffectKind effect)
            {
                _capsules.Add(new Capsule(effect, brick.Center));
                _events.Add(new GameEvent(GameEventKind.CapsuleDropped, (int)effect));
            }
        }

        private void StepCapsules()
        {
            var fall = Config.CapsuleSpeed * Config.TickDuration;
            for (var i = _capsules.Count - 1; i >= 0; i--)
            {
                var capsule = _capsules[i];
                capsule.Fall(fall);
                if (capsule.Touches(Paddle, Config))
                {
                    _capsules.RemoveAt(i);
                    Activate(capsule.Effect);
                }
                else if (capsule.IsBelow(Config))
                {
                    _capsules.RemoveAt(i);
                }
            }
        }

        private void Activate(EffectKind effect)
        {
            Effects.Activate(effect);
            if (effect == EffectKind.Expand)
            {
                Paddle.SetExpanded(true);
            }

            _events.Add(new GameEvent(GameEventKind.EffectStarted, (int)effect));
        }

        private void AdvanceEffects()
        {
            _expired.Clear();
            Effects.Advance(_expired);
            foreach (var effect in _expired)
            {
                if (effect == EffectKind.Expand)
                {
                    Paddle.SetExpanded(false);
                }

                _events.Add(new GameEvent(GameEventKind.EffectExpired, (int)effect));
            }
        }

        // ミス・面クリアでは、落下中のカプセルと有効中の効果をすべて消す。
        private void ResetPowerUps()
        {
            _capsules.Clear();
            Effects.Clear();
            Paddle.SetExpanded(false);
        }

        private void ClearStage()
        {
            ResetPowerUps();
            HoldBallOnPaddle();
            _events.Add(new GameEvent(GameEventKind.StageCleared, StageIndex));
            if (StageIndex == StageCount - 1)
            {
                Phase = GamePhase.Won;
                _events.Add(new GameEvent(GameEventKind.GameWon));
                return;
            }

            Phase = GamePhase.StageClear;
            _stageClearTicksRemaining = Config.StageClearDelayTicks;
        }

        private void LoseLife()
        {
            Lives--;
            ResetPowerUps();
            HoldBallOnPaddle();
            _events.Add(new GameEvent(GameEventKind.LifeLost, Lives));
            if (Lives > 0)
            {
                Phase = GamePhase.Serving;
                return;
            }

            Phase = GamePhase.Lost;
            _events.Add(new GameEvent(GameEventKind.GameOver));
        }
    }
}
