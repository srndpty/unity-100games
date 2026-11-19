using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>証跡の台本。実時間と入力を使わず、Tick と操作を手順どおりに進めて撮る。</summary>
    public sealed class PrismTideCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int OpeningMoves = 4;
        private const int MaxSteps = 20000;

        private PrismTideGame _game;

        private Match3Flow Flow => _game.Flow;
        private Match3Config Rules => _game.Settings.Rules;
        private Match3Game Game => Flow.Game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<PrismTideGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));
            var normalMatch = Flow.MatchFactory;

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル");

            Flow.Submit();
            capture.Record("seed", Flow.MatchSeed.ToString());
            await Shoot(capture, "02-start");

            var pilot = new Autopilot(Flow.MatchSeed);
            for (var i = 0; i < OpeningMoves; i++)
            {
                var swap = pilot.Choose(Game.Board);
                Click(swap.From);
                Click(swap.To);
                Require(Flow.LastVerdict == SwapVerdict.Legal, "序盤の入れ替え");
                StepUntil(() => Game.AcceptsSwap || Game.IsOver, "序盤の解決");
            }

            Require(Flow.Screen == AppScreen.Playing, "序盤の進行");

            // キーボードでカーソルを動かし、結晶を選んだところ。
            Flow.Move(-1, 0);
            Flow.Move(0, -1);
            Flow.ToggleSelect();
            Require(Flow.Input.Selected.HasValue, "選択");
            await Shoot(capture, "03-select");

            Flow.TogglePause();
            Flow.MatchFactory = seed => new Match3Game(Rules, seed, DemoBoards.TwoChain());
            Flow.Restart();
            Click(DemoBoards.NoMatchSwap.From);
            Click(DemoBoards.NoMatchSwap.To);
            Require(Flow.LastVerdict == SwapVerdict.NoMatch, "不成立の入れ替え");
            StepUntil(() => Game.PhaseRemaining <= Game.PhaseDuration * 3 / 4, "戻る途中");
            await Shoot(capture, "04-revert");

            StepUntil(() => Game.AcceptsSwap, "戻し終わり");
            Click(DemoBoards.TwoChainSwap.From);
            Click(DemoBoards.TwoChainSwap.To);
            Require(Flow.LastVerdict == SwapVerdict.Legal, "連鎖の入れ替え");
            StepUntil(() => Game.Phase == GamePhase.Falling && Game.PhaseRemaining <= Game.PhaseDuration / 2, "1段目の落下");
            await Shoot(capture, "05-falling");

            StepUntil(() => Game.Phase == GamePhase.Clearing && Game.Chain == 2 && Game.PhaseRemaining <= Game.PhaseDuration / 2, "2連鎖目");
            await Shoot(capture, "06-chain-2");

            Flow.TogglePause();
            Flow.MatchFactory = seed => new Match3Game(Rules, seed, DemoBoards.Cross());
            Flow.Restart();
            Click(DemoBoards.CrossSwap.From);
            Click(DemoBoards.CrossSwap.To);
            StepUntil(() => Game.Phase == GamePhase.Clearing && Game.PhaseRemaining <= Game.PhaseDuration / 2, "T字の消去");
            Require(Game.ClearingCells.Count == 5, "T字の5個");
            await Shoot(capture, "07-cross");

            Flow.TogglePause();
            Flow.MatchFactory = seed => new Match3Game(Rules, seed, DemoBoards.Dead());
            Flow.Restart();
            Require(Game.Phase == GamePhase.Reshuffling, "手詰まりの作り直し");
            StepUntil(() => Game.PhaseRemaining <= Game.PhaseDuration / 2, "作り直しの途中");
            await Shoot(capture, "08-reshuffle");

            Flow.TogglePause();
            Require(Flow.Screen == AppScreen.Paused, "一時停止");
            await Shoot(capture, "09-paused");

            var winRules = Rules.Copy();
            winRules.TargetScore = 60;
            Flow.MatchFactory = seed => new Match3Game(winRules, seed, DemoBoards.TwoChain());
            Flow.Restart();
            Click(DemoBoards.TwoChainSwap.From);
            Click(DemoBoards.TwoChainSwap.To);
            StepUntil(() => Flow.Screen == AppScreen.Result, "クリア");
            Require(Game.Phase == GamePhase.Won, "クリアの結果");
            await Shoot(capture, "10-win");

            var loseRules = Rules.Copy();
            loseRules.Moves = 1;
            Flow.MatchFactory = seed => new Match3Game(loseRules, seed, DemoBoards.Cross());
            Flow.Submit();
            Click(DemoBoards.CrossSwap.From);
            Click(DemoBoards.CrossSwap.To);
            StepUntil(() => Flow.Screen == AppScreen.Result, "手数切れ");
            Require(Game.Phase == GamePhase.Lost, "手数切れの結果");
            await Shoot(capture, "11-lose");

            Flow.MatchFactory = normalMatch;
            Flow.Submit();
            Require(Flow.Screen == AppScreen.Playing, "再戦");
            await Shoot(capture, "12-replay");
        }

        private static void Require(bool condition, string scene)
        {
            if (!condition) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
        }

        // マスの中心をクリックした操作として Flow へ渡す。
        private void Click(Point cell)
        {
            Flow.PointerDown(cell.X + .5, cell.Y + .5);
            Flow.PointerUp();
        }

        private void StepUntil(Func<bool> condition, string scene)
        {
            for (var i = 0; i < MaxSteps && !condition(); i++) { Flow.StepTick(); }
            Require(condition(), scene);
        }

        private async Awaitable Shoot(CaptureContext capture, string name)
        {
            _game.Render();
            await capture.ShootAsync(name, $"Tick {Game.TickCount}、{Game.Phase}、スコア {Game.Score}、のこり {Game.MovesLeft} 手、最大連鎖 {Game.MaxChain}");
        }
    }
}
