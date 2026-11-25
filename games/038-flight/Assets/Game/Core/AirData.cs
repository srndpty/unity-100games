using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 速度と姿勢から決まる空力の値。<b>機首と速度方向のずれ</b>を迎角（上下）と横滑り角（左右）で表す。
    /// 飛行モデル・ロックオン・HUD・テストが同じ値を使う。
    /// </summary>
    public readonly struct AirData : IEquatable<AirData>
    {
        public AirData(long speed, int alpha, int beta, bool stalled)
        {
            Speed = speed;
            Alpha = alpha;
            Beta = beta;
            Stalled = stalled;
        }

        /// <summary>速さ（µm/Tick）。</summary>
        public long Speed { get; }

        /// <summary>迎角（符号つきの角度）。正 = 機首が速度の向きより上。</summary>
        public int Alpha { get; }

        /// <summary>横滑り角（符号つきの角度）。正 = 速度が機首より右へ流れている（機首は速度の左）。</summary>
        public int Beta { get; }

        /// <summary>失速中か（空中で、迎角が失速角を超える、または速さが失速速度未満）。地上では常に false。</summary>
        public bool Stalled { get; }

        public static bool operator ==(AirData left, AirData right) => left.Equals(right);

        public static bool operator !=(AirData left, AirData right) => !left.Equals(right);

        /// <summary>速度 <paramref name="velocity"/> と姿勢 <paramref name="attitude"/> の空力の値。速さ 0 では迎角・横滑り角とも 0。</summary>
        public static AirData Of(FlightConfig config, Vec3l velocity, Quat attitude, bool grounded)
        {
            var speed = velocity.Length;
            if (speed == 0) { return new AirData(0, 0, 0, !grounded); }

            var forward = Vec3l.Project(velocity, attitude.Forward);
            var up = Vec3l.Project(velocity, attitude.Up);
            var right = Vec3l.Project(velocity, attitude.Right);
            var alpha = Fixed.Wrap(Trig.Atan2(-up, forward, 0));
            var beta = Fixed.Wrap(Trig.Atan2(right, forward, 0));
            var stalled = !grounded && (Math.Abs(alpha) > config.StallAngle || speed < config.StallSpeed);
            return new AirData(speed, alpha, beta, stalled);
        }

        public bool Equals(AirData other) => Speed == other.Speed && Alpha == other.Alpha && Beta == other.Beta && Stalled == other.Stalled;

        public override bool Equals(object? obj) => obj is AirData other && Equals(other);

        public override int GetHashCode() => unchecked((Speed.GetHashCode() * 397) ^ (Alpha * 131) ^ (Beta * 31) ^ (Stalled ? 1 : 0));

        public override string ToString() => $"(v {Speed}, α {Alpha}, β {Beta}{(Stalled ? ", STALL" : "")})";
    }
}
