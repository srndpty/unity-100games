using Game.App;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 証跡撮影の台本。タイトル → 試合中（開始から <see cref="PlayingShotTick"/> Tick）→ 結果を撮る。
    /// 試合は Tick 単位で進め、プレイヤーは動かないので CPU が勝つ。シードは固定なので毎回同じ画面になる。
    /// </summary>
    [RequireComponent(typeof(PongGame))]
    public sealed class PongCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        // サーブ（60 Tick 後）からボールが左へ飛んでいる途中。
        private const int PlayingShotTick = 100;
        private const int MaxMatchTicks = 60 * 60 * 30;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            var game = GetComponent<PongGame>();
            var flow = game.Flow;
            capture.Record("settings", JsonUtility.ToJson(game.Settings));

            await capture.ShootAsync("title", "起動直後");

            flow.Submit();
            capture.Record("matchSeed", flow.MatchSeed.ToString());
            game.AdvanceTicks(PlayingShotTick);
            await capture.ShootAsync("playing", $"tick={flow.Match!.Ticks}");

            game.AdvanceTicks(MaxMatchTicks);
            if (flow.Screen != AppScreen.Result)
            {
                throw new System.InvalidOperationException($"{MaxMatchTicks} Tick 以内に決着しませんでした");
            }

            await capture.ShootAsync("result", $"tick={flow.Match.Ticks}, {flow.Match.LeftScore}-{flow.Match.RightScore}");
        }
    }
}
