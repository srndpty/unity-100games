using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間と入力を使わず、意図と Tick を順に渡して場面を作って撮る。乱数は使わない。
    /// 意図の時刻はすべて 0 にする（時計で Tick を進めず、台本の StepTick だけで進める）。
    /// </summary>
    public sealed class GlintAscentCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int TickLimit = 60 * 60 * 10;
        private const double ClockTime = 0;

        private GlintAscentGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<GlintAscentGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（1 部屋目の配置を Stage Prefab のまま描く。挑戦はまだない）");

            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            await Shoot(capture, "02-start", "1 部屋目の開始直後");

            // 入力の列（Routes）で進め、各動作の途中を撮る。
            _game.Flow.Pilot = new RoutePilot(Routes.All());
            StepUntil(() => Climb.RoomIndex == 0 && !Run.Player.Grounded && Run.Player.VelocityY > 40, FlowEvents.None, "ジャンプ");
            await Shoot(capture, "03-jump", "1 部屋目、跳び上がっている途中");

            StepUntil(() => Climb.RoomIndex == 1 && Run.Player.IsDashing, FlowEvents.None, "ダッシュ");
            Step(3);
            await Shoot(capture, "04-dash", "2 部屋目、空中のダッシュ（使い切ると自機が青くなる）");

            StepUntil(() => Climb.RoomIndex == 2, FlowEvents.WallJumped, "壁蹴り");
            Step(4);
            await Shoot(capture, "05-walljump", "3 部屋目の煙突、壁を蹴って反対の壁へ跳んだ直後");

            StepUntil(() => Climb.RoomIndex == 5 && Run.Player.IsDashing && Run.Player.VelocityX != 0 && Run.Player.VelocityY != 0, FlowEvents.None, "斜めのダッシュ");
            Step(3);
            await Shoot(capture, "06-diagonal", "6 部屋目、斜め上へのダッシュ");

            StepUntil(() => Climb.RoomIndex == 9 && Run.Player.Body.X > 20 * Units.Tile, FlowEvents.None, "10 部屋目");
            await Shoot(capture, "07-summit", "10 部屋目（最後の部屋）の途中");

            StepUntil(() => _game.Flow.Screen == AppScreen.Result, FlowEvents.None, "完走");
            await Shoot(capture, "08-result", "入力の列で 10 部屋を死亡なしで抜けた結果");

            // 2 部屋目で右へ歩き続けて、トゲの谷に落ちる。
            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            var routes = Routes.All();
            _game.Flow.Pilot = new RoutePilot(new[] { routes[0], RouteScript.Parse("R600") });
            StepUntil(() => true, FlowEvents.Died, "死亡");
            Step(6);
            Require(Run.Status == RunStatus.Dying && Run.LastDeath == DeathCause.Spike, "トゲで死亡");
            await Shoot(capture, "09-death", "2 部屋目、トゲに触れて死亡（入力を受け付けない短い演出）");

            StepUntil(() => true, FlowEvents.Respawned, "やり直し");
            await Shoot(capture, "10-respawn", "部屋の入口に戻ってすぐやり直す。死亡回数が 1 増え、ダッシュは戻る");

            _game.Flow.Pilot = null;
            Apply(Intent.Of(IntentKind.TogglePause, ClockTime));
            await Shoot(capture, "11-paused", "一時停止のメニュー");
        }

        private Climb Climb => _game.Flow.Climb ?? throw new InvalidOperationException("挑戦がありません。");

        private RoomRun Run => Climb.Run;

        private void Apply(Intent intent) => _game.Flow.Apply(intent);

        private void Step(int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                _game.Flow.StepTick();
                _game.Flow.TakeEvents();
            }
        }

        /// <summary><paramref name="done"/> が成り立ち、<paramref name="required"/> の出来事が起きた Tick まで進める。</summary>
        private void StepUntil(Func<bool> done, FlowEvents required, string scene)
        {
            for (var i = 0; ; i++)
            {
                if (i > TickLimit || _game.Flow.Screen != AppScreen.Playing)
                {
                    throw new InvalidOperationException("撮影場面を作れませんでした: " + scene);
                }

                _game.Flow.StepTick();
                var events = _game.Flow.TakeEvents();
                if (done() && (events & required) == required) { return; }
                if (_game.Flow.Screen == AppScreen.Result && done()) { return; }
            }
        }

        private async Awaitable Shoot(CaptureContext capture, string name, string scene)
        {
            _game.Render();
            var climb = _game.Flow.Climb;
            var detail = climb == null
                ? scene
                : $"{scene}。{climb.RoomIndex + 1} 部屋目、経過 {climb.ElapsedTicks} Tick、自機 ({climb.Run.Player.Body.X}, {climb.Run.Player.Body.Y})、死亡 {climb.Deaths}";
            await capture.ShootAsync(name, detail);
        }

        private static void Require(bool condition, string scene)
        {
            if (!condition) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
        }
    }
}
