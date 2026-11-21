using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>証跡の台本。実時間と入力を使わず、意図を順に渡して場面を作って撮る。</summary>
    public sealed class FireflyHushCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private FireflyHushGame _game;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            _game = GetComponent<FireflyHushGame>();
            await capture.WaitUntilAsync(() => _game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(_game.Settings));

            _game.Render();
            await capture.ShootAsync("01-title", "タイトル（飾りの灯、試合はまだない）");

            Apply(Intent.Of(IntentKind.Submit));
            await Shoot(capture, "02-start", "出題直後。カーソルは中央");

            // キーボードの経路：カーソルを最短解の最初のマスへ動かして押す。
            MoveCursorTo(Autopilot.NextPress(Game));
            Apply(Intent.Of(IntentKind.PressCursor));
            Apply(Intent.PressCell(Autopilot.NextPress(Game)));
            await Shoot(capture, "03-playing", "最短解の 2 マスを押した途中");

            Apply(Intent.Of(IntentKind.TogglePause));
            await Shoot(capture, "04-paused", "一時停止のメニュー");

            Apply(Intent.Of(IntentKind.GiveUp));
            await Shoot(capture, "05-gave-up", "あきらめた結果。印は今の盤の最短解");

            Apply(Intent.Of(IntentKind.Submit));
            for (var guard = 0; guard < LightsBoard.CellCount && _game.Flow.Screen == AppScreen.Playing; guard++)
            {
                Apply(Intent.PressCell(Autopilot.NextPress(Game)));
            }

            Require(Game.Status == GameStatus.Cleared, "クリア");
            await Shoot(capture, "06-cleared", "次の問題を最短解で消した結果");

            Apply(Intent.Of(IntentKind.Submit));
            await Shoot(capture, "07-next", "結果から次の問題（新しいシード）");
        }

        private LightsOutGame Game => _game.Flow.Game ?? throw new InvalidOperationException("試合がありません。");

        private void Apply(Intent intent) => _game.Flow.Apply(intent);

        private void MoveCursorTo(int cell)
        {
            var cursor = _game.Flow.Cursor;
            var targetX = cell % LightsBoard.Size;
            var targetY = cell / LightsBoard.Size;
            while (cursor.X < targetX) { Apply(Intent.Of(IntentKind.CursorRight)); }
            while (cursor.X > targetX) { Apply(Intent.Of(IntentKind.CursorLeft)); }
            while (cursor.Y < targetY) { Apply(Intent.Of(IntentKind.CursorDown)); }
            while (cursor.Y > targetY) { Apply(Intent.Of(IntentKind.CursorUp)); }
        }

        private async Awaitable Shoot(CaptureContext capture, string name, string scene)
        {
            _game.Render();
            var game = _game.Flow.Game;
            var detail = game == null ? scene : $"{scene}。シード {game.Seed}、手数 {game.Moves}、最短 {game.OptimalMoves}、点灯 {game.Board.LitCount}";
            await capture.ShootAsync(name, detail);
        }

        private static void Require(bool condition, string scene)
        {
            if (!condition) { throw new InvalidOperationException("撮影場面を作れませんでした: " + scene); }
        }
    }
}
