using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間と入力を使わず、意図と Tick を順に渡して場面を作って撮る。シードは撮影用の固定値（<see cref="EvidenceCapture.Seed"/>）。
    /// 意図の時刻はすべて 0 にする（時計で Tick を進めず、台本の StepTick だけで進める）。
    /// </summary>
    public sealed class CinderLanesCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const double ClockTime = 0;

        private CinderLanesGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<CinderLanesGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));
            var config = _game.Flow.Rules.Config;

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（操作説明。道の上に立った自機）");

            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            _game.Flow.Pilot = new Autopilot();
            Step(50);
            await Shoot(capture, "02-runway", "走り出し。最初の 30 m は障害物のない助走、奥にチャンクが並ぶ");

            StepUntil(() => Happened(FlowEvents.Steered), "レーン移動");
            Step(4);
            await Shoot(capture, "03-steer", "自動操縦が隣のレーンへ移っている途中（8 Tick で移る。移る向きへ傾く）");

            StepUntil(() => R.Body.Vertical.Y > config.HurdleHeight && Near(ObstacleKind.Hurdle, R.Body.Lane.To, 1), "Hurdle を跳ぶ");
            await Shoot(capture, "04-jump", "低い柵（Hurdle）を跳び越えている");

            StepUntil(() => R.Body.Vertical.IsSliding && Near(ObstacleKind.Beam, R.Body.Lane.To, 1), "Beam をくぐる");
            await Shoot(capture, "05-slide", "頭上の梁（Beam）の下をスライドでくぐっている");

            StepUntil(() => !R.Body.Lane.IsMoving && (Near(ObstacleKind.Wall, R.Body.Lane.To - 1, 0) || Near(ObstacleKind.Wall, R.Body.Lane.To + 1, 0)), "壁の横");
            await Shoot(capture, "06-wall", "隣のレーンの壁（Wall）の横を走り抜けている");

            var cap = Difficulty.CapMeters(config);
            StepUntil(() => R.Meters >= cap + 40, "難度の上限");
            await Shoot(capture, "07-top-speed", $"{cap} m を越えて速さ・難度とも上限（SPEED {config.MaxSpeedLevel + 1}）。以後はこのまま続く");

            Apply(Intent.Of(IntentKind.TogglePause, ClockTime));
            await Shoot(capture, "08-pause", "一時停止");
            Apply(Intent.Of(IntentKind.Submit, ClockTime));

            // 操作をやめると、次の障害物にぶつかって終わる。
            _game.Flow.Pilot = new IdlePilot();
            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "衝突");
            await Shoot(capture, "09-result", "操作をやめて障害物にぶつかった結果（距離・最高記録）。自機は触れた時刻の位置で止めて描く");
        }

        private RunnerRun R => _game.Flow.Run ?? throw new InvalidOperationException("試合がありません。");

        private bool Happened(FlowEvents events) => (_game.LastEvents & events) != 0;

        /// <summary>レーン <paramref name="lane"/> の種類 <paramref name="kind"/> の障害物が、自機の中心から前後 <paramref name="meters"/> m 以内に重なるか。</summary>
        private bool Near(ObstacleKind kind, int lane, int meters)
        {
            var run = R;
            var reach = (long)meters * (long)CinderLanesStage.UnitsPerMeter;
            for (var i = 0; i < run.Chunks.Count; i++)
            {
                var start = run.Chunks.StartAt(i);
                var obstacles = run.Chunks.ChunkAt(i).Obstacles;
                for (var o = 0; o < obstacles.Count; o++)
                {
                    var obstacle = obstacles[o];
                    if (obstacle.Kind != kind || obstacle.Lane != lane) { continue; }
                    var box = obstacle.Box(run.Config);
                    var from = start + box.Z - reach;
                    var to = start + box.Z + box.Depth + reach;
                    if (run.Z >= from && run.Z <= to) { return true; }
                }
            }

            return false;
        }

        /// <summary>何もしない操縦役（衝突の場面を作る）。</summary>
        private sealed class IdlePilot : IPilot
        {
            public Command CommandFor(RunnerRun run) => Command.None;
        }

        private void Apply(Intent intent)
        {
            _game.Flow.Apply(intent);
            _game.ConsumeEvents();
        }

        private void Step(int ticks)
        {
            for (var i = 0; i < ticks && _game.Flow.Screen == AppScreen.Playing; i++)
            {
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }
        }

        /// <summary><paramref name="done"/> が成り立つ Tick まで進める。</summary>
        private void StepUntil(Func<bool> done, string scene)
        {
            for (var i = 0; i < 1_000_000; i++)
            {
                if (_game.Flow.Run != null && done()) { return; }
                if (_game.Flow.Screen != AppScreen.Playing) { break; }
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }

            throw new InvalidOperationException("撮影場面を作れませんでした: " + scene);
        }

        private async Awaitable Shoot(CaptureContext capture, string name, string scene)
        {
            _game.Render();
            var run = _game.Flow.Run;
            var detail = run == null
                ? scene
                : $"{scene}。{run.Ticks} Tick、{run.Meters} m、速さ {run.Speed}、レーン {run.Body.Lane.To}、高さ {run.Body.Vertical.Y}";
            await capture.ShootAsync(name, detail);
        }
    }
}
