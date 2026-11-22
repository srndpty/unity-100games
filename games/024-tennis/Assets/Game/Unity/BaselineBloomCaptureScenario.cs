using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間と入力を使わず、意図と Tick を順に渡して場面を作って撮る。試合のシードは <see cref="EvidenceCapture.Seed"/>。
    /// 意図の時刻はすべて 0 にする（時計で Tick を進めず、台本の StepTick だけで進める）。
    /// </summary>
    public sealed class BaselineBloomCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int TickLimit = 60 * 60 * 30;
        private const double ClockTime = 0;

        private BaselineBloomGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<BaselineBloomGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));
            var config = _game.Flow.Config;

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（最初のポイントの立ち位置を描く。試合はまだない）");

            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            await Shoot(capture, "02-start", "試合の開始。プレイヤーがデュースサイドからサーブ（案内：Z でトス）");

            // 1 本目：トスを上げ、頂点より遅く打ってネット（フォルト）。
            Tap(Control.Stroke);
            StepUntil(() => M.Rally.Phase == RallyPhase.Toss && M.Rally.TossTick >= config.TossApexTick - 3, "トス");
            await Shoot(capture, "03-toss", "トスが頂点の手前。もう一度 Z で打つ");
            StepUntil(() => M.Rally.TossTick >= config.TossApexTick + config.ServeTimingSpan, "遅いサーブ");
            Tap(Control.Stroke);
            StepUntil(() => M.Rally.Phase == RallyPhase.Ready, "フォルト");
            await Shoot(capture, "04-fault", "頂点より遅く打ったサーブがネットにかかり FAULT。セカンドサーブへ");

            // 2 本目：頂点で打つ。
            Tap(Control.Stroke);
            StepUntil(() => M.Rally.TossTick >= config.TossApexTick, "2 本目のトス");
            Tap(Control.Stroke);
            Step(10);
            await Shoot(capture, "05-serve", "頂点で打ったセカンドサーブが対角のサービスボックスへ飛ぶ");

            // ここからプレイヤー側は自動操縦（CPU と同じ判断）で打ち合う。
            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => M.Phase == MatchPhase.Gap || (M.Rally.Shots >= 4 && M.Ball.Position.Z > 15000), "ラリー");
            await Shoot(capture, "06-rally", "ラリー。球の高さは影との離れで表す");

            StepUntil(() => M.Phase == MatchPhase.Gap, "ポイント");
            Step(20);
            await Shoot(capture, "07-point", "ポイントが決まった。判定と得点した側を出し、最後のバウンドに跡を残す");

            StepUntil(() => M.Phase == MatchPhase.Point && M.PointsPlayed == 1 && M.Rally.Phase == RallyPhase.Toss, "サーブの交代");
            await Shoot(capture, "08-server-change", "1 ポイント後、タイブレークの順で CPU のサーブ（アドサイド）。得点板の ● がサーバー");

            // 早打ちと遅打ち：同じ入球を、理想の Tick より 3 Tick 早く・2 Tick 遅く打つ（3 Tick 遅いとネットにかかる）。
            await TimedHit(capture, -3, "09-early", "早打ち。理想の打点よりネット寄りで打ち、左へ長く飛ぶ（案内：早い）");
            await TimedHit(capture, 2, "10-late", "遅打ち。体の横で打ち、右へ低く飛ぶ（案内：遅い）");

            // 判定：アウトとネット。
            _game.Flow.Pilot = new IdlePilot();
            Stage(ShotSolver.Solve(NearContact, 15000, Court.HalfLength + 15000, config.StrokeSpeed, config.StrokeClearance, config.Gravity));
            StepUntil(() => M.Phase == MatchPhase.Gap, "アウト");
            Step(10);
            await Shoot(capture, "11-out", "ベースラインの外に落ちてアウト（打った側の失点）。跡がラインの外にある");
            StepUntil(() => M.Phase == MatchPhase.Point, "次のポイント");

            Stage(ShotSolver.Solve(NearContact, 0, 80000, config.StrokeSpeed, -6000, config.Gravity));
            StepUntil(() => M.Phase == MatchPhase.Gap, "ネット");
            Step(10);
            await Shoot(capture, "12-net", "ネットにかかった（打った側の失点）");
            StepUntil(() => M.Phase == MatchPhase.Point, "次のポイント");

            Apply(Intent.Of(IntentKind.TogglePause, ClockTime));
            await Shoot(capture, "13-pause", "一時停止（操作の説明）");
            Apply(Intent.Of(IntentKind.Submit, ClockTime));

            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "結果");
            await Shoot(capture, "14-result", "試合終了の結果（勝者・ポイント数・いちばん長いラリー・試合時間）");
        }

        private static Vec3 NearContact => new(5000, -110000, 9000);

        private TennisMatch M => _game.Flow.Match ?? throw new InvalidOperationException("試合がありません。");

        /// <summary>手前の選手が今打った球として場面を作る（奥の選手は CPU のまま）。</summary>
        private void Stage(Vec3 velocity)
        {
            var near = new Vec3(0, -112000, 0);
            M.StageRally(Side.Near, NearContact, velocity, near, new Vec3(0, 120000, 0));
            _game.Render();
        }

        /// <summary>奥から手前の選手へ向かう球を、理想の打点の Tick から <paramref name="shift"/> ずらして打つ。</summary>
        private async Awaitable TimedHit(CaptureContext capture, int shift, string name, string scene)
        {
            var config = M.Config;
            var from = new Vec3(0, 100000, 9000);
            var near = new Vec3(0, -112000, 0);
            M.StageRally(Side.Far, from, ShotSolver.Solve(from, 0, -80000, config.StrokeSpeed, config.StrokeClearance, config.Gravity), near, from.WithZ(0));

            // 球が理想の打点（の y）を通り過ぎる Tick を予測し、打点がそこからずれるように押す。
            var idealY = HitWindow.IdealContact(Side.Near, near, config).Y;
            var copy = M.Ball.Clone();
            var contact = 0;
            while (copy.Position.Y > idealY && contact < 600)
            {
                copy.Step(config.Physics);
                contact++;
            }

            _game.Flow.Pilot = new TimedSwingPilot(M.Tick + contact - config.ImpactTicks + shift);
            StepUntil(() => M.Rally.LastHitter == Side.Near && M.Ball.Position.Y > 30000, name);
            await Shoot(capture, name, scene);
            StepUntil(() => M.Phase == MatchPhase.Point && M.Rally.Phase != RallyPhase.InPlay, "次のポイント");
        }

        private void Tap(Control control)
        {
            Apply(Intent.Down(control, ClockTime));
            Step(1);
            Apply(Intent.Up(control, ClockTime));
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
            for (var i = 0; ; i++)
            {
                if (_game.Flow.Match != null && done()) { return; }
                if (i > TickLimit || (_game.Flow.Screen != AppScreen.Playing && !done()))
                {
                    throw new InvalidOperationException("撮影場面を作れませんでした: " + scene);
                }

                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }
        }

        private async Awaitable Shoot(CaptureContext capture, string name, string scene)
        {
            _game.Render();
            var m = _game.Flow.Match;
            var detail = m == null
                ? scene
                : $"{scene}。Tick {m.Tick}、得点 {m.Score.Points(Side.Near)}-{m.Score.Points(Side.Far)}、球 {m.Ball.Position}、手前 {m.Near.Position}、奥 {m.Far.Position}";
            await capture.ShootAsync(name, detail);
        }

        /// <summary>何もしない操縦役（判定の場面を作るとき）。</summary>
        private sealed class IdlePilot : IPilot
        {
            public AthleteCommand CommandFor(TennisMatch match) => AthleteCommand.Idle;
        }

        /// <summary>決まった Tick にだけストロークを押す操縦役。</summary>
        private sealed class TimedSwingPilot : IPilot
        {
            private readonly long _pressTick;

            public TimedSwingPilot(long pressTick) => _pressTick = pressTick;

            public AthleteCommand CommandFor(TennisMatch match) => match.Tick + 1 == _pressTick ? new AthleteCommand(0, 0, true, false) : AthleteCommand.Idle;
        }
    }
}
