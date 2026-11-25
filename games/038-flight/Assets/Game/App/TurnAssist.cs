using System;
using Game.Core;
using IntGeometry;

namespace Game.App
{
    /// <summary>
    /// キーボードの旋回の補助。左右のキーを押している間は失速しない範囲で強く引き、傾きで高度を保つ
    /// （上っていれば深く、下っていれば浅く傾ける。<see cref="Bank"/> を中心に <see cref="MinBank"/>〜<see cref="MaxBank"/>）。
    /// 押すだけで鋭く水平に曲がる。左右も上下も押していなければ翼を水平へ戻す。
    /// 上下のキーは補助より優先し、上下だけ押しているときはロールしない（宙返りできる）。地上では補助しない（キーのまま車輪を切る）。
    /// </summary>
    public static class TurnAssist
    {
        /// <summary>水平飛行のときに傾ける角度。</summary>
        public static readonly int Bank = Units.Degrees(80);

        public static readonly int MinBank = Units.Degrees(55);

        public static readonly int MaxBank = Units.Degrees(110);

        public static Controls Apply(FlightConfig flight, AircraftView plane, Controls keys)
        {
            if (flight == null) { throw new ArgumentNullException(nameof(flight)); }
            if (plane.Grounded) { return keys; }

            var bank = BankOf(plane.Attitude);
            var roll = keys.Roll;
            var pitch = keys.Pitch;
            var turning = keys.Roll != 0 && keys.Pitch == 0;
            if (keys.Roll != 0 || keys.Pitch == 0)
            {
                var target = 0;
                if (keys.Roll != 0)
                {
                    // 上っている角度 1° につき 4° 深く傾ける。
                    var depth = (int)Fixed.Clamp(Bank + (4L * ClimbOf(plane)), MinBank, MaxBank);
                    target = keys.Roll > 0 ? depth : -depth;
                }

                roll = Scale(Fixed.Wrap(target - bank), Units.Degrees(15));
            }

            if (turning && Math.Abs(bank) > Units.Degrees(30) && Math.Abs(bank) < Units.Degrees(135))
            {
                // 失速が近づくほど弱く引く（迎角が失速角の 3/4 で 0）。
                var limit = flight.StallAngle * 3 / 4;
                var margin = limit - plane.Air.Alpha;
                pitch = (int)Fixed.Clamp(Fixed.RoundDiv((long)margin * Controls.Full, Math.Max(1, limit / 3)), 0, Controls.Full);
            }

            return new Controls(pitch, roll, keys.Yaw, keys.Throttle);
        }

        /// <summary>速度の上向きの角度（下りは負）。</summary>
        public static int ClimbOf(AircraftView plane)
        {
            var v = plane.Velocity;
            var x = v.X / Units.PerMillimeter;
            var z = v.Z / Units.PerMillimeter;
            return Fixed.Wrap(Trig.Atan2(v.Y / Units.PerMillimeter, IntMath.Sqrt((x * x) + (z * z)), 0));
        }

        /// <summary>傾き（右へ傾くと正、背面で ±180°）。</summary>
        public static int BankOf(Quat attitude) => Fixed.Wrap(Trig.Atan2(-attitude.Right.Y, attitude.Up.Y, 0));

        private static int Scale(int angle, int full) =>
            (int)Fixed.Clamp(Fixed.RoundDiv((long)angle * Controls.Full, full), -Controls.Full, Controls.Full);
    }
}
