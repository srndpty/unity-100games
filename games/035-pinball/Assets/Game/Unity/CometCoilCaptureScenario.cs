using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間と入力を使わず、意図と Tick を順に渡して場面を作って撮る。
    /// 意図の時刻はすべて 0 にする（時計で Tick を進めず、台本の StepTick だけで進める）。
    /// 最初の 1 球はキーの意図でプランジャーを引いて打ち、以後は操縦役（<see cref="Autopilot"/>）が遊ぶ。
    /// 試合のシードは <see cref="EvidenceCapture.Seed"/>、操縦役のシードは設定の <see cref="CometCoilSettings.CapturePilotSeed"/>（マルチボールの場面が撮れる値）。
    /// </summary>
    public sealed class CometCoilCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int TickLimit = 60 * 60 * 10;
        private const double ClockTime = 0;

        private CometCoilGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<CometCoilGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings.Rules));
            capture.Record("pilotSeed", _game.Settings.CapturePilotSeed.ToString(System.Globalization.CultureInfo.InvariantCulture));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（遊び方の説明と操作）");

            Key(IntentKind.Submit);
            Key(IntentKind.PlungerDown);
            Step(40);
            await Shoot(capture, "02-pull", "Space を押したまま 40 Tick：プランジャーを引くと球も下がる");

            Key(IntentKind.PlungerUp);
            Step(10);
            await Shoot(capture, "03-launch", "離すと引きに比例した速さで打ち出す（レーンを上る）");

            var pilot = new Autopilot();
            _game.Flow.Pilot = pilot;
            pilot.Reset(_game.Settings.CapturePilotSeed);

            StepUntil(() => _game.LastTickEvents.TargetHits > 0, "最初のターゲット");
            await Shoot(capture, "04-target", "右上の曲がりから台へ入った球がターゲットを倒した（薄く表示）");

            StepUntil(() => _game.LastTickEvents.BumperHits > 0, "最初のバンパー");
            await Shoot(capture, "05-bumper", "バンパーに当たった Tick：光って外向きに弾く");

            StepUntil(() => _game.LastTickEvents.FlipperHits > 0 && (Run.FlipperOf(FlipperSide.Left).Held || Run.FlipperOf(FlipperSide.Right).Held) && Run.BallAt(0).Velocity.Y > 0, "フリッパーで打つ");
            Step(2);
            await Shoot(capture, "06-flip", "フリッパーを上げて球を打ち返した直後");

            StepUntil(() => _game.LastTickEvents.MultiballStarted, "マルチボール");
            Step(45);
            await Shoot(capture, "07-multiball", "ターゲットを 3 枚倒してマルチボール：2 個目の球が自動で打ち出される");

            Key(IntentKind.Cancel);
            await Shoot(capture, "08-paused", "Esc で一時停止");
            Key(IntentKind.Cancel);

            StepUntil(() => _game.LastTickEvents.BallEnded, "1 球の終わり");
            await Shoot(capture, "09-drain", "台の上の最後の球がドレインへ落ちて 1 球が終わり、次の球がレーンへ出た");

            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "試合の終わり");
            await Shoot(capture, "10-result", "3 球を失って結果（得点と起動中の最高記録）");
        }

        private PinballRun Run => _game.Flow.Run ?? throw new InvalidOperationException("試合がありません。");

        private void Key(IntentKind kind)
        {
            _game.Apply(new Intent(kind, ClockTime));
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

        /// <summary><paramref name="done"/> が成り立つ Tick まで進める（1 Tick ごとに出来事を取り出して調べる）。</summary>
        private void StepUntil(Func<bool> done, string scene)
        {
            for (var i = 0; ; i++)
            {
                if (i > TickLimit || _game.Flow.Screen != AppScreen.Playing)
                {
                    if (done()) { return; }
                    throw new InvalidOperationException("撮影場面を作れませんでした: " + scene);
                }

                _game.Flow.StepTick();
                _game.ConsumeEvents();
                if (done()) { return; }
            }
        }

        private async Awaitable Shoot(CaptureContext capture, string name, string scene)
        {
            _game.Render();
            var run = _game.Flow.Run;
            var detail = run == null
                ? scene
                : $"{scene}。経過 {run.Ticks} Tick、得点 {run.Score}、球 {run.BallNumber}/{_game.Flow.Config.Balls}、台の上 {run.BallsActive} 個、ハッシュ 0x{run.Hash():X8}";
            await capture.ShootAsync(name, detail);
        }
    }
}
