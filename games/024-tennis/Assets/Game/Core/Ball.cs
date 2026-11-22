using System;

namespace Game.Core
{
    public enum BallEventKind
    {
        None,

        /// <summary>地面で弾んだ。<see cref="BallEvent.Point"/> は地面に触れた位置。</summary>
        Bounce,

        /// <summary>ネットに触れた。球はその位置で止まる（死に球）。</summary>
        Net,
    }

    /// <summary>1 Tick の間に球に起きたこと（多くても 1 つ）。</summary>
    public readonly struct BallEvent
    {
        public BallEvent(BallEventKind kind, Vec3 point)
        {
            Kind = kind;
            Point = point;
        }

        public BallEventKind Kind { get; }

        public Vec3 Point { get; }

        public static BallEvent None => default;
    }

    /// <summary>
    /// 球。位置は球の下端（地面に触れる点）、速さは 1 Tick あたりの単位。空気抵抗と回転は扱わない。
    /// <para>
    /// 1 Tick：重力で縦の速さを減らし、位置に速さを足す。その線分の中でネットの面（y = 0）と地面（z = 0）のどちらに先に触れるかを割合で求める。
    /// ネットの面を通る高さがネットより低ければネット（その位置で止まる）。地面に触れたらその位置で弾み（縦は反転して反発の割合、横は摩擦の割合）、
    /// 残りの割合だけ新しい速さで進む。弾んだ後の同じ Tick の中では、ネットも 2 回目の地面も見ない（弾んだ球はネットから離れる向きに進む）。
    /// </para>
    /// 予測（<see cref="BallPredictor"/>）も同じ <see cref="Step"/> を使うので、予測と実際の軌跡は一致する。
    /// </summary>
    public sealed class Ball
    {
        public Ball(Vec3 position, Vec3 velocity)
        {
            Position = position;
            Velocity = velocity;
        }

        public Vec3 Position { get; private set; }

        public Vec3 Velocity { get; private set; }

        /// <summary>ネットに触れて止まった。<see cref="Step"/> は何もしない。</summary>
        public bool IsDead { get; private set; }

        public Ball Clone() => (Ball)MemberwiseClone();

        public void Place(Vec3 position, Vec3 velocity)
        {
            Position = position;
            Velocity = velocity;
            IsDead = false;
        }

        public BallEvent Step(BallPhysics physics)
        {
            if (IsDead) { return BallEvent.None; }

            var v = new Vec3(Velocity.X, Velocity.Y, Velocity.Z - physics.Gravity);
            var p0 = Position;
            var p1 = p0 + v;

            // ネットの面を横切る割合（分子 / 分母）。横切らなければ分母 0。
            long netNum = 0, netDen = 0;
            if ((p0.Y < 0 && p1.Y >= 0) || (p0.Y > 0 && p1.Y <= 0))
            {
                netNum = Math.Abs((long)p0.Y);
                netDen = Math.Abs((long)p1.Y - p0.Y);
            }

            long groundNum = 0, groundDen = 0;
            if (p1.Z < 0)
            {
                groundNum = p0.Z;
                groundDen = (long)p0.Z - p1.Z;
            }

            var netFirst = netDen != 0 && (groundDen == 0 || netNum * groundDen <= groundNum * netDen);
            if (netFirst)
            {
                var crossing = Lerp(p0, p1, netNum, netDen);
                if (crossing.Z < Court.NetHeight)
                {
                    Position = crossing.WithZ(Math.Max(0, crossing.Z));
                    Velocity = Vec3.Zero;
                    IsDead = true;
                    return new BallEvent(BallEventKind.Net, Position);
                }
            }

            if (groundDen != 0)
            {
                var contact = Lerp(p0, p1, groundNum, groundDen).WithZ(0);
                var bounced = new Vec3(
                    v.X * physics.FrictionPercent / 100,
                    v.Y * physics.FrictionPercent / 100,
                    -v.Z * physics.RestitutionPercent / 100);
                var rest = groundDen - groundNum;
                Position = new Vec3(
                    contact.X + (int)(bounced.X * rest / groundDen),
                    contact.Y + (int)(bounced.Y * rest / groundDen),
                    (int)(bounced.Z * rest / groundDen));
                Velocity = bounced;
                return new BallEvent(BallEventKind.Bounce, contact);
            }

            Position = p1;
            Velocity = v;
            return BallEvent.None;
        }

        /// <summary>p0 から p1 へ num / den だけ進んだ位置（0 に向けて切り捨て）。</summary>
        private static Vec3 Lerp(Vec3 p0, Vec3 p1, long num, long den) => new(
            p0.X + (int)(((long)p1.X - p0.X) * num / den),
            p0.Y + (int)(((long)p1.Y - p0.Y) * num / den),
            p0.Z + (int)(((long)p1.Z - p0.Z) * num / den));
    }

    /// <summary>球の物理の値（設定から取る）。</summary>
    public readonly struct BallPhysics
    {
        public BallPhysics(int gravity, int restitutionPercent, int frictionPercent)
        {
            Gravity = gravity;
            RestitutionPercent = restitutionPercent;
            FrictionPercent = frictionPercent;
        }

        /// <summary>1 Tick あたりの縦の速さの減り。</summary>
        public int Gravity { get; }

        /// <summary>弾んだときに残る縦の速さ（%）。</summary>
        public int RestitutionPercent { get; }

        /// <summary>弾んだときに残る横の速さ（%）。</summary>
        public int FrictionPercent { get; }
    }
}
