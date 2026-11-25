using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 点へ向かう操縦（敵の操縦とプレイヤーの操縦役が使う純粋関数）。
    /// 点が機首からずれていれば、点が機体の真上に来るようロールしてから引く。ほぼ正面なら翼を水平に近づけて小さく合わせる。
    /// 失速が近ければ引かない。速さは <c>cruise</c> を保つようにスロットルを使う。
    /// </summary>
    public static class Steering
    {
        /// <summary>このずれより小さければ「ほぼ正面」。</summary>
        public static readonly int FineAngle = Units.Degrees(4);

        public static Controls Toward(FlightConfig config, AircraftView plane, Vec3l point, long cruise)
        {
            var attitude = plane.Attitude;
            var d = Millimeters(point - plane.Position);
            var x = Vec3l.Project(d, attitude.Right);
            var y = Vec3l.Project(d, attitude.Up);
            var z = Vec3l.Project(d, attitude.Forward);
            var offAxis = Fixed.Wrap(Trig.Atan2(IntMath.Sqrt((x * x) + (y * y)), z, 0));

            int pitch;
            int roll;
            var yaw = 0;
            if (offAxis < FineAngle)
            {
                // ほぼ正面：上下はピッチ、左右はヨーで合わせ、翼は水平へ戻す。
                pitch = Scale(Fixed.Wrap(Trig.Atan2(y, z, 0)), Units.Degrees(2));
                yaw = Scale(Fixed.Wrap(Trig.Atan2(x, z, 0)), Units.Degrees(2));
                // 右翼が上がっている（左に傾いている）なら右へロールして戻す。
                roll = Scale(Fixed.Wrap(Trig.Atan2(attitude.Right.Y, Fixed.One, 0)), Units.Degrees(10));
            }
            else
            {
                // 点が機体の上（+y）に来るようロールする。誤差が 60° 以内なら引く。
                var bankError = Fixed.Wrap(Trig.Atan2(x, y, 0));
                roll = Scale(bankError, Units.Degrees(20));
                pitch = Math.Abs(bankError) < Units.Degrees(60) ? Scale(offAxis, Units.Degrees(8)) : 0;
            }

            if (plane.Air.Alpha > config.StallAngle * 3 / 4 || plane.Air.Speed < config.StallSpeed + (config.StallSpeed / 4))
            {
                pitch = Math.Min(pitch, 0);
            }

            var speed = plane.Air.Speed;
            var throttle = speed < cruise ? Controls.Full : speed > cruise + (10 * Units.MetersPerSecond) ? -Controls.Full : 0;
            return new Controls(pitch, roll, yaw, throttle);
        }

        /// <summary>地面へ向かっているときに、水平の少し上の点（引き起こし）。そうでなければ null。</summary>
        public static Vec3l? Recovery(AircraftView plane, long floor)
        {
            var position = plane.Position;
            var velocity = plane.Velocity;
            var predicted = position.Y + (velocity.Y * 90);
            if (position.Y >= floor && predicted >= floor / 2) { return null; }
            return new Vec3l(position.X + (velocity.X * 120), Math.Max(position.Y, floor) + (500 * Units.PerMeter), position.Z + (velocity.Z * 120));
        }

        /// <summary>脅威（ミサイル）の視線と直角に、水平に逃げる点。今の進む向きに近い側を選ぶ。</summary>
        public static Vec3l Evade(AircraftView plane, Vec3l threat)
        {
            var los = plane.Position - threat;
            var side = new Vec3l(los.Z, 0, -los.X);
            if (Vec3l.Dot(Millimeters(side), Millimeters(plane.Velocity)) < 0) { side = -side; }
            return plane.Position + side;
        }

        /// <summary>区域の外寄り（<paramref name="limit"/> の外）にいれば、区域の中心の同じ高さの点。そうでなければ null。</summary>
        public static Vec3l? StayInside(AircraftView plane, long limit)
        {
            var p = plane.Position;
            if (Math.Abs(p.X) <= limit && Math.Abs(p.Z) <= limit) { return null; }
            return new Vec3l(0, p.Y, 0);
        }

        private static int Scale(int angle, int full) =>
            (int)Fixed.Clamp(Fixed.RoundDiv((long)angle * Controls.Full, full), -Controls.Full, Controls.Full);

        private static Vec3l Millimeters(Vec3l value) =>
            new(value.X / Units.PerMillimeter, value.Y / Units.PerMillimeter, value.Z / Units.PerMillimeter);
    }
}
