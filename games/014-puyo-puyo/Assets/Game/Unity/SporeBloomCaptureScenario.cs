using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>証跡の台本。実時間と入力を使わず、Tick を手順どおりに進めて撮る。</summary>
    public sealed class SporeBloomCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int OpeningPairs = 8;
        private const int MaxSteps = 20000;

        private SporeBloomGame _game;

        private VersusFlow Flow => _game.Flow;
        private SporeConfig Rules => _game.Settings.Rules;
        private PlayerField Player => Flow.Match.Player;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<SporeBloomGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));
            var normalMatch = Flow.MatchFactory;

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル");

            Flow.Submit();
            capture.Record("seed", Flow.MatchSeed.ToString());
            await Shoot(capture, "02-start");

            for (var i = 0; i < OpeningPairs && Flow.Screen == AppScreen.Playing; i++) { PlayPair(); }
            Require(Flow.Screen == AppScreen.Playing, "序盤の進行");
            await Shoot(capture, "03-playing");

            Flow.TogglePause();
            Require(Flow.Screen == AppScreen.Paused, "一時停止");
            await Shoot(capture, "04-paused");

            Flow.MatchFactory = seed => DemoMatches.TwoChain(Rules, seed);
            Flow.Restart();
            Require(Flow.Perform(GameCommand.MoveLeft), "ペアの移動");
            Require(Flow.Perform(GameCommand.SoftDropOn), "ソフトドロップ");
            StepUntil(() => Player.Phase == FieldPhase.Popping && Player.Chain == 1, "1連鎖目");
            await Shoot(capture, "05-chain-1");

            StepUntil(() => Player.Phase == FieldPhase.Popping && Player.Chain == 2, "2連鎖目");
            await Shoot(capture, "06-chain-2");

            StepUntil(() => Player.Phase == FieldPhase.Falling, "連鎖の終了");
            Require(Player.MaxChain == 2 && Flow.Match.Cpu.PendingGarbage + CountAsh(Flow.Match.Cpu.Board) > 0, "攻撃の予告");
            await Shoot(capture, "07-attack");

            StepUntil(() => Flow.Match.Cpu.Phase == FieldPhase.GarbageFalling, "枯れ胞子の着弾");
            await Shoot(capture, "08-garbage");

            Flow.TogglePause();
            Flow.MatchFactory = seed => DemoMatches.CpuAboutToLose(Rules, seed);
            Flow.Restart();
            Flow.Match.Cpu.Apply(GameCommand.SoftDropOn);
            StepUntil(() => Flow.Screen == AppScreen.Result, "勝利");
            Require(Flow.Match.Outcome == MatchOutcome.PlayerWin, "勝利の結果");
            await Shoot(capture, "09-win");

            Flow.MatchFactory = seed => DemoMatches.PlayerAboutToLose(Rules, seed);
            Flow.Submit();
            Flow.Perform(GameCommand.SoftDropOn);
            StepUntil(() => Flow.Screen == AppScreen.Result, "敗北");
            Require(Flow.Match.Outcome == MatchOutcome.CpuWin, "敗北の結果");
            await Shoot(capture, "10-lose");

            Flow.MatchFactory = normalMatch;
            Flow.Submit();
            Require(Flow.Screen == AppScreen.Playing, "再戦");
            await Shoot(capture, "11-replay");
        }

        private static int CountAsh(Board board)
        {
            var count = 0;
            foreach (var cell in board.ToCells())
            {
                if (cell == Cell.Ash) { count++; }
            }

            return count;
        }

        private static void Require(bool condition, string scene)
        {
            if (!condition) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
        }

        private void PlayPair()
        {
            StepUntil(() => Flow.Screen != AppScreen.Playing || Player.Phase == FieldPhase.Falling, "ペアの出現");
            if (Flow.Screen != AppScreen.Playing) { return; }
            var pair = Player.PairsSpawned;
            foreach (var command in PairPlanner.Plan(Rules, Player)) { Flow.Perform(command); }
            StepUntil(() => Flow.Screen != AppScreen.Playing || Player.PairsSpawned != pair, "ペアの確定");
        }

        private void StepUntil(Func<bool> condition, string scene)
        {
            for (var i = 0; i < MaxSteps && !condition(); i++) { Flow.StepTick(); }
            Require(condition(), scene);
        }

        private async Awaitable Shoot(CaptureContext capture, string name)
        {
            _game.Render();
            var match = Flow.Match;
            await capture.ShootAsync(name, $"Tick {match.Player.TickCount}、あなた {match.Player.Score} 点、CPU {match.Cpu.Score} 点、最大連鎖 {match.Player.MaxChain}");
        }
    }
}
