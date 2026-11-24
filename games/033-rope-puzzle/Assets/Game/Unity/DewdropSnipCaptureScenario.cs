using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using IntGeometry;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡の台本。実時間と入力を使わず、意図と Tick を順に渡して場面を作って撮る。乱数は使わない。
    /// 意図の時刻はすべて 0 にする（時計で Tick を進めず、台本の StepTick だけで進める）。
    /// 1 問目はマウスのなぞりの意図を渡して切り、以後は解答の手順の操縦役（<see cref="Snipper"/>）で解く。
    /// 失敗の場面（落下・トゲ）は、なぞりの意図でわざと切って作り、やり直してから操縦役で解く。
    /// </summary>
    public sealed class DewdropSnipCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int TickLimit = 60 * 60;
        private const double ClockTime = 0;

        private DewdropSnipGame _game;
        private Snipper _snipper;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<DewdropSnipGame>();
            _snipper = new Snipper(CutPlan.Parse(_game.Settings.Solutions.text));
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings.Rules));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（遊び方の説明）");

            Key(IntentKind.Submit);
            await Shoot(capture, "02-select", "問題選択：最初は 1 問目だけを選べる");

            Key(IntentKind.Submit);
            Step(30);
            await Shoot(capture, "03-hanging", "1 問目：ロープで吊った露玉が下の花の器の上で揺れが収まる");

            // マウスのなぞり：左から近づけて、ロープの手前で止めて撮る。次の移動でロープを横切って切る。
            Pointer(IntentKind.PointerDown, Board(112, 116));
            Pointer(IntentKind.PointerMove, Board(118, 118));
            Pointer(IntentKind.PointerMove, Board(124, 120));
            await Shoot(capture, "04-swiping", "なぞりの途中（軌跡を描く）。まだロープを横切っていない");
            Pointer(IntentKind.PointerMove, Board(134, 122));
            Pointer(IntentKind.PointerUp, Board(134, 122));
            Step(4);
            await Shoot(capture, "05-cut", "なぞりがロープを横切って切れた：支点側の断片は垂れ、露玉側の断片は露玉から外れて落ちる");

            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome, "1 問目の成功");
            await Shoot(capture, "06-solved", "露玉が器に触れた Tick に成功（手数 1）");

            // 2 問目は操縦役で解く。
            Pilot(true);
            Key(IntentKind.Submit);
            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome, "2 問目の成功");

            // 3 問目：すぐに切ると真下へ落ちて失敗する。
            Pilot(false);
            Key(IntentKind.Submit);
            Step(1);
            SwipeAcross(0);
            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome, "3 問目の落下");
            await Shoot(capture, "07-fell", "3 問目：揺れの始めに切ると、器に届かず盤の下へ落ちて失敗");

            Pilot(true);
            Key(IntentKind.Retry);
            Step(40);
            await Shoot(capture, "08-swing", "やり直し：振り子の揺れ。器の方へ振れたところで切る（手順は 48 Tick）");
            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome, "3 問目の成功");

            // 4 問目：両方を一度に切ると真下のトゲに落ちる。
            Pilot(false);
            Key(IntentKind.Submit);
            Pointer(IntentKind.PointerDown, Board(60, 118));
            Pointer(IntentKind.PointerMove, Board(200, 118));
            Pointer(IntentKind.PointerUp, Board(200, 118));
            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome, "4 問目のトゲ");
            await Shoot(capture, "09-spike", "4 問目：1 回のなぞりで 2 本とも切ると（手数 2）、真下のトゲに触れて失敗");

            Pilot(true);
            Key(IntentKind.Retry);
            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome, "4 問目の成功");
            Key(IntentKind.Submit);
            StepUntil(() => Run.Moves == 1, "5 問目の切断");
            Step(36);
            await Shoot(capture, "10-ledge", "5 問目：棚に落ちた露玉が棚の上を滑って端へ向かう");
            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome, "5 問目の成功");

            for (var i = 5; i < 9; i++)
            {
                Key(IntentKind.Submit);
                StepUntil(() => _game.Flow.Screen == AppScreen.Outcome, $"{i + 1} 問目の成功");
            }

            Key(IntentKind.Submit);
            StepUntil(() => Run.Moves == 2, "10 問目の 2 本の切断");
            Step(70);
            await Shoot(capture, "11-final", "10 問目：2 本を切り、残る 1 本で振り上げる（手順では右の頂点の近くで切る）");
            StepUntil(() => _game.Flow.Screen == AppScreen.Outcome, "10 問目の成功");
            Key(IntentKind.Submit);
            await Shoot(capture, "12-result", "全 10 問を解いた結果（最少手数の合計）");
        }

        private PuzzleRun Run => _game.Flow.Run ?? throw new InvalidOperationException("試合がありません。");

        private static Vec2i Board(int x, int y) => new(x * Game.Core.Board.DataUnit, y * Game.Core.Board.DataUnit);

        private void Pilot(bool on)
        {
            _game.Flow.Pilot = on ? _snipper : null;
            if (on) { _snipper.Reset(_game.Flow.Cursor); }
        }

        /// <summary>ロープ <paramref name="rope"/> だけを横切るなぞりを、マウスの意図として渡す。</summary>
        private void SwipeAcross(int rope)
        {
            var swipe = CutPlan.SwipeAcross(Run, rope) ?? throw new InvalidOperationException($"ロープ {rope} を横切るなぞりが作れません。");
            Pointer(IntentKind.PointerDown, swipe.From);
            Pointer(IntentKind.PointerMove, swipe.To);
            Pointer(IntentKind.PointerUp, swipe.To);
        }

        private void Key(IntentKind kind)
        {
            _game.Apply(Intent.Of(kind, ClockTime));
            _game.ConsumeEvents();
        }

        private void Pointer(IntentKind kind, Vec2i point)
        {
            _game.Apply(Intent.Mouse(kind, point, ClockTime));
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
                : $"{scene}。第 {_game.Flow.Cursor + 1} 問「{run.Puzzle.Name}」、経過 {run.Ticks} Tick、手数 {run.Moves}、結果 {run.Status}/{run.Reason}、ハッシュ 0x{run.Hash():X8}";
            await capture.ShootAsync(name, detail);
        }
    }
}
