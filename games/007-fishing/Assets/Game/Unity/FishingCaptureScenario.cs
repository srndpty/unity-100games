using System;
using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    public sealed class FishingCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = GetComponent<FishingGame>();
            await capture.WaitUntilAsync(() => game.HudRoot != null, 10);
            capture.Record("settings", JsonUtility.ToJson(game.Settings));
            game.Render(); await capture.ShootAsync("01-title", "開始前");
            game.Flow.Submit(); game.Render(); await capture.ShootAsync("02-points", "ポイント選択");
            game.Flow.Cast();
            while (game.Flow.Game.Phase != FishingPhase.Biting) { game.Flow.StepOneTick(); }
            game.Render(); await capture.ShootAsync("03-bite", "Tick " + game.Flow.Game.TickCount);
            for (var i = 0; i < 90; i++) { Autopilot.Step(game.Flow); }
            game.Render(); await capture.ShootAsync("04-fight", "Tick " + game.Flow.Game.TickCount);
            for (var kind = 0; kind < 3; kind++)
            {
                if (kind > 0) { game.Flow.Submit(); game.Flow.Select((FishKind)kind); game.Flow.Cast(); }
                for (var tick = 0; tick < 3600 && game.Flow.Screen == AppScreen.Playing; tick++) { Autopilot.Step(game.Flow); }
                if (game.Flow.Game.Phase != FishingPhase.Caught) { throw new InvalidOperationException("捕獲できませんでした。"); }
                game.Render(); await capture.ShootAsync("05-catch-" + kind, "Tick " + game.Flow.Game.TickCount);
            }
            game.Flow.Submit(); game.Flow.Cast();
            while (game.Flow.Game.Phase != FishingPhase.Biting) { game.Flow.StepOneTick(); }
            game.Flow.SetInput(false, false); game.Flow.SetInput(true, false); game.Flow.StepOneTick(); game.Flow.SetInput(false, false);
            for (var tick = 0; tick < 3600 && game.Flow.Screen == AppScreen.Playing; tick++)
            { game.Flow.SetInput(false, true); game.Flow.StepOneTick(); }
            if (game.Flow.Game.Reason != EscapeReason.BrokenLine) { throw new InvalidOperationException("糸切れになりませんでした。"); }
            game.Render(); await capture.ShootAsync("08-broken-line", "Tick " + game.Flow.Game.TickCount);
        }
    }
}
