using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// ウェイポイントと曲率の速度計画。その Tick の始めの全車の状態だけから操作を決める。
    /// 中心線に沿って前の <see cref="KartConfig.AvoidRange"/> 以内に、横の間が直径 + <see cref="KartConfig.AvoidGap"/> より狭い車がいれば、
    /// 向ける点をその車の横へずらして追い越す。
    /// </summary>
    public static class CpuPlan
    {
        private const int SteerWindow = 350;

        /// <summary>中心線からずらせる横の距離の、ターマックの端からの残り（mm）。</summary>
        private const int EdgeMargin = 500;

        public static Drive Intent(Kart[] karts, int index, Track track, KartConfig config, int pacePermille)
        {
            if (karts == null) { throw new ArgumentNullException(nameof(karts)); }
            if (track == null) { throw new ArgumentNullException(nameof(track)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }

            var kart = karts[index];
            if (kart.Finished) { return Drive.None; }
            var place = new TrackPlace(kart.Station, kart.Along);
            var ahead = track.Travel(place, config.Lookahead);
            var lane = kart.RecoverLeft > 0 ? 0 : AvoidLane(karts, index, track, config);
            var steer = SteerToward(kart, track.Offset(ahead, lane));
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

        /// <summary>
        /// 前のいちばん近い、同じ筋の車の横を通る横の位置（左が正）。いなければ 0（中心線）。
        /// 自分がその車より右（同じを含む）なら右を、左なら左を通る。前の車は中心線へ戻ってくるので、
        /// 通る筋はその車と中心線の両方から直径 + 余裕だけ離す。ターマックの端から <see cref="EdgeMargin"/> 内に収める。
        /// 完走した車は止まった幽霊なのでよけない。
        /// </summary>
        public static int AvoidLane(Kart[] karts, int index, Track track, KartConfig config)
        {
            if (karts == null) { throw new ArgumentNullException(nameof(karts)); }
            if (track == null) { throw new ArgumentNullException(nameof(track)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }

            var self = karts[index];
            var place = new TrackPlace(self.Station, self.Along);
            var lateral = track.LateralOf(self.Position, self.Station);
            var clearance = (config.Radius * 2) + config.AvoidGap;
            var nearest = int.MaxValue;
            var blocker = 0;
            var found = false;
            for (var i = 0; i < karts.Length; i++)
            {
                if (i == index || karts[i].Finished) { continue; }
                var other = karts[i];
                var gap = track.AdvanceOf(place, new TrackPlace(other.Station, other.Along));
                if (gap <= 0 || gap > config.AvoidRange || gap >= nearest) { continue; }
                var otherLateral = track.LateralOf(other.Position, self.Station);
                if (Math.Abs(otherLateral - lateral) >= clearance) { continue; }
                nearest = gap;
                blocker = otherLateral;
                found = true;
            }

            if (!found) { return 0; }
            var room = track.HalfWidth(self.Station) - config.Radius - EdgeMargin;
            if (room <= 0) { return 0; }
            var lane = lateral <= blocker ? Math.Min(blocker, 0) - clearance : Math.Max(blocker, 0) + clearance;
            return IntMath.Clamp(lane, -room, room);
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
