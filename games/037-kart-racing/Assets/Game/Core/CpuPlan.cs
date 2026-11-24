using IntGeometry;

namespace Game.Core
{
    /// <summary>ウェイポイントと曲率の速度計画。その Tick の車の状態だけから操作を決める。</summary>
    public static class CpuPlan
    {
        private const int SteerWindow = 350;

        public static Drive Intent(Kart kart, Track track, KartConfig config, int pacePermille)
        {
            if (kart.Finished) { return Drive.None; }
            var place = new TrackPlace(kart.Station, kart.Along);
            var ahead = track.Travel(place, config.Lookahead);
            var steer = SteerToward(kart, track.PointAt(ahead));
            if (kart.RecoverLeft > 0)
            {
                // 後退中はハンドルの符号が反転するので、鼻先を目標へ向けるには逆に切る。
                var recoverSteer = steer == 0 ? 0 : -steer;
                return new Drive(accelerate: false, brake: true, recoverSteer, drift: false);
            }

            var severity = WorstTurn(track, place, config.Lookahead * 4);
            if (kart.DriftTicks >= config.MiniTurboTicks && kart.BoostLeft == 0)
            {
                return new Drive(kart.Speed < config.MaxSpeed, brake: false, kart.DriftSteer, drift: false);
            }

            var limit = severity >= config.DriftCorner
                ? config.CornerSpeed
                : config.MaxSpeed - ((config.MaxSpeed - config.CornerSpeed) * severity / config.DriftCorner);
            var target = config.CornerSpeed + ((limit - config.CornerSpeed) * pacePermille / 1000);
            var accelerate = kart.Speed < target;
            var brake = kart.Speed > target + config.Accel;
            var nearTarget = kart.Speed <= target + (config.Accel * 4);
            var drifting = severity >= config.DriftCorner && kart.BoostLeft == 0 && kart.Speed >= config.DriftMinSpeed && nearTarget;
            if (kart.DriftTicks > 0 && severity >= (config.DriftCorner / 2) && kart.BoostLeft == 0)
            {
                drifting = true;
                steer = kart.DriftSteer;
            }

            if (drifting)
            {
                brake = false;
                accelerate = kart.Speed < config.MaxSpeed;
            }
            if (brake) { accelerate = false; }
            if (drifting && steer == 0)
            {
                var corner = Geom.AngleDelta(track.Direction(place.Station), track.Direction(ahead.Station));
                steer = corner >= 0 ? 1 : -1;
            }

            var drift = drifting && steer != 0;
            return new Drive(accelerate, brake, steer, drift);
        }

        private static int WorstTurn(Track track, TrackPlace place, int horizon)
        {
            if (horizon < 4000) { horizon = 4000; }
            var origin = track.Direction(place.Station);
            var worst = 0;
            var step = horizon / 4;
            for (var distance = step; distance <= horizon; distance += step)
            {
                var sample = track.Travel(place, distance);
                var delta = Geom.AngleDelta(origin, track.Direction(sample.Station));
                if (delta < 0) { delta = -delta; }
                if (delta > worst) { worst = delta; }
            }

            return worst;
        }

        private static int SteerToward(Kart kart, Vec2i target)
        {
            var forward = new Vec2i(Trig.Cos(kart.Heading), Trig.Sin(kart.Heading));
            var to = target - kart.Position;
            var lateral = ((forward.X * (long)to.Y) - (forward.Y * (long)to.X)) / Trig.One;
            if (lateral > SteerWindow) { return 1; }
            if (lateral < -SteerWindow) { return -1; }
            return 0;
        }
    }
}
