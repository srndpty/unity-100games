using System;
using Game.Core;

namespace Game.App
{
    /// <summary>キーの代わりに、Tick ごとの命令を決める操縦役。</summary>
    public interface ISortiePilot
    {
        /// <summary>ミッションを始める前に呼ぶ。前のミッションの判断を捨てる。</summary>
        void Reset();

        PlayerCommand CommandFor(Mission mission);
    }

    /// <summary>
    /// プレイヤー枠の操縦役。離陸 → 上昇 → 最も近い敵へ向かう（敵と同じ <see cref="Steering"/>）→ LOCK で発射。
    /// 自分のミサイルがまだ目標を追っていれば次は撃たない。敵のミサイルは避けない（避けると撃たれる時間が延びて勝ちにくかった）。
    /// 撮影・App テスト・PlayMode で共有する。
    /// </summary>
    public sealed class Autopilot : ISortiePilot
    {
        /// <summary>上昇を終えて敵へ向かう高度。</summary>
        public static readonly long ClimbAltitude = 600 * Units.PerMeter;

        /// <summary>保とうとする速さ。</summary>
        public static readonly long Cruise = 180 * Units.MetersPerSecond;

        private Mission? _mission;
        private FlightConfig? _flight;
        private int _arenaHalfWidth;

        public void Reset()
        {
            _mission = null;
            _flight = null;
            _arenaHalfWidth = 0;
        }

        public PlayerCommand CommandFor(Mission mission)
        {
            if (mission == null) { throw new ArgumentNullException(nameof(mission)); }
            if (!ReferenceEquals(mission, _mission))
            {
                _mission = mission;
                _flight = mission.Flight;
                _arenaHalfWidth = mission.Config.ArenaHalfWidth;
            }

            var flight = _flight!;
            var me = mission.Player;
            var view = me.View;
            if (view.Grounded)
            {
                var rotate = view.Air.Speed >= flight.RotateSpeed ? Controls.Full : 0;
                return new PlayerCommand(new Controls(rotate, 0, 0, Controls.Full), false, false);
            }

            var point = Steering.Recovery(view, 300 * Units.PerMeter)
                ?? Steering.StayInside(view, MissionConfig.Meters(_arenaHalfWidth) * 4 / 5)
                ?? (view.Position.Y < ClimbAltitude ? ClimbPoint(view) : AttackPoint(mission, view));
            var controls = Steering.Toward(flight, view, point, Cruise);
            var fire = mission.PlayerLocked && !Chasing(mission, me.LockTarget);
            return new PlayerCommand(controls, fire, false);
        }

        private static Vec3l ClimbPoint(AircraftView view)
        {
            var v = view.Velocity;
            return new Vec3l(view.Position.X + (v.X * 120), ClimbAltitude + (400 * Units.PerMeter), view.Position.Z + (v.Z * 120));
        }

        private static Vec3l AttackPoint(Mission mission, AircraftView view)
        {
            var target = Target(mission, view);
            if (target == null) { return view.Position + Vec3l.MulDiv(view.Velocity, 120, 1); }
            var t = target.View;
            var distance = (t.Position - view.Position).Length;

            // 近いほど先読みを短く（最大 1 秒）。
            var lead = Math.Min(60, distance / (400 * Units.MetersPerSecond));
            return t.Position + Vec3l.MulDiv(t.Velocity, lead, 1);
        }

        /// <summary>LOCK しかけている敵、なければ最も近い敵。</summary>
        private static Fighter? Target(Mission mission, AircraftView view)
        {
            var me = mission.Player;
            if (me.LockTarget > 0 && !mission.Fighters[me.LockTarget].Down) { return mission.Fighters[me.LockTarget]; }
            Fighter? best = null;
            var bestDistance = long.MaxValue;
            for (var i = 1; i < mission.Fighters.Count; i++)
            {
                var enemy = mission.Fighters[i];
                if (enemy.Down) { continue; }
                var distance = (enemy.View.Position - view.Position).Length;
                if (distance < bestDistance)
                {
                    best = enemy;
                    bestDistance = distance;
                }
            }

            return best;
        }

        /// <summary>自分のミサイルがまだ <paramref name="target"/> を追っているか。</summary>
        private static bool Chasing(Mission mission, int target)
        {
            foreach (var missile in mission.Missiles)
            {
                if (missile.Active && missile.Tracking && missile.Owner == 0 && missile.Target == target) { return true; }
            }

            return false;
        }
    }
}
