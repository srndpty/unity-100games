using System;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>証跡の台本。実時間と入力を使わず、手順どおりに場面を作って撮る。</summary>
    public sealed class TwinForgeCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int MidgameMoves = 30;

        private static readonly Direction[] _preference = { Direction.Left, Direction.Down, Direction.Right, Direction.Up };

        private static readonly BoardState _mergeBoard = BoardState.FromValues(
            2, 2, 2, 2,
            4, 4, 8, 8,
            16, 16, 0, 0,
            32, 0, 0, 32);

        private static readonly BoardState _winBoard = BoardState.FromValues(
            1024, 1024, 8, 16,
            512, 256, 128, 64,
            4, 8, 16, 32,
            2, 4, 8, 16);

        private static readonly BoardState _loseBoard = BoardState.FromValues(
            2, 2, 8, 16,
            8, 16, 32, 64,
            4, 8, 16, 32,
            8, 16, 32, 64);

        private TwinForgeGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<TwinForgeGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル");

            _game.Flow.Submit();
            await Shoot(capture, "02-start");

            for (var i = 0; i < MidgameMoves; i++) { MoveAny(); }
            await Shoot(capture, "03-midgame");

            _game.Flow.StartFrom(_mergeBoard);
            Require(_game.Flow.Move(Direction.Left) != MoveResult.Rejected, "合体");
            await Shoot(capture, "04-merge");

            _game.Flow.StartFrom(_winBoard);
            Require(_game.Flow.Move(Direction.Left) == MoveResult.Won, "勝利");
            await Shoot(capture, "05-won");

            _game.Flow.StartFrom(_loseBoard);
            Require(_game.Flow.Move(Direction.Left) == MoveResult.Lost, "敗北");
            await Shoot(capture, "06-lost");

            _game.Flow.Submit();
            MoveAny();
            await Shoot(capture, "07-replay");
        }

        private ForgeGame Game => _game.Flow.Game;

        private void MoveAny()
        {
            foreach (var direction in _preference)
            {
                if (_game.Flow.Move(direction) != MoveResult.Rejected) { return; }
            }

            throw new InvalidOperationException("撮影場面を作れませんでした: 動かせる手がありません");
        }

        private async Awaitable Shoot(CaptureContext capture, string name)
        {
            _game.Render();
            await capture.ShootAsync(name, $"シード {Game.Seed}、スコア {Game.Score}、手数 {Game.MoveCount}");
        }

        private static void Require(bool condition, string scene)
        {
            if (!condition) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
        }
    }
}
