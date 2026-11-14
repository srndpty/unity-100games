using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum MatchPhase
    {
        /// <summary>ボールが中央で止まり、サーブを待っている。</summary>
        Serving,
        Playing,
        Finished,
    }

    /// <summary>
    /// 1 試合の状態とルール。固定刻みの <see cref="Tick"/> でのみ進行し、描画や実時間には依存しない。
    /// サーブの乱数はシードから決まるため、同じシードと同じ入力列なら同じ結果になる。
    /// </summary>
    public sealed class MatchState
    {
        private const float DegreesToRadians = MathF.PI / 180f;

        private readonly SeededRandom _serveRandom;
        private readonly List<MatchEvent> _events = new List<MatchEvent>();
        private int _serveTicksRemaining;
        private Side _serveTo;

        public MatchState(PongConfig config, int seed)
        {
            config.Validate();
            Config = config;
            _serveRandom = new SeededRandom(seed);
            Left = new Paddle(config, Side.Left);
            Right = new Paddle(config, Side.Right);
            Ball = new BallSim(config);
            StartMatch();
        }

        public PongConfig Config { get; }

        public Paddle Left { get; }

        public Paddle Right { get; }

        public BallSim Ball { get; }

        public MatchPhase Phase { get; private set; }

        public int LeftScore { get; private set; }

        public int RightScore { get; private set; }

        public Side? Winner { get; private set; }

        /// <summary>試合開始からの累計 Tick 数（再戦でも継続）。</summary>
        public long Ticks { get; private set; }

        /// <summary>直前の <see cref="Tick"/> で起きた出来事。</summary>
        public IReadOnlyList<MatchEvent> Events => _events;

        public MatchSnapshot Snapshot => new MatchSnapshot(Ball.Position, Ball.InPlay, Left.Y, Right.Y);

        public Paddle PaddleOf(Side side) => side == Side.Left ? Left : Right;

        public int ScoreOf(Side side) => side == Side.Left ? LeftScore : RightScore;

        /// <summary>1 Tick 進める。入力は -1（下）〜 +1（上）。決着後は何もしない。</summary>
        public void Tick(float leftInput, float rightInput)
        {
            _events.Clear();
            if (Phase == MatchPhase.Finished)
            {
                return;
            }

            Ticks++;
            var deltaTime = Config.TickDuration;
            Left.Move(leftInput, deltaTime);
            Right.Move(rightInput, deltaTime);

            if (Phase == MatchPhase.Serving)
            {
                _serveTicksRemaining--;
                if (_serveTicksRemaining <= 0)
                {
                    Serve();
                }

                return;
            }

            Ball.Step(deltaTime, Left, Right, _events);
            if (Ball.GoalReached() is Side conceded)
            {
                ScorePoint(conceded.Opponent());
            }
        }

        /// <summary>決着後にのみ、スコアを戻して再戦する。それ以外の局面では何もせず false。</summary>
        public bool Rematch()
        {
            if (Phase != MatchPhase.Finished)
            {
                return false;
            }

            _events.Clear();
            StartMatch();
            return true;
        }

        private void StartMatch()
        {
            LeftScore = 0;
            RightScore = 0;
            Winner = null;
            Left.SetY(0f);
            Right.SetY(0f);
            PrepareServe(_serveRandom.NextBool() ? Side.Left : Side.Right);
        }

        private void PrepareServe(Side receiver)
        {
            _serveTo = receiver;
            _serveTicksRemaining = Config.ServeDelayTicks;
            Ball.ResetToCenter();
            Phase = MatchPhase.Serving;
        }

        private void Serve()
        {
            var maxAngle = Config.MaxServeAngleDegrees * DegreesToRadians;
            var angle = _serveRandom.Range(-maxAngle, maxAngle);
            var velocity = new Vec2(_serveTo.Direction() * MathF.Cos(angle), MathF.Sin(angle)) * Config.ServeSpeed;
            Ball.Launch(Vec2.Zero, velocity);
            Phase = MatchPhase.Playing;
        }

        // 得点したらボールを中央に戻し、失点した側へ向けてサーブし直す。
        private void ScorePoint(Side scorer)
        {
            if (scorer == Side.Left)
            {
                LeftScore++;
            }
            else
            {
                RightScore++;
            }

            _events.Add(new MatchEvent(MatchEventKind.Scored, scorer));
            if (ScoreOf(scorer) < Config.WinScore)
            {
                PrepareServe(scorer.Opponent());
                return;
            }

            Winner = scorer;
            Phase = MatchPhase.Finished;
            Ball.ResetToCenter();
            _events.Add(new MatchEvent(MatchEventKind.MatchWon, scorer));
        }
    }
}
