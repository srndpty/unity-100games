using System;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>証跡の台本。実時間と入力を使わず、手順どおりに場面を作って撮る。</summary>
    public sealed class HarborShiftCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const int FirstLevel = 0;
        private const int ThirdLevel = 2;
        private const int LastLevel = 9;
        private const string FirstSolution = "RR";
        private const string ThirdOpening = "UURRD";
        private const string LastSolution = "RUULUURDRRRLDRRRRDRRUULDUULLLDLDDLDLLUULURRRRDRRRDDRRULRUULDUULLLDLDRRRLLLDLDRRRR";

        private HarborShiftGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<HarborShiftGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));
            capture.Record("random", "このゲームは乱数を使わない");

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル");

            var flow = _game.Flow;
            flow.Submit();
            flow.PressDirection(Direction.Right);
            flow.ReleaseDirection(Direction.Right);
            flow.PressDirection(Direction.Right);
            flow.ReleaseDirection(Direction.Right);
            Require(flow.SelectedLevel == ThirdLevel, "面選択");
            await Shoot(capture, "02-select");

            flow.Submit();
            await Shoot(capture, "03-start");

            Play(ThirdOpening);
            Require(flow.Game.PushCount > 0, "箱押し");
            await Shoot(capture, "04-push");

            Require(flow.Undo(), "取り消し");
            await Shoot(capture, "05-undo");

            Require(flow.Restart(), "やり直し");
            Require(flow.Game.Board.Equals(flow.Game.Map.Initial), "やり直し後の盤面");
            await Shoot(capture, "06-restart");

            flow.Back();
            Require(flow.SelectLevel(FirstLevel), "第1問の開始");
            Play(FirstSolution);
            Require(flow.Game.IsCleared, "クリア");
            await Shoot(capture, "07-cleared");

            // 全問クリアの場面を作るため、途中の問題の記録だけを用意し、最終問は実際に解く。
            for (var level = FirstLevel + 1; level < LastLevel; level++) { flow.Records.Record(level, 1); }
            flow.Back();
            Require(flow.SelectLevel(LastLevel), "最終問の開始");
            Play(LastSolution);
            Require(flow.Records.IsAllCleared, "全問クリア");
            await Shoot(capture, "08-all-clear");

            Require(flow.Restart(), "再プレイ");
            await Shoot(capture, "09-replay");
        }

        private void Play(string moves)
        {
            foreach (var symbol in moves)
            {
                Require(_game.Flow.Move(ToDirection(symbol)) != MoveResult.Rejected, "手順の再生");
            }
        }

        private static Direction ToDirection(char symbol) => symbol switch
        {
            'U' => Direction.Up,
            'D' => Direction.Down,
            'L' => Direction.Left,
            'R' => Direction.Right,
            _ => throw new ArgumentOutOfRangeException(nameof(symbol), symbol, "不正な手順です。"),
        };

        private async Awaitable Shoot(CaptureContext capture, string name)
        {
            _game.Render();
            var game = _game.Flow.Game;
            await capture.ShootAsync(name, $"第{_game.Flow.SelectedLevel + 1}問、手数 {game?.MoveCount ?? 0}、押した数 {game?.PushCount ?? 0}");
        }

        private static void Require(bool condition, string scene)
        {
            if (!condition) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
        }
    }
}
