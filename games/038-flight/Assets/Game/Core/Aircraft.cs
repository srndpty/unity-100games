using TileMotion;

namespace Game.Core
{
    /// <summary>1 機の飛行状態。<see cref="FlightModel.Step"/> が 1 Tick ずつ進める。</summary>
    public sealed class Aircraft
    {
        /// <summary>位置（µm）。</summary>
        public Vec3l Position { get; internal set; }

        /// <summary>速度（µm/Tick）。機首の向きとは別に持つ。</summary>
        public Vec3l Velocity { get; internal set; }

        /// <summary>姿勢。</summary>
        public Quat Attitude { get; internal set; } = Quat.Identity;

        /// <summary>スロットル（‰、0〜1000）。</summary>
        public int Throttle { get; internal set; }

        /// <summary>車輪で地上にいるか。</summary>
        public bool Grounded { get; internal set; }

        /// <summary>地上の機首の方位（北 = 0、右回りが正）。地上でだけ意味を持つ。</summary>
        public int GroundHeading { get; internal set; }

        /// <summary>地上の機首上げの角度。地上でだけ意味を持つ。</summary>
        public int GroundPitch { get; internal set; }

        /// <summary>墜落したか。墜落した機体は動かない。</summary>
        public bool Crashed { get; internal set; }

        /// <summary>直近の Step の終わりの空力の値。</summary>
        public AirData Air { get; internal set; }

        /// <summary>滑走路の南端の中心で、北を向いて止まった機体。</summary>
        public static Aircraft OnRunway(FlightConfig config, Runway runway) => new()
        {
            Position = new Vec3l(runway.X, config.GearHeight, runway.StartZ),
            Attitude = Quat.Identity,
            Grounded = true,
        };

        /// <summary>空中で、方位 <paramref name="heading"/> へ水平に速さ <paramref name="speed"/> で飛ぶ機体。</summary>
        public static Aircraft InFlight(FlightConfig config, Vec3l position, int heading, long speed, int throttle)
        {
            var attitude = Quat.FromHeadingPitch(heading, 0);
            var velocity = Vec3l.ScaleUnit(attitude.Forward, speed);
            var aircraft = new Aircraft
            {
                Position = position,
                Velocity = velocity,
                Attitude = attitude,
                Throttle = throttle,
            };
            aircraft.Air = AirData.Of(config, velocity, attitude, false);
            return aircraft;
        }

        public Aircraft Clone() => (Aircraft)MemberwiseClone();

        /// <summary>以後の動きを決める状態を全て混ぜる。</summary>
        public void Mix(ref StateHash hash)
        {
            Mix(ref hash, Position);
            Mix(ref hash, Velocity);
            hash.Mix(Attitude.W);
            hash.Mix(Attitude.X);
            hash.Mix(Attitude.Y);
            hash.Mix(Attitude.Z);
            hash.Mix(Throttle);
            hash.Mix(Grounded);
            hash.Mix(GroundHeading);
            hash.Mix(GroundPitch);
            hash.Mix(Crashed);
        }

        private static void Mix(ref StateHash hash, Vec3l v)
        {
            hash.Mix(v.X);
            hash.Mix(v.Y);
            hash.Mix(v.Z);
        }
    }
}
