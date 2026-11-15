using System;

namespace Game.Core
{
    /// <summary>アイテムブロックから落ちるカプセル。パドルに触れたら効果が発動する。</summary>
    public sealed class Capsule
    {
        public Capsule(EffectKind effect, Vec2 position)
        {
            Effect = effect;
            Position = position;
        }

        public EffectKind Effect { get; }

        /// <summary>中心の位置。</summary>
        public Vec2 Position { get; private set; }

        internal void Fall(float distance) => Position = new Vec2(Position.X, Position.Y - distance);

        /// <summary>パドルと重なっているか（Tick 終了時の位置で判定）。</summary>
        internal bool Touches(Paddle paddle, BreakoutConfig config)
        {
            var dx = MathF.Abs(Position.X - paddle.X);
            var dy = MathF.Abs(Position.Y - config.PaddleY);
            return dx <= paddle.HalfWidth + (config.CapsuleWidth / 2f) && dy <= (config.PaddleHeight + config.CapsuleHeight) / 2f;
        }

        /// <summary>下端を完全に越えたか。</summary>
        internal bool IsBelow(BreakoutConfig config) => Position.Y + (config.CapsuleHeight / 2f) < -config.FieldHeight / 2f;
    }
}
