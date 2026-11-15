namespace Game.Core
{
    /// <summary>ボールの位置と速度。発射後に動かすのは <see cref="CollisionResolver"/> だけ。</summary>
    public sealed class Ball
    {
        public Ball(BreakoutConfig config)
        {
            HalfSize = config.BallSize / 2f;
        }

        public float HalfSize { get; }

        /// <summary>中心の位置。</summary>
        public Vec2 Position { get; internal set; }

        public Vec2 Velocity { get; internal set; }

        /// <summary>発射されて動いている。false ならパドルの上で待っている（または面の切り替え中）。</summary>
        public bool InPlay { get; private set; }

        /// <summary>止めて <paramref name="position"/> に置く。</summary>
        public void Hold(Vec2 position)
        {
            Position = position;
            Velocity = Vec2.Zero;
            InPlay = false;
        }

        /// <summary><paramref name="position"/> から <paramref name="velocity"/> で動かし始める。</summary>
        public void Launch(Vec2 position, Vec2 velocity)
        {
            Position = position;
            Velocity = velocity;
            InPlay = true;
        }
    }
}
