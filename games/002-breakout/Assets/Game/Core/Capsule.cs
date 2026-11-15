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

        /// <summary>
        /// 1 Tick 分落とし、その Tick の間にパドルと重なったら true。パドルはこの Tick の移動（<see cref="Paddle.Motion"/>）を済ませておくこと。
        /// <para>
        /// カプセルは Tick 内で等速に落ち、パドルは衝突判定と同じ軌跡（途中で壁に着いたらそこで止まる）をたどるとして、
        /// Tick 中のどこかの時刻で重なるかを調べる。Tick 終了時の位置だけで比べると、動くパドルが途中で触れたカプセルを取り逃す。
        /// </para>
        /// </summary>
        public bool FallAndCatch(Paddle paddle, BreakoutConfig config)
        {
            var start = Position;
            var fall = config.CapsuleSpeed * config.TickDuration;
            Position = new Vec2(start.X, start.Y - fall);

            // 縦に重なっている時刻の区間 [from, to]（落下は等速なので 1 つの区間になる）。
            var reachY = (config.PaddleHeight + config.CapsuleHeight) / 2f;
            var from = Math.Max(0f, (start.Y - config.PaddleY - reachY) / fall);
            var to = Math.Min(1f, (start.Y - config.PaddleY + reachY) / fall);
            if (from > to)
            {
                return false;
            }

            // パドルの x は「止まるまで等速、止まった後は一定」なので、その境目で分け、各部分では両端の位置から最小距離が決まる。
            var reachX = paddle.HalfWidth + (config.CapsuleWidth / 2f);
            var motion = paddle.Motion;
            return CloseEnough(start.X, motion, from, Math.Min(to, motion.StopAt), reachX)
                || CloseEnough(start.X, motion, Math.Max(from, motion.StopAt), to, reachX);
        }

        /// <summary>下端を完全に越えたか。</summary>
        internal bool IsBelow(BreakoutConfig config) => Position.Y + (config.CapsuleHeight / 2f) < -config.FieldHeight / 2f;

        // 時刻 [from, to] の間で、パドルの x が等速（または一定）なら、x との距離の最小値が reach 以内か。
        private static bool CloseEnough(float x, PaddleMotion motion, float from, float to, float reach)
        {
            if (from > to)
            {
                return false;
            }

            var a = motion.XAt(from) - x;
            var b = motion.XAt(to) - x;
            var nearest = (a <= 0f && b >= 0f) || (a >= 0f && b <= 0f) ? 0f : MathF.Min(MathF.Abs(a), MathF.Abs(b));
            return nearest <= reach;
        }
    }
}
