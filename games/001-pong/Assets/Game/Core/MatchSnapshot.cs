namespace Game.Core
{
    /// <summary>描画に必要な状態だけを切り出したもの。Tick 間の補間に使う。</summary>
    public readonly struct MatchSnapshot
    {
        public MatchSnapshot(Vec2 ball, bool ballInPlay, PaddleMotion left, PaddleMotion right)
        {
            Ball = ball;
            BallInPlay = ballInPlay;
            Left = left;
            Right = right;
        }

        public Vec2 Ball { get; }

        public bool BallInPlay { get; }

        /// <summary>この Tick の左パドルの軌跡。</summary>
        public PaddleMotion Left { get; }

        /// <summary>この Tick の右パドルの軌跡。</summary>
        public PaddleMotion Right { get; }

        public float LeftY => Left.To;

        public float RightY => Right.To;

        /// <summary>
        /// from → to の Tick 中の時刻 t での状態。ボールが中央へ戻った直後などは補間せず to を使う。
        /// パドルは衝突判定と同じ軌跡（端に着いたらそこで止まる）をたどる。
        /// </summary>
        public static MatchSnapshot Interpolate(MatchSnapshot from, MatchSnapshot to, float t)
        {
            var ball = from.BallInPlay && to.BallInPlay ? Vec2.Lerp(from.Ball, to.Ball, t) : to.Ball;
            return new MatchSnapshot(
                ball,
                to.BallInPlay,
                PaddleMotion.At(to.Left.YAt(t)),
                PaddleMotion.At(to.Right.YAt(t)));
        }
    }
}
