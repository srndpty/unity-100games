using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// タイトル、滑走、離陸、ロック中、LOCK、発射、撃墜、ミサイル警告、成功の結果（操縦役）と、
    /// 失速、墜落の結果（決まった操縦）を Tick で進めて撮る。
    /// </summary>
    public sealed class HalcyonCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = GetComponent<HalcyonGame>();
            await capture.WaitUntilAsync(() => game.HudRoot != null, 10f);
            capture.Record("flight", JsonUtility.ToJson(game.Settings.Flight));
            capture.Record("mission", JsonUtility.ToJson(game.Settings.Mission));
            game.Render();
            await capture.ShootAsync("01-title", "タイトルと操作説明");

            var flow = game.Flow;
            flow.Pilot = new Autopilot();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            capture.Record("seed", flow.Mission.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var mission = flow.Mission;
            var flight = mission.Flight;
            StepUntil(flow, () => mission.Player.View.Air.Speed >= 40L * Units.MetersPerSecond, "滑走");
            await Shoot(game, capture, "02-roll", "滑走路を加速する");

            StepUntil(flow, () => !mission.Player.View.Grounded, "離陸");
            Step(flow, 45);
            await Shoot(game, capture, "03-takeoff", "離陸（機首の十字と速度の円がずれる）");

            StepUntil(flow, () => mission.Player.LockTarget > 0 && mission.Player.LockTicks >= mission.LockTicksFor(mission.Player) / 2, "ロック中");
            await Shoot(game, capture, "04-locking", "ロック中");

            StepUntil(flow, () => mission.PlayerLocked, "LOCK");
            await Shoot(game, capture, "05-lock", "LOCK");

            StepUntil(flow, () => Count(mission, owner: 0) > 0, "発射");
            Step(flow, 25);
            await Shoot(game, capture, "06-launch", "ミサイルの発射と煙");

            StepUntil(flow, () => mission.EnemiesLeft < MissionConfig.EnemyCount, "撃墜");
            Step(flow, 10);
            await Shoot(game, capture, "07-kill", "撃墜");

            StepUntil(flow, () => Incoming(mission), "ミサイル警告");
            await Shoot(game, capture, "08-warning", "敵のミサイルの警告");

            StepUntil(flow, () => flow.Screen == AppScreen.Result, "成功の結果");
            await Shoot(game, capture, "09-complete", "成功の結果");

            // 2 本目：離陸して上昇し、スロットルを絞って引き続けて失速し、そのまま地面へ。
            flow.Pilot = new StallPilot(flight);
            flow.Apply(new Intent(IntentKind.Submit, 0));
            mission = flow.Mission;
            StepUntil(flow, () => mission.Player.View.Air.Stalled, "失速");
            Step(flow, 20);
            await Shoot(game, capture, "10-stall", "失速（STALL、機首が下がる）");

            StepUntil(flow, () => flow.Screen == AppScreen.Result, "失敗の結果");
            await Shoot(game, capture, "11-failed", "失敗の結果（墜落）");
        }

        private static async Awaitable Shoot(HalcyonGame game, CaptureContext capture, string name, string note)
        {
            game.Render();
            await capture.ShootAsync(name, note);
        }

        private static int Count(Mission mission, int owner)
        {
            var count = 0;
            foreach (var missile in mission.Missiles) { if (missile.Owner == owner) { count++; } }
            return count;
        }

        private static bool Incoming(Mission mission)
        {
            var position = mission.Player.View.Position;
            foreach (var missile in mission.Missiles)
            {
                if (missile.Active && missile.Tracking && missile.Target == 0 && (missile.Position - position).Length < 1200 * Units.PerMeter) { return true; }
            }

            return false;
        }

        private static void Step(SortieFlow flow, int ticks)
        {
            for (var i = 0; i < ticks; i++) { flow.StepTick(); }
        }

        private static void StepUntil(SortieFlow flow, Func<bool> done, string label)
        {
            for (var i = 0; i < 30000 && !done(); i++) { flow.StepTick(); }
            if (!done()) { throw new InvalidOperationException(label + " の場面になりませんでした。"); }
        }

        /// <summary>離陸して 800 m まで上がり、スロットルを絞って引き続ける（失速する）。500 m より上で失速したら、押し続けて地面へ。</summary>
        private sealed class StallPilot : ISortiePilot
        {
            private readonly FlightConfig _flight;
            private bool _stalled;

            public StallPilot(FlightConfig flight)
            {
                _flight = flight;
            }

            public void Reset() => _stalled = false;

            public PlayerCommand CommandFor(Mission mission)
            {
                var view = mission.Player.View;
                if (view.Grounded)
                {
                    return new PlayerCommand(new Controls(view.Air.Speed >= _flight.RotateSpeed ? Controls.Full : 0, 0, 0, Controls.Full), false, false);
                }

                if (view.Air.Stalled && view.Position.Y > 500 * Units.PerMeter) { _stalled = true; }
                if (_stalled) { return new PlayerCommand(new Controls(-Controls.Full, 0, 0, -Controls.Full), false, false); }
                if (view.Position.Y < 800 * Units.PerMeter)
                {
                    var climb = view.Position + new Vec3l(view.Velocity.X * 120, 900 * Units.PerMeter, view.Velocity.Z * 120);
                    return new PlayerCommand(Steering.Toward(_flight, view, climb, 180L * Units.MetersPerSecond), false, false);
                }

                return new PlayerCommand(new Controls(Controls.Full, 0, 0, -Controls.Full), false, false);
            }
        }
    }
}
