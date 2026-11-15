using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    public sealed class FlappyCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = GetComponent<FlappyGame>();
            await capture.WaitUntilAsync(() => game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(game.Settings));
            game.Render(); await capture.ShootAsync("01-title", "Tick 0");
            game.Flow.Submit(); game.Render(); await capture.ShootAsync("02-ready", "Tick 0");
            game.Step(new FlappyCommand(true));
            for (var tick = 0; tick < 90; tick++) { game.Step(Autopilot.Next(game.Flow.Game, game.Settings.Game)); }
            game.Render(); await capture.ShootAsync("03-flight", "Tick " + game.Flow.Game.TickCount);
            for (var tick = 0; tick < 15000 && game.Flow.Game.Score < 25 && game.Flow.Screen == AppScreen.Playing; tick++)
            {
                game.Step(Autopilot.Next(game.Flow.Game, game.Settings.Game));
                if (tick % 1000 == 0) { await Awaitable.NextFrameAsync(); }
            }
            if (game.Flow.Game.Score < 25 || game.Flow.Game.Lost) { throw new InvalidOperationException("最大難易度まで到達できませんでした。"); }
            game.Render(); await capture.ShootAsync("04-level-cap", "Tick " + game.Flow.Game.TickCount);
            for (var tick = 0; tick < 300 && game.Flow.Screen == AppScreen.Playing; tick++) { game.Step(default); }
            if (game.Flow.Screen != AppScreen.Result) { throw new InvalidOperationException("結果画面へ到達できませんでした。"); }
            game.Render(); await capture.ShootAsync("05-result", "Tick " + game.Flow.Game.TickCount);
            game.Flow.Submit(); capture.Record("replaySeed", game.Flow.GameSeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            game.Render(); await capture.ShootAsync("06-replay", "Tick 0");
        }
    }
}
