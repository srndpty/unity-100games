using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>証跡の台本。実時間と入力を使わず、Tick を手順どおりに進めて撮る。</summary>
    public sealed class StrataDropCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int OpeningPieces = 14;
        private const int MaxPieces = 400;
        private const int FallTicks = 40;
        private const int ClearingTicks = 4;

        private StrataDropGame _game;

        private StrataFlow Flow => _game.Flow;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<StrataDropGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル");

            Flow.Submit();
            capture.Record("seed", Flow.GameSeed.ToString());
            await Shoot(capture, "02-start");

            for (var i = 0; i < OpeningPieces; i++) { PlayPiece(); }
            FinishClearing();
            Steps(FallTicks);
            await Shoot(capture, "03-playing");

            Require(Flow.Perform(GameCommand.Hold), "ホールド");
            await Shoot(capture, "04-hold");

            PlayUntilClearing();
            Steps(ClearingTicks);
            Require(Flow.Game.Phase == GamePhase.Clearing, "ライン消去の演出");
            await Shoot(capture, "05-clearing");

            FinishClearing();
            Require(Flow.Game.Lines > 0, "ライン消去後");
            await Shoot(capture, "06-cleared");

            Flow.TogglePause();
            Require(Flow.Screen == AppScreen.Paused, "一時停止");
            await Shoot(capture, "07-paused");

            Flow.Submit();
            PlayUntilResult();
            await Shoot(capture, "08-result");

            Flow.Submit();
            Require(Flow.Screen == AppScreen.Playing, "再戦");
            await Shoot(capture, "09-replay");
        }

        private void PlayPiece()
        {
            foreach (var command in Autopilot.PlanPiece(Flow.Game)) { Flow.Perform(command); }
        }

        private void PlayUntilClearing()
        {
            FinishClearing();
            for (var i = 0; i < MaxPieces && Flow.Game.Phase != GamePhase.Clearing; i++) { PlayPiece(); }
            Require(Flow.Game.Phase == GamePhase.Clearing, "ライン消去");
        }

        private void FinishClearing()
        {
            while (Flow.Screen == AppScreen.Playing && Flow.Game.Phase == GamePhase.Clearing) { Flow.StepTick(); }
        }

        // 何も考えずに中央へ積み、出現位置を塞いで負ける。
        private void PlayUntilResult()
        {
            for (var i = 0; i < MaxPieces && Flow.Screen == AppScreen.Playing; i++)
            {
                FinishClearing();
                Flow.Perform(GameCommand.HardDrop);
            }

            Require(Flow.Screen == AppScreen.Result, "ゲームオーバー");
        }

        private void Steps(int count)
        {
            for (var i = 0; i < count; i++) { Flow.StepTick(); }
        }

        private async Awaitable Shoot(CaptureContext capture, string name)
        {
            _game.Render();
            var game = Flow.Game;
            await capture.ShootAsync(name, $"Tick {game.TickCount}、スコア {game.Score}、ライン {game.Lines}、レベル {game.Level}");
        }

        private static void Require(bool condition, string scene)
        {
            if (!condition) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
        }
    }
}
