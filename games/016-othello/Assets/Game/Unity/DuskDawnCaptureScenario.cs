using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>証跡の台本。実時間と入力を使わず、Tick と操作を手順どおりに進めて撮る。</summary>
    public sealed class DuskDawnCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int OpeningMoves = 5;
        private const int PlayerDepth = 2;
        private const int MaxSteps = 20000;

        private DuskDawnGame _game;

        private OthelloFlow Flow => _game.Flow;
        private OthelloGame Game => Flow.Game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<DuskDawnGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));
            var normalGame = Flow.GameFactory;

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（普通・黒）");

            Flow.Submit();
            capture.Record("seed", Flow.MatchSeed.ToString());
            await Shoot(capture, "02-start");

            // プレイヤー側は浅い探索で打つ。乱数は試合のシードから作るので毎回同じ手になる。
            var player = new SeededRandom(Flow.MatchSeed + 1);
            for (var i = 0; i < OpeningMoves; i++)
            {
                StepUntil(() => Flow.IsPlayerTurn, "プレイヤーの手番");
                Click(Searcher.Search(Game.Position, PlayerDepth, player).Move);
                Require(Flow.LastVerdict == PlayVerdict.Ok, "序盤の着手");
            }

            StepUntil(() => Flow.IsPlayerTurn, "序盤の後の手番");
            Flow.MoveCursor(1, 0);
            Flow.MoveCursor(0, -1);
            Require(Flow.Cursor.IsActive, "カーソル");
            await Shoot(capture, "03-hints-cursor");

            var move = Searcher.Search(Game.Position, PlayerDepth, player).Move;
            Click(move);
            Require(Bits.PopCount(Game.LastFlips) >= 1, "返す石");
            StepUntil(() => Game.Phase == GamePhase.Placing && Game.PhaseRemaining <= Game.PhaseDuration * 2 / 3, "返す途中");
            await Shoot(capture, "04-flipping");

            StepUntil(() => Flow.IsCpuTurn && Flow.Cpu.PlannedMove.HasValue, "CPU の考える間");
            await Shoot(capture, "05-cpu-thinking");

            Flow.TogglePause();
            Flow.GameFactory = _ => new OthelloGame(Flow.Config, DemoGames.PassSoon());
            Flow.Restart();
            Click(DemoGames.PassMove);
            StepUntil(() => Game.Phase == GamePhase.Passing && Game.PhaseRemaining <= Game.PhaseDuration / 2, "パス");
            await Shoot(capture, "06-pass");

            Flow.TogglePause();
            Require(Flow.Screen == AppScreen.Paused, "一時停止");
            await Shoot(capture, "07-paused");

            Flow.GameFactory = _ => new OthelloGame(Flow.Config, DemoGames.Endgame());
            Flow.Restart();
            PlayToTheEnd(player);
            Require(Flow.Outcome == Outcome.Win, "勝ち");
            await Shoot(capture, "08-win");

            Flow.SelectSide(Side.White);
            Flow.SelectLevel(CpuLevel.Strong);
            Flow.Submit();
            PlayToTheEnd(player);
            Require(Flow.Outcome == Outcome.Loss, "負け");
            await Shoot(capture, "09-loss");

            Flow.GameFactory = normalGame;
            Flow.Submit();
            Require(Flow.Screen == AppScreen.Playing && Flow.MatchPlayerSide == Side.White, "白で再戦");
            StepUntil(() => Flow.IsPlayerTurn, "CPU の初手");
            await Shoot(capture, "10-replay-white");
        }

        private static void Require(bool condition, string scene)
        {
            if (!condition) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
        }

        // マスの中心をクリックした操作として Flow へ渡す。
        private void Click(Square square) => Flow.PointerDown(square.X + .5, square.Y + .5);

        private void PlayToTheEnd(SeededRandom player)
        {
            for (var i = 0; i < MaxSteps && Flow.Screen == AppScreen.Playing; i++)
            {
                if (Flow.IsPlayerTurn) { Click(Searcher.Search(Game.Position, PlayerDepth, player).Move); }
                else { Flow.StepTick(); }
            }

            Require(Flow.Screen == AppScreen.Result, "終局");
        }

        private void StepUntil(Func<bool> condition, string scene)
        {
            for (var i = 0; i < MaxSteps && !condition(); i++) { Flow.StepTick(); }
            Require(condition(), scene);
        }

        private async Awaitable Shoot(CaptureContext capture, string name)
        {
            _game.Render();
            await capture.ShootAsync(name, $"Tick {Game.TickCount}、{Game.Phase}、黒 {Game.Count(Side.Black)} 白 {Game.Count(Side.White)}、手数 {Game.MovesPlayed}、パス {Game.Passes}");
        }
    }
}
