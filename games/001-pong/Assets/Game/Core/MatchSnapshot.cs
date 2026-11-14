namespace Game.Core
{
    /// <summary>描画に必要な位置だけを切り出した状態。Tick 間の補間に使う。</summary>
    public readonly struct MatchSnapshot
    {
        public MatchSnapshot(Vec2 ball, bool ballInPlay, float leftY, float rightY)
        {
            Ball = ball;
            BallInPlay = ballInPlay;
            LeftY = leftY;
            RightY = rightY;
        }

        public Vec2 Ball { get; }

        public bool BallInPlay { get; }

        public float LeftY { get; }

        public float RightY { get; }

        /// <summary>from → to を t で補間する。ボールが中央へ戻った直後などは補間せず to を使う。</summary>
        public static MatchSnapshot Interpolate(MatchSnapshot from, MatchSnapshot to, float t)
        {
            var ball = from.BallInPlay && to.BallInPlay ? Vec2.Lerp(from.Ball, to.Ball, t) : to.Ball;
            return new MatchSnapshot(
                ball,
                to.BallInPlay,
                from.LeftY + ((to.LeftY - from.LeftY) * t),
                from.RightY + ((to.RightY - from.RightY) * t));
        }
    }
}
