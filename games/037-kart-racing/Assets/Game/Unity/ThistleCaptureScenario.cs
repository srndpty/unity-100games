using Game.App;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>タイトル、グリッド、加速、ヘアピン、ブースト、順位、周回、フィニッシュ、結果を Tick で進めて撮る。</summary>
    public sealed class ThistleCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = GetComponent<ThistleGame>();
            await capture.WaitUntilAsync(() => game.HudRoot != null, 10f);
            capture.Record("settings", JsonUtility.ToJson(game.Settings.Rules));
            capture.Record("track", game.Settings.Track.text);
            game.Render();
            await capture.ShootAsync("01-title", "タイトル");

            game.Flow.Apply(new Intent(IntentKind.Submit, 0));
            game.Flow.Pilot = new Autopilot();
            Step(game, game.Settings.Rules.StartDelayTicks / 2);
            game.Render();
            await capture.ShootAsync("02-grid", "スタート待ちのグリッド");

            Step(game, 80);
            game.Render();
            await capture.ShootAsync("03-straight", "直線の加速");

            var order = OrderOf(game);
            StepUntil(game, () => OrderOf(game) != order, "順位");
            game.Render();
            await capture.ShootAsync("04-order", "順位が変わる");

            StepUntil(game, () => game.Flow.Race.KartAt(0).DriftTicks > 0, "ドリフト");
            game.Render();
            await capture.ShootAsync("05-drift", "ヘアピンのドリフト");

            StepUntil(game, () => game.Flow.Race.KartAt(0).BoostLeft > 0, "ブースト");
            game.Render();
            await capture.ShootAsync("06-boost", "ブースト");

            StepUntil(game, () => game.Flow.Race.KartAt(0).Laps >= 1, "1周");
            Step(game, 40);
            game.Render();
            await capture.ShootAsync("07-lap", "1 周目の通過");

            StepUntil(game, () => game.Flow.Screen == AppScreen.Result, "結果");
            game.Render();
            await capture.ShootAsync("08-result", "結果");
        }

        private static int OrderOf(ThistleGame game)
        {
            var race = game.Flow.Race;
            var order = 0;
            for (var i = 0; i < 4; i++) { order = (order * 10) + race.PlaceOf(i); }
            return order;
        }

        private static void Step(ThistleGame game, int ticks)
        {
            for (var i = 0; i < ticks; i++) { game.Flow.StepTick(); }
        }

        private static void StepUntil(ThistleGame game, System.Func<bool> done, string label)
        {
            for (var i = 0; i < 20000 && !done(); i++) { game.Flow.StepTick(); }
            if (!done()) { throw new System.InvalidOperationException(label + " の場面になりませんでした。"); }
        }
    }
}
