using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>Core の整数（µm、Q30）を Unity の値（m、Quaternion）へ直す。軸の向きは同じ（左手系、y 上、z 北）。</summary>
    public static class HalcyonSpace
    {
        public static Vector3 Position(Vec3l value) =>
            new((float)(value.X / (double)Units.PerMeter), (float)(value.Y / (double)Units.PerMeter), (float)(value.Z / (double)Units.PerMeter));

        /// <summary>速度（µm/Tick）を m/s へ。</summary>
        public static Vector3 Velocity(Vec3l value) => Position(value) * Units.TickRate;

        public static Quaternion Rotation(Quat value)
        {
            const double One = Fixed.One;
            return new Quaternion((float)(value.X / One), (float)(value.Y / One), (float)(value.Z / One), (float)(value.W / One));
        }

        /// <summary>µm/Tick を km/h へ。</summary>
        public static int KilometersPerHour(long speed) => (int)(speed * Units.TickRate * 36 / (Units.PerMeter * 10));
    }
}
