namespace Game.Core
{
    /// <summary>描画に必要な動く物の状態だけを切り出したもの。Tick 間の補間に使う（ブロックとカプセルは最新の Tick を描く）。</summary>
    public readonly struct BreakoutSnapshot
    {
        public BreakoutSnapshot(Vec2 ball, bool ballInPlay, PaddleMotion paddle, float paddleHalfWidth)
        {
            Ball = ball;
            BallInPlay = ballInPlay;
            Paddle = paddle;
            PaddleHalfWidth = paddleHalfWidth;
        }

        public Vec2 Ball { get; }

        /// <summary>ボールが動いている（false ならパドルの上で待っている）。</summary>
        public bool BallInPlay { get; }

        /// <summary>この Tick のパドルの軌跡。</summary>
        public PaddleMotion Paddle { get; }

        public float PaddleX => Paddle.To;

        public float PaddleHalfWidth { get; }

        /// <summary>
        /// from → to の Tick 中の時刻 t での状態。ボールが動き続けている間だけ補間し、発射待ち（パドルに乗っている）ならパドルと同じだけずらす。
        /// パドルは衝突判定と同じ軌跡（端に着いたらそこで止まる）をたどる。
        /// </summary>
        public static BreakoutSnapshot Interpolate(BreakoutSnapshot from, BreakoutSnapshot to, float t)
        {
            var paddleX = to.Paddle.XAt(t);
            var ball = from.BallInPlay && to.BallInPlay ? Vec2.Lerp(from.Ball, to.Ball, t)
                : to.BallInPlay ? to.Ball
                : new Vec2(to.Ball.X + (paddleX - to.PaddleX), to.Ball.Y);
            return new BreakoutSnapshot(ball, to.BallInPlay, PaddleMotion.At(paddleX), to.PaddleHalfWidth);
        }
    }
}
