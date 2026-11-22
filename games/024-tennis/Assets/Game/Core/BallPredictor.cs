using System;

namespace Game.Core
{
    /// <summary>予測した打点：今から何 Tick 後（球の Step の回数）に、どの位置にあるか。</summary>
    public readonly struct PredictedContact
    {
        public PredictedContact(int ticks, Vec3 position)
        {
            Ticks = ticks;
            Position = position;
        }

        public int Ticks { get; }

        public Vec3 Position { get; }
    }

    /// <summary>予測した球の出来事：今から何 Tick 後に、何が起きるか。</summary>
    public readonly struct PredictedEvent
    {
        public PredictedEvent(int ticks, BallEvent ballEvent)
        {
            Ticks = ticks;
            Event = ballEvent;
        }

        public int Ticks { get; }

        public BallEvent Event { get; }
    }

    /// <summary>
    /// 着地点の予測。球を複製して本番と同じ <see cref="Ball.Step"/> で進めるので、予測と実際の軌跡は Tick 単位で一致する
    /// （途中で誰も打たなければ）。
    /// </summary>
    public static class BallPredictor
    {
        /// <summary>次の出来事（バウンドかネット）。<paramref name="maxTicks"/> までに起きなければ null。</summary>
        public static PredictedEvent? NextEvent(Ball ball, BallPhysics physics, int maxTicks)
        {
            if (ball == null) { throw new ArgumentNullException(nameof(ball)); }
            var copy = ball.Clone();
            for (var tick = 1; tick <= maxTicks; tick++)
            {
                var e = copy.Step(physics);
                if (e.Kind != BallEventKind.None) { return new PredictedEvent(tick, e); }
            }

            return null;
        }

        /// <summary>
        /// <paramref name="side"/> の選手が返球する打点を探す。最初のバウンドがアウト（サーブならサービスボックスの外）かネットなら見送り（null）。
        /// 1 回弾んだ後、打てる高さ（上下に 1 割の余裕）にある Tick のうち、
        /// 理想の打点にできる位置へ間に合うもの（下りてくるものを優先）を選ぶ。間に合うものがなければ最初の候補。
        /// </summary>
        public static PredictedContact? FindContact(Ball ball, Rally rally, Side side, Vec3 athletePosition, int speed, TennisConfig config, int maxTicks)
        {
            if (ball == null) { throw new ArgumentNullException(nameof(ball)); }
            if (rally == null) { throw new ArgumentNullException(nameof(rally)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }

            var copy = ball.Clone();
            var bounces = rally.Bounces;
            var serveInFlight = rally.ServeInFlight;
            var forward = Court.Forward(side);
            var margin = (config.MaxHitZ - config.MinHitZ) / 10;
            var probe = new Athlete(side, athletePosition);
            PredictedContact? reachable = null;
            PredictedContact? first = null;
            for (var tick = 1; tick <= maxTicks; tick++)
            {
                var e = copy.Step(config.Physics);
                if (e.Kind == BallEventKind.Net) { return null; }
                if (e.Kind == BallEventKind.Bounce)
                {
                    if (bounces >= 1) { break; }
                    var valid = serveInFlight
                        ? Court.InServiceBox(e.Point.X, e.Point.Y, rally.Server, rally.ServeCourt)
                        : Court.InSingles(e.Point.X, e.Point.Y, side);
                    if (!valid) { return null; }
                    bounces = 1;
                    serveInFlight = false;
                }

                var p = copy.Position;
                if (bounces < 1 || p.Y * forward >= 0 || p.Z < config.MinHitZ + margin || p.Z > config.MaxHitZ - margin) { continue; }

                var candidate = new PredictedContact(tick, p);
                first ??= candidate;
                var stand = new Vec3(p.X, p.Y - (config.ContactAhead * forward), 0);
                var clamped = probe.Clamp(stand, config);
                var distance = Units.Sqrt(clamped.FlatDistanceSquared(athletePosition));
                // 斜めに動くと各成分が 181/256 倍になるので、間に合うかは控えめに見積もる。
                var inTime = clamped == stand && distance <= (long)speed * tick * 181 / 256;
                if (!inTime) { continue; }
                if (copy.Velocity.Z < 0) { return candidate; }
                reachable ??= candidate;
            }

            return reachable ?? first;
        }
    }
}
