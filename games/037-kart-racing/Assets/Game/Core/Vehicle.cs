using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>1 台の加速・旋回・ドリフト。壁・ゲート・他の車は知らない。路面（ターマックか芝か）は引数で受け取る。</summary>
    public static class Vehicle
    {
        /// <summary>
        /// 操作を 1 Tick 反映し、壁に当たる前の移動量を返す。
        /// ドリフトのボタンだけを離し、加速と旋回と速さが条件を満たしたままなら、
        /// <paramref name="releasedTicks"/> に押していた Tick 数を入れる。ブーストの残りはここでは増やさない。
        /// 加速を離した、旋回をやめた、壁以外で条件を外したときは溜めを消すだけで、releasedTicks は 0。
        /// <paramref name="offroad"/> なら、加速・ブレーキの後で速さを <see cref="KartConfig.OffroadSpeed"/> へ
        /// <see cref="KartConfig.OffroadDrag"/> ずつ下げる（ブースト中も）。
        /// </summary>
        public static Vec2i Integrate(KartConfig config, ref Kart kart, Drive drive, bool offroad, out int releasedTicks)
        {
            releasedTicks = 0;
            var steering = drive.Steer == -1 || drive.Steer == 1;
            if (kart.DriftTicks > 0)
            {
                var holding = drive.Drift && drive.Accelerate && !drive.Brake && drive.Steer == kart.DriftSteer;
                var cleanRelease = !drive.Drift && drive.Accelerate && !drive.Brake && drive.Steer == kart.DriftSteer;
                if (cleanRelease) { releasedTicks = kart.DriftTicks; }
                if (cleanRelease || !holding) { kart.DriftTicks = 0; }
                else { kart.DriftTicks++; }
            }
            else if (drive.Drift && drive.Accelerate && !drive.Brake && steering && kart.Speed >= config.DriftMinSpeed)
            {
                kart.DriftSteer = drive.Steer;
                kart.DriftTicks = 1;
            }

            var cap = kart.BoostLeft > 0 ? config.BoostSpeed : config.MaxSpeed;
            if (drive.Brake)
            {
                kart.Speed = Math.Max(kart.Speed - config.Brake, -config.ReverseSpeed);
            }
            else if (drive.Accelerate)
            {
                if (kart.Speed < cap) { kart.Speed = Math.Min(kart.Speed + config.Accel, cap); }
                else if (kart.Speed > cap) { kart.Speed = Math.Max(kart.Speed - config.Drag, cap); }
            }
            else if (kart.Speed > 0)
            {
                kart.Speed = Math.Max(0, kart.Speed - config.Drag);
            }
            else if (kart.Speed < 0)
            {
                kart.Speed = Math.Min(0, kart.Speed + config.Drag);
            }

            if (kart.DriftTicks > 0) { kart.Speed = Math.Max(0, kart.Speed - config.DriftDrag); }
            if (offroad && kart.Speed > config.OffroadSpeed)
            {
                kart.Speed = Math.Max(config.OffroadSpeed, kart.Speed - config.OffroadDrag);
            }

            if (kart.Speed != 0 && drive.Steer != 0)
            {
                var steer = kart.Speed < 0 ? -drive.Steer : drive.Steer;
                var turn = config.Turn(kart.Speed);
                if (kart.DriftTicks > 0) { turn += config.DriftTurn; }
                kart.Heading = Trig.Normalize(kart.Heading + (steer * turn));
            }

            if (kart.BoostLeft > 0) { kart.BoostLeft--; }

            if (kart.Speed == 0) { return Vec2i.Zero; }
            var moveAngle = kart.Heading;
            if (kart.DriftTicks > 0)
            {
                moveAngle = Trig.Normalize(kart.Heading - (kart.DriftSteer * config.SlipAngle));
            }

            return new Vec2i(
                (int)(((long)kart.Speed * Trig.Cos(moveAngle)) / Trig.One),
                (int)(((long)kart.Speed * Trig.Sin(moveAngle)) / Trig.One));
        }
    }
}
