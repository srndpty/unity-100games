using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    public sealed class AsteroidsCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = GetComponent<AsteroidsGame>();
            await capture.WaitUntilAsync(() => game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(game.Settings.Game));
            game.Render(); await capture.ShootAsync("01-title", "Tick 0");
            game.Flow.Submit(); var combat = false; var split = false; var wave = false;
            for (var tick = 0; tick < 60000 && game.Flow.Screen == AppScreen.Playing; tick++)
            {
                game.Step(Autopilot.Next(game.Flow.Game, game.Settings.Game));
                if (!combat && game.Flow.Game.TickCount >= 1 && game.Flow.Game.Phase == GamePhase.Playing)
                { game.Render(); await capture.ShootAsync("02-combat", "Tick " + game.Flow.Game.TickCount); combat = true; }
                if (!split && FragmentsSeparated(game.Flow.Game) && game.Flow.Game.Phase == GamePhase.Playing)
                { game.Render(); await capture.ShootAsync("03-split", "Tick " + game.Flow.Game.TickCount); split = true; }
                if (!wave && game.Flow.Game.Phase == GamePhase.WaveTransition)
                { game.Render(); await capture.ShootAsync("04-wave-clear", "Tick " + game.Flow.Game.TickCount); wave = true; }
                if (tick % 1000 == 0) { await Awaitable.NextFrameAsync(); }
            }
            if (game.Flow.Game.Phase != GamePhase.Won || !combat || !split || !wave) { throw new InvalidOperationException("撮影に必要な交戦・分裂・Wave・勝利へ到達しませんでした。"); }
            game.Render(); await capture.ShootAsync("05-win", "Tick " + game.Flow.Game.TickCount);
            game.Flow.Submit(); capture.Record("replaySeed", game.Flow.GameSeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (var tick = 0; tick < 60000 && game.Flow.Screen == AppScreen.Playing; tick++)
            {
                game.Step(default);
                if (tick % 1000 == 0) { await Awaitable.NextFrameAsync(); }
            }
            if (game.Flow.Game.Phase != GamePhase.Lost) { throw new InvalidOperationException("敗北へ到達しませんでした。"); }
            game.Render(); await capture.ShootAsync("06-loss", "Tick " + game.Flow.Game.TickCount);
        }
        private static bool FragmentsSeparated(AsteroidsState state)
        {
            for (var i = 0; i < state.AsteroidCount; i++)
            {
                if (state.AsteroidAt(i).Size == 2) { continue; }
                for (var j = 0; j < i; j++)
                {
                    if (state.AsteroidAt(j).Size < 2 && Torus.Difference(state.AsteroidAt(i).Position, state.AsteroidAt(j).Position).Length > .35f) { return true; }
                }
            }
            return false;
        }
    }
}
