using System;
using System.Collections.Generic;
using Game.Core;
using IntGeometry;

namespace Game.App.Tests
{
    /// <summary>
    /// 人間寄りの操縦役。人がキーボードで押せるキー（左右・上下が全開か 0、Shift、Space）だけを決め、<paramref name="delay"/> Tick 遅れて押す。
    /// 押したキーは <see cref="TurnAssist"/> を通す（実際のプレイと同じ）。左右は水平の方位（レーダーで見る向き）で決め、
    /// ほぼ正面なら上下で合わせる。低ければ翼を水平にして引き、区域の端では中心へ戻る。LOCK したら撃つ（自分のミサイルが飛んでいる間は撃たない）。
    /// </summary>
    internal sealed class KeyboardPilot : ISortiePilot
    {
        private readonly int _delay;
        private readonly Queue<(Controls Keys, bool Fire)> _queue = new();

        public KeyboardPilot(int delay)
        {
            _delay = delay;
        }

        public void Reset() => _queue.Clear();

        public PlayerCommand CommandFor(Mission mission)
        {
            var flight = mission.Flight;
            var view = mission.Player.View;
            const int Full = Controls.Full;
            _queue.Enqueue(view.Grounded
                ? (new Controls(view.Air.Speed >= flight.RotateSpeed ? Full : 0, 0, 0, Full), false)
                : Decide(mission, view));
            var (keys, fire) = _queue.Count > _delay ? _queue.Dequeue() : (new Controls(0, 0, 0, Full), false);
            return new PlayerCommand(TurnAssist.Apply(flight, view, keys), fire, false);
        }

        private static (Controls Keys, bool Fire) Decide(Mission mission, AircraftView view)
        {
            const int Full = Controls.Full;
            var bank = TurnAssist.BankOf(view.Attitude);
            if (view.Position.Y < 300 * Units.PerMeter || view.Position.Y + (view.Velocity.Y * 120) < 150 * Units.PerMeter)
            {
                return (new Controls(Math.Abs(bank) < Units.Degrees(45) ? Full : 0, 0, 0, Full), false);
            }

            var half = mission.Config.ArenaHalfWidth * Units.PerMeter * 8 / 10;
            Vec3l point;
            if (Math.Abs(view.Position.X) > half || Math.Abs(view.Position.Z) > half)
            {
                point = new Vec3l(0, view.Position.Y, 0);
            }
            else
            {
                var target = Nearest(mission, view);
                if (target == null) { return (new Controls(0, 0, 0, Full), false); }
                point = target.Value;
            }

            var d = point - view.Position;
            var dm = new Vec3l(d.X / Units.PerMillimeter, d.Y / Units.PerMillimeter, d.Z / Units.PerMillimeter);
            var velocity = view.Velocity;
            var bearing = Fixed.Wrap(Trig.Atan2(dm.X, dm.Z, 0) - Trig.Atan2(velocity.X, velocity.Z, 0));
            var up = Vec3l.Project(dm, view.Attitude.Up);
            var ahead = Vec3l.Project(dm, view.Attitude.Forward);
            var elevation = Fixed.Wrap(Trig.Atan2(up, Math.Abs(ahead) + 1, 0));
            int roll = 0, pitch = 0;
            if (Math.Abs(bearing) > Units.Degrees(10)) { roll = bearing >= 0 ? Full : -Full; }
            else if (elevation > Units.Degrees(2)) { pitch = Full; }
            else if (elevation < -Units.Degrees(2)) { pitch = -Full; }

            var fire = mission.PlayerLocked;
            for (var i = 0; i < mission.Missiles.Count; i++)
            {
                var missile = mission.Missiles[i];
                if (missile.Active && missile.Tracking && missile.Owner == 0) { fire = false; }
            }

            return (new Controls(pitch, roll, 0, Full), fire);
        }

        private static Vec3l? Nearest(Mission mission, AircraftView view)
        {
            var locked = mission.Player.LockTarget;
            if (locked > 0 && !mission.Fighters[locked].Down) { return mission.Fighters[locked].View.Position; }
            Vec3l? best = null;
            var bestDistance = long.MaxValue;
            for (var i = 1; i < mission.Fighters.Count; i++)
            {
                var enemy = mission.Fighters[i];
                if (enemy.Down) { continue; }
                var distance = (enemy.View.Position - view.Position).Length;
                if (distance < bestDistance)
                {
                    best = enemy.View.Position;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }
}
