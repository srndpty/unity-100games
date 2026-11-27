using System;
using IntGeometry;

namespace Game.Core
{
    [Flags]
    public enum BallHit
    {
        None = 0,

        /// <summary>地面で弾んだ。</summary>
        Bounce = 1,

        /// <summary>ポストに当たった。</summary>
        Post = 2,

        /// <summary>弾みが小さくなり、転がりに変わった。</summary>
        Landed = 4,
    }

    /// <summary>
    /// 球。位置（x, y）・高さ z（球の下端）・速度（1 Tick あたり）。全て整数で、Mono と .NET で同じ軌跡になる。
    /// <para>
    /// 1 Tick：高さ 0 で縦の速さ 0 なら転がる（横の速さを ‰ で減らし、遅くなったら止まる）。それ以外は空中（縦の速さから重力を引く）。
    /// 位置に速さを足し、高さが 0 以下になったら弾む（縦は反転して反発の割合、横は摩擦の割合。弾みが小さければ転がりへ）。
    /// 動いた線分がクロスバーより下でポストに触れたら、触れた位置で止めて速さを法線に対して反射する（Tick の残りは捨てる）。
    /// </para>
    /// 予測（<see cref="BallPredictor"/>）も同じ <see cref="Step"/> を使うので、予測と本番の軌跡は Tick 単位で一致する（誰も触らなければ）。
    /// </summary>
    public sealed class Ball
    {
        public Vec2i Position { get; private set; }

        public int Height { get; private set; }

        public Vec2i Velocity { get; private set; }

        public int VerticalSpeed { get; private set; }

        public bool IsRolling => Height == 0 && VerticalSpeed == 0;

        public bool IsStopped => IsRolling && Velocity.IsZero;

        public Ball Clone() => (Ball)MemberwiseClone();

        public void Place(Vec2i position, int height = 0)
        {
            Position = position;
            Height = Math.Max(0, height);
            Velocity = Vec2i.Zero;
            VerticalSpeed = 0;
        }

        /// <summary>蹴る（今の位置から、この速度で）。</summary>
        public void Launch(Vec2i velocity, int verticalSpeed)
        {
            Velocity = velocity;
            VerticalSpeed = Math.Max(0, verticalSpeed);
        }

        /// <summary>選手が足元で運ぶ（位置と速度を選手に合わせ、地面に置く）。</summary>
        public void Carry(Vec2i position, Vec2i velocity)
        {
            Position = position;
            Velocity = velocity;
            Height = 0;
            VerticalSpeed = 0;
        }

        public BallHit Step(SoccerConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var hit = BallHit.None;
            var v = Velocity;
            var vz = VerticalSpeed;
            if (IsRolling)
            {
                v = new Vec2i(Decay(v.X, config.RollFrictionPerMille), Decay(v.Y, config.RollFrictionPerMille));
                if (Math.Abs(v.X) + Math.Abs(v.Y) <= config.StopSpeed) { v = Vec2i.Zero; }
            }
            else
            {
                vz -= config.Gravity;
            }

            var p0 = Position;
            var p1 = p0 + v;
            var z1 = Height + vz;

            // ポスト：クロスバーより下の球だけ。最初に触れたポストで止めて反射する。
            if (Math.Min(Height, Math.Max(0, z1)) < Pitch.CrossbarHeight && !v.IsZero)
            {
                var first = Touch.None;
                var post = -1;
                for (int i = 0; i < Pitch.Posts.Count; i++)
                {
                    var t = Sweep.First(p0, p1, Pitch.Posts[i], Pitch.Posts[i], config.BallRadius + Pitch.PostRadius);
                    if (t.IsEarlierThan(first))
                    {
                        first = t;
                        post = i;
                    }
                }

                if (post >= 0)
                {
                    var contact = new Vec2i(
                        p0.X + (int)((long)v.X * first.Numerator / first.Denominator),
                        p0.Y + (int)((long)v.Y * first.Numerator / first.Denominator));
                    Position = contact;
                    Velocity = Reflect(v, contact - Pitch.Posts[post], config.PostRestitutionPercent);
                    Height = Math.Max(0, Height + (int)((long)vz * first.Numerator / first.Denominator));
                    VerticalSpeed = Height == 0 ? 0 : vz;
                    return BallHit.Post;
                }
            }

            if (z1 <= 0 && vz < 0)
            {
                hit |= BallHit.Bounce;
                vz = -vz * config.BounceRestitutionPercent / 100;
                v = new Vec2i(v.X * config.BounceFrictionPercent / 100, v.Y * config.BounceFrictionPercent / 100);
                if (vz < config.MinBounceSpeed)
                {
                    vz = 0;
                    hit |= BallHit.Landed;
                }

                z1 = 0;
            }

            Position = p1;
            Height = Math.Max(0, z1);
            Velocity = v;
            VerticalSpeed = vz;
            return hit;
        }

        /// <summary>‰ の摩擦で 0 に向けて減らす（0 に向けて切り捨て）。</summary>
        private static int Decay(int value, int perMille) => (int)((long)value * (1000 - perMille) / 1000);

        /// <summary>速度を法線について反射し、割合を掛ける（法線は長さを問わない）。</summary>
        private static Vec2i Reflect(Vec2i v, Vec2i normal, int percent)
        {
            var nn = normal.LengthSquared;
            if (nn == 0) { return new Vec2i(-v.X * percent / 100, -v.Y * percent / 100); }
            var dot = Vec2i.Dot(v, normal);
            if (dot >= 0) { return new Vec2i(v.X * percent / 100, v.Y * percent / 100); }
            var rx = v.X - (2 * dot * normal.X / nn);
            var ry = v.Y - (2 * dot * normal.Y / nn);
            return new Vec2i((int)(rx * percent / 100), (int)(ry * percent / 100));
        }
    }
}
