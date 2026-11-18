using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    public sealed class FroggerCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = GetComponent<FroggerGame>();
            await capture.WaitUntilAsync(() => game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(game.Settings));
            game.Render(); await capture.ShootAsync("01-title", "Tick 0");
            game.Flow.Submit();
            for (var i = 0; i < 20; i++) { game.Step(Autopilot.Next(game.Flow.Game)); }
            game.Render(); await capture.ShootAsync("02-road", "Tick " + game.Flow.Game.TickCount);
            for (var i = 0; i < 20; i++) { game.Step(Autopilot.Next(game.Flow.Game)); }
            game.Render(); await capture.ShootAsync("03-log", "Tick " + game.Flow.Game.TickCount);
            for (var i = 0; i < 80 && game.Flow.Screen == AppScreen.Playing; i++) { game.Step(Autopilot.Next(game.Flow.Game)); }
            Require(game, RoundResult.Won); game.Render(); await capture.ShootAsync("04-clear", "Tick " + game.Flow.Game.TickCount);
            game.Flow.Submit(); game.Step(MoveDirection.Up);
            for (var i = 0; i < 300 && game.Flow.Screen == AppScreen.Playing; i++) { game.Step(); }
            Require(game, RoundResult.CarHit); game.Render(); await capture.ShootAsync("05-car-hit", "Tick " + game.Flow.Game.TickCount);
            game.Flow.Submit();
            for (var i = 0; i < 4; i++) { game.Step(MoveDirection.Up); }
            game.Step(MoveDirection.Right); game.Step(MoveDirection.Right); game.Step(MoveDirection.Up);
            Require(game, RoundResult.Drowned); game.Render(); await capture.ShootAsync("06-water", "Tick " + game.Flow.Game.TickCount);
            game.Flow.Submit();
            for (var i = 0; i < 5; i++) { game.Step(MoveDirection.Up); }
            for (var i = 0; i < 700 && game.Flow.Screen == AppScreen.Playing; i++) { game.Step(); }
            Require(game, RoundResult.CarriedOut); game.Render(); await capture.ShootAsync("07-carried-out", "Tick " + game.Flow.Game.TickCount);
            game.Flow.Submit(); game.Render(); await capture.ShootAsync("08-replay", "Tick 0");
        }
        private static void Require(FroggerGame game, RoundResult expected)
        {
            if (game.Flow.Game.Result != expected) { throw new InvalidOperationException("撮影場面の結果不一致: " + game.Flow.Game.Result + " / " + expected); }
        }
    }
}
