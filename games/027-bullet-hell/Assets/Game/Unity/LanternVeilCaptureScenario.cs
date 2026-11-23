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
    /// 1 試合目は自動操縦で 6 弾幕を通して勝ち、2 試合目は動かずに撃ち続けて低速の表示・被弾・ボム・一時停止・負け・再挑戦を撮る。
    /// </summary>
    public sealed class LanternVeilCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const double ClockTime = 0;

        /// <summary>弾幕が始まってから撮るまでの Tick 数（弾が画面に広がるのを待つ）。</summary>
        private const int PatternShotDelay = 300;

        private LanternVeilGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<LanternVeilGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings.Rules));
            capture.Record("boss1", _game.Settings.Boss1.text);
            capture.Record("boss2", _game.Settings.Boss2.text);

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（操作説明。自機とボス 1 体目が始めの位置）");

            Apply(Intent.Of(IntentKind.Submit, ClockTime));
            _game.Flow.Pilot = new Autopilot();
            for (var stage = 0; stage < VeilConfig.BossCount * VeilConfig.PatternsPerBoss; stage++)
            {
                var target = stage;
                StepUntil(() => R.StageIndex == target && R.Phase == RunPhase.Pattern && R.CurrentPattern.TimeTicks - R.PatternTicksLeft >= PatternShotDelay, $"弾幕 {stage + 1}");
                await Shoot(capture, $"{stage + 2:00}-pattern{stage + 1}", $"弾幕 {stage + 1}「{R.CurrentPattern.Name}」（{R.CurrentBoss.Name}）。自動操縦がボスの真下で弾をくぐる");
            }

            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "勝利");
            await Shoot(capture, "08-victory", "6 弾幕を突破した結果（得点・プレイ時間・被弾・残機とボムのボーナス）");

            // 2 試合目（シード 2）：動かずに低速で撃ち続ける。
            Apply(Intent.Of(IntentKind.Restart, ClockTime));
            _game.Flow.Pilot = new StillPilot(bombOnce: false);
            StepUntil(() => R.Phase == RunPhase.Pattern && R.Bullets.Count >= 60, "低速");
            await Shoot(capture, "09-slow-hitbox", "Shift の低速中は、自機の中心に当たり判定の点（白い芯と赤い縁）を出す");

            StepUntil(() => Happened(FlowEvents.Hurt), "被弾");
            Step(12);
            await Shoot(capture, "10-hurt", "被弾：残機が 1 減り、敵弾を全消去。被弾した Tick を含めて 120 Tick は無敵（自機が半透明に点滅）");

            StepUntil(() => !R.IsInvincible && R.Bullets.Count >= 40, "ボムの前");
            _game.Flow.Pilot = new StillPilot(bombOnce: true);
            Step(24);
            await Shoot(capture, "11-bomb", "ボム：輪が広がる間は無敵で、敵弾を消し続け、ボスの体力を削る");

            Apply(Intent.Of(IntentKind.TogglePause, ClockTime));
            await Shoot(capture, "12-pause", "一時停止");
            Apply(Intent.Of(IntentKind.Submit, ClockTime));

            _game.Flow.Pilot = new StillPilot(bombOnce: false);
            StepUntil(() => _game.Flow.Screen == AppScreen.Result, "敗北");
            await Shoot(capture, "13-defeat", "残機 0 の結果。「このボスから再挑戦」（C）を出す");

            Apply(Intent.Of(IntentKind.RetryBoss, ClockTime));
            _game.Flow.Pilot = new Autopilot();
            StepUntil(() => R.Phase == RunPhase.Pattern && R.Bullets.Count >= 40, "再挑戦");
            await Shoot(capture, "14-retry", "負けたボスの 1 つ目の弾幕から、残機とボムを戻して再挑戦（得点はボスを始めたときの値）");
        }

        private VeilRun R => _game.Flow.Run ?? throw new InvalidOperationException("試合がありません。");

        private bool Happened(FlowEvents events) => (_game.LastEvents & events) != 0;

        /// <summary>動かずに、低速で撃ち続ける操縦役。<c>bombOnce</c> なら最初の Tick にボムを使う。</summary>
        private sealed class StillPilot : IPilot
        {
            private bool _bomb;

            public StillPilot(bool bombOnce) => _bomb = bombOnce;

            public PlayerCommand CommandFor(VeilRun run)
            {
                var bomb = _bomb;
                _bomb = false;
                return new PlayerCommand(0, 0, slow: true, shoot: true, bomb: bomb);
            }
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
            while (true)
            {
                if (_game.Flow.Run != null && done()) { return; }
                if (_game.Flow.Screen != AppScreen.Playing) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
                _game.Flow.StepTick();
                _game.ConsumeEvents();
            }
        }

        private async Awaitable Shoot(CaptureContext capture, string name, string scene)
        {
            _game.Render();
            var run = _game.Flow.Run;
            var detail = run == null
                ? scene
                : $"{scene}。経過 {run.Elapsed} Tick、弾幕 {run.StageIndex + 1}、自機 ({run.Player.X}, {run.Player.Y})、残機 {run.Lives}、ボム {run.Bombs}、得点 {run.Score}、敵弾 {run.Bullets.Count}";
            await capture.ShootAsync(name, detail);
        }
    }
}
