using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    public sealed class InvadersCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = GetComponent<InvadersGame>();
            await capture.WaitUntilAsync(() => game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(game.Settings.Game));
            game.Render(); await capture.ShootAsync("01-title", "Tick 0");
            game.Flow.Submit();
            var combatShot = false; var waveShot = false;
            for (var tick = 0; tick < 60000 && game.Flow.Screen == AppScreen.Playing; tick++)
            {
                game.Step(Autopilot.Next(game.Flow.Game, game.Settings.Game));
                if (!combatShot && game.Flow.Game.TickCount >= 600)
                {
                    game.Render(); await capture.ShootAsync("02-combat", "Tick " + game.Flow.Game.TickCount); combatShot = true;
                }
                if (!waveShot && game.Flow.Game.Phase == GamePhase.WaveTransition)
                {
                    game.Render(); await capture.ShootAsync("03-wave-clear", "Tick " + game.Flow.Game.TickCount); waveShot = true;
                }
                if (tick % 1000 == 0) { await Awaitable.NextFrameAsync(); }
            }
            if (game.Flow.Game.Phase != GamePhase.Won || !waveShot) { throw new InvalidOperationException("3Waveの勝利へ到達しませんでした。"); }
            game.Render(); await capture.ShootAsync("04-win", "Tick " + game.Flow.Game.TickCount);
            game.Flow.Submit();
            capture.Record("replaySeed", game.Flow.GameSeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (var tick = 0; tick < 60000 && game.Flow.Screen == AppScreen.Playing; tick++)
            {
                game.Step(default);
                if (tick % 1000 == 0) { await Awaitable.NextFrameAsync(); }
            }
            if (game.Flow.Game.Phase != GamePhase.Lost) { throw new InvalidOperationException("敗北へ到達しませんでした。"); }
            game.Render(); await capture.ShootAsync("05-loss", "Tick " + game.Flow.Game.TickCount);
        }
    }
}
