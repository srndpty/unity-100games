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
    /// 1 面目はマウスの意図で引いて撃ち、以後は解答の手順の操縦役（<see cref="PlannedPilot"/>）で解く。
    /// 負けの場面は 5 面目でわざと外して作り、やり直してから操縦役で解く。
    /// </summary>
    public sealed class PebbleSiegeCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int TickLimit = 60 * 120;
        private const double ClockTime = 0;

        private PebbleSiegeGame _game;
        private PlannedPilot _pilot;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<PebbleSiegeGame>();
            var solutions = ShotPlan.Parse(_game.Settings.Solutions.text);
            _pilot = new PlannedPilot(solutions);
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings.Rules));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（遊び方の説明）。背景は 1 面目の初期配置");

            Key(IntentKind.Submit);
            await Shoot(capture, "02-select", "面選択：最初は 1 面目だけを選べる");

            // 1 面目：発射台を押して引く（解答と同じ引き）。構えている間は弾道の点を描く。
            Key(IntentKind.Submit);
            Step(20);
            var launcher = Run.Stage.Launcher;
            var pull = solutions[0][0].Pull;
            Pointer(IntentKind.PointerDown, launcher);
            Pointer(IntentKind.PointerMove, launcher + (pull * .5f));
            Pointer(IntentKind.PointerMove, launcher + pull);
            await Shoot(capture, "03-aim", "構え：引いた逆向きに飛ぶ。点は最初の 48 Tick の弾道（Physics2D と同じ半陰的オイラー）");

            Pointer(IntentKind.PointerUp, launcher + pull);
            Step(18);
            await Shoot(capture, "04-flight", "撃った直後：弾は点の道すじどおりに飛ぶ");
            StepUntil(() => (_game.LastEvents & FlowEvents.Broke) != 0, "1 面目の命中");
            Step(2);
            await Shoot(capture, "05-hit", "命中：灯籠が壊れて土ぼこりが出る（得点は 1 回だけ）");
            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome, "1 面目の勝ち");
            await Shoot(capture, "06-won", "全部止まってから勝ちが決まる。残った弾で星");

            // 2・3 面目は操縦役で解く。3 面目は命中の後の崩れを撮る。
            Pilot(true);
            Key(IntentKind.Submit);
            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome, "2 面目の勝ち");
            Key(IntentKind.Submit);
            StepUntil(() => Run.BrokenCount > 0, "3 面目の命中");
            Step(24);
            await Shoot(capture, "07-collapse", "3 面目：重い弾の命中で氷の塔が 2 段とも崩れる");
            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome, "3 面目の勝ち");

            // 4 面目：爆発の弾。起爆の瞬間を撮る。
            Key(IntentKind.Submit);
            StepUntil(() => (_game.LastEvents & FlowEvents.Detonated) != 0, "4 面目の起爆");
            Step(3);
            await Shoot(capture, "08-blast", "4 面目：爆発の弾を起爆。爆風は囲いの向こうの灯籠にも届く");
            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome, "4 面目の勝ち");

            // 5 面目：わざと真上へ弱く撃って 3 発とも外し、負けを撮る。
            Pilot(false);
            Key(IntentKind.Submit);
            for (var shot = 0; shot < Run.Stage.Shots.Count; shot++)
            {
                StepUntil(() => Run.Phase == RunPhase.Aiming && Run.QuietTicks >= PlannedPilot.AimDelay, "5 面目の構え");
                Pointer(IntentKind.PointerDown, Run.Stage.Launcher);
                Pointer(IntentKind.PointerUp, Run.Stage.Launcher + new V2(-.3f, -.8f));
                Step(1);
            }

            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome, "5 面目の負け");
            await Shoot(capture, "09-lost", "5 面目：弾を使い切り、止まっても灯籠が残ったので負け");

            Pilot(true);
            Key(IntentKind.Retry);
            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome, "5 面目の勝ち");
            await Shoot(capture, "10-fort", "やり直して勝ち：砦の 3 つの灯籠を全部落とした");
            Key(IntentKind.Submit);
            await Shoot(capture, "11-result", "全 5 面の結果（最高点の合計と星）");
        }

        private SiegeRun Run => _game.Flow.Run ?? throw new InvalidOperationException("試合がありません。");

        private void Pilot(bool on)
        {
            _game.Flow.Pilot = on ? _pilot : null;
            if (on) { _pilot.Reset(_game.Flow.Cursor); }
        }

        private void Key(IntentKind kind)
        {
            _game.Flow.Apply(Intent.Of(kind, ClockTime));
            _game.ConsumeEvents();
        }

        private void Pointer(IntentKind kind, V2 point)
        {
            _game.Flow.Apply(Intent.Mouse(kind, point, ClockTime));
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
                if (done()) { return; }
                if (i > TickLimit || _game.Flow.Screen != AppScreen.Playing)
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
            var run = _game.Flow.Run;
            var detail = run == null
                ? scene
                : $"{scene}。第 {_game.Flow.Cursor + 1} 面「{run.Stage.Name}」、経過 {run.Ticks} Tick、弾 {run.ShotsUsed}/{run.Stage.Shots.Count}、得点 {run.Score}、結果 {run.Phase}、ハッシュ 0x{run.Hash():X8}";
            await capture.ShootAsync(name, detail);
        }
    }
}
