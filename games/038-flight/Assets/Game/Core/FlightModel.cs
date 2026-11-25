using System;

namespace Game.Core
{
    /// <summary>1 Tick の飛行で起きたこと。</summary>
    public enum FlightEvent
    {
        None,
        TookOff,
        Crashed,
    }

    /// <summary>
    /// 飛行モデル（揚力の近似）。1 Tick の順序：
    /// スロットル → 舵と失速・風見による角速度 → 姿勢 → 新しい姿勢での迎角・横滑り角 → 推力・揚力・横力・抗力・重力 → 速度 → 位置 → 地面。
    /// 速度は機首の向きと別に持ち、揚力（機体の上方向）と横力（機体の左右）が速度を機首の向きへ少しずつ曲げる。
    /// </summary>
    public static class FlightModel
    {
        /// <summary>1 Tick 進める。墜落した機体は変えない。</summary>
        public static FlightEvent Step(FlightConfig config, Aircraft aircraft, Controls controls, Runway runway)
        {
            controls.Validate();
            if (aircraft.Crashed) { return FlightEvent.None; }

            aircraft.Throttle = Fixed.Clamp(aircraft.Throttle + (int)Fixed.RoundDiv((long)controls.Throttle * config.ThrottleRate, Controls.Full), 0, 1000);
            var result = aircraft.Grounded ? StepGround(config, aircraft, controls, runway) : StepAir(config, aircraft, controls);
            aircraft.Air = AirData.Of(config, aircraft.Velocity, aircraft.Attitude, aircraft.Grounded);
            return result;
        }

        private static FlightEvent StepAir(FlightConfig config, Aircraft aircraft, Controls controls)
        {
            var before = AirData.Of(config, aircraft.Velocity, aircraft.Attitude, false);
            var effect = config.ControlEffect(before.Speed);

            // 機体軸の角速度。Unity の向きでは +x 回転が機首下げ、+z 回転が左へ傾ける、+y 回転が機首を右へ。
            var pitch = -Rate(controls.Pitch, config.PitchRate, effect);
            var roll = -Rate(controls.Roll, config.RollRate, effect);
            var yaw = Rate(controls.Yaw, config.YawRate, effect);
            if (before.Stalled)
            {
                // 失速中は機首が速度の向きへ落ちる（迎角が正なら機首下げ）。
                pitch += Math.Sign(before.Alpha) * config.StallPitchDrop;
            }

            yaw += (int)Fixed.RoundDiv((long)before.Beta * config.Weathervane, 1000);
            aircraft.Attitude = aircraft.Attitude.RotateBody(pitch, yaw, roll);

            // 新しい姿勢で力を求める。
            var velocity = aircraft.Velocity;
            var air = AirData.Of(config, velocity, aircraft.Attitude, false);
            var speed = air.Speed;
            var accel = Vec3l.ScaleUnit(aircraft.Attitude.Forward, config.ThrustAt(aircraft.Throttle))
                + Vec3l.ScaleUnit(aircraft.Attitude.Up, config.Lift(air.Alpha, speed))
                - Vec3l.ScaleUnit(aircraft.Attitude.Right, config.SideForce(air.Beta, speed))
                + new Vec3l(0, -config.Gravity, 0);
            if (speed > 0) { accel -= Vec3l.MulDiv(velocity, config.Drag(speed), speed); }

            velocity = LimitSpeed(velocity + accel, config.SpeedLimit);
            aircraft.Velocity = velocity;
            var position = aircraft.Position + velocity;

            // 地面は平面なので、Tick の終わりの高さだけで軌跡の接触が分かる。ちょうど触れて下りていれば接触。
            if (position.Y < config.GearHeight || (position.Y == config.GearHeight && velocity.Y <= 0))
            {
                aircraft.Position = new Vec3l(position.X, config.GearHeight, position.Z);
                aircraft.Crashed = true;
                return FlightEvent.Crashed;
            }

            aircraft.Position = position;
            return FlightEvent.None;
        }

        private static FlightEvent StepGround(FlightConfig config, Aircraft aircraft, Controls controls, Runway runway)
        {
            var speed = aircraft.Velocity.Length;

            // 地上ではロールとヨーの入力で車輪を切る。止まっていれば向きは変えない。
            if (speed > 0)
            {
                var steer = Fixed.Clamp(controls.Yaw + controls.Roll, -Controls.Full, Controls.Full);
                aircraft.GroundHeading = Fixed.Wrap(aircraft.GroundHeading + (int)Fixed.RoundDiv((long)steer * config.TaxiTurnRate, Controls.Full));
            }

            if (controls.Pitch > 0 && speed >= config.RotateSpeed)
            {
                var up = (int)Fixed.RoundDiv((long)controls.Pitch * config.GroundPitchRate, Controls.Full);
                aircraft.GroundPitch = Math.Min(aircraft.GroundPitch + up, config.MaxGroundPitch);
            }
            else
            {
                aircraft.GroundPitch = Math.Max(aircraft.GroundPitch - config.GroundPitchRate, 0);
            }

            speed = Math.Max(0, speed + config.GroundAccel(speed, aircraft.Throttle));
            var heading = aircraft.GroundHeading;
            aircraft.Attitude = Quat.FromHeadingPitch(heading, aircraft.GroundPitch);
            var velocity = new Vec3l(Fixed.Round(Fixed.Sin(heading) * speed), 0, Fixed.Round(Fixed.Cos(heading) * speed));

            // 速度は水平なので迎角 = 機首上げの角度。揚力が重力を超えたら浮く。
            var lift = config.Lift(aircraft.GroundPitch, speed);
            var result = FlightEvent.None;
            if (lift > config.Gravity)
            {
                velocity = new Vec3l(velocity.X, lift - config.Gravity, velocity.Z);
                aircraft.Grounded = false;
                result = FlightEvent.TookOff;
            }

            aircraft.Velocity = velocity;
            aircraft.Position += velocity;
            if (aircraft.Grounded && !runway.Contains(aircraft.Position))
            {
                aircraft.Crashed = true;
                return FlightEvent.Crashed;
            }

            return result;
        }

        private static int Rate(int input, int rate, long effectQ16) =>
            (int)Fixed.RoundDiv((long)input * rate * effectQ16, (long)Controls.Full << 16);

        private static Vec3l LimitSpeed(Vec3l velocity, long limit)
        {
            var speed = velocity.Length;
            return speed > limit ? Vec3l.MulDiv(velocity, limit, speed) : velocity;
        }
    }
}
