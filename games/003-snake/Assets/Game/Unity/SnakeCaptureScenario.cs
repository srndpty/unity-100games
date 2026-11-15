using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    public sealed class SnakeCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = GetComponent<SnakeGame>();
            await capture.WaitUntilAsync(() => game.HudRoot != null, 10);
            capture.Record("rules", "fixed speed, 2 buffered turns, full board wins");
            capture.Record("settings", JsonUtility.ToJson(game.Settings.Game));
            game.Render();
            await capture.ShootAsync("01-title", "Title / Tick 0");
            game.Flow.Submit();
            var ticks = 0;
            while (game.Flow.Game.Score < 20 && game.Flow.Screen == AppScreen.Playing && ticks++ < 110000)
            {
                game.Flow.RequestDirection(Autopilot.Next(game.Flow.Game));
                game.Flow.StepOneTick();
                if (ticks % 2000 == 0) { await Awaitable.NextFrameAsync(); }
            }
            if (game.Flow.Game.Score < 20) { throw new InvalidOperationException("20個の成長に到達しませんでした。"); }
            game.Render();
            await capture.ShootAsync("02-growth", "Tick " + game.Flow.Game.TickCount);
            game.Flow.TogglePause(); game.Render();
            await capture.ShootAsync("03-paused", "Tick " + game.Flow.Game.TickCount);
            game.Flow.TogglePause();
            while (game.Flow.Screen == AppScreen.Playing && ticks++ < 110000)
            {
                game.Flow.RequestDirection(Autopilot.Next(game.Flow.Game));
                game.Flow.StepOneTick();
                if (ticks % 2000 == 0) { await Awaitable.NextFrameAsync(); }
            }
            if (game.Flow.Game.Phase != GamePhase.Won) { throw new InvalidOperationException("クリアに到達しませんでした。"); }
            game.Render();
            await capture.ShootAsync("04-win", "Tick " + game.Flow.Game.TickCount);
            game.Flow.Submit();
            while (game.Flow.Screen == AppScreen.Playing) { game.Flow.StepOneTick(); }
            game.Render();
            capture.Record("replaySeed", game.Flow.GameSeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            await capture.ShootAsync("05-loss", "Tick " + game.Flow.Game.TickCount);
        }
    }
}
