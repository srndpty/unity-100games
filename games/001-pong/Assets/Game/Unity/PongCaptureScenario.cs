using Game.App;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>証跡撮影の台本。タイトル → 試合中 → 結果（プレイヤーは動かないので CPU が勝つ）を撮る。</summary>
    [RequireComponent(typeof(PongGame))]
    public sealed class PongCaptureScenario : MonoBehaviour, ICaptureScenario
    {
        private const float FastForwardScale = 20f;
        private const float MatchTimeoutSeconds = 60f;

        public async Awaitable RunAsync(CaptureContext capture)
        {
            var flow = GetComponent<PongGame>().Flow;

            await Awaitable.WaitForSecondsAsync(0.5f);
            await capture.ShootAsync("title");

            flow.Submit();
            await Awaitable.WaitForSecondsAsync(2.5f);
            await capture.ShootAsync("playing");

            Time.timeScale = FastForwardScale;
            try
            {
                await capture.WaitUntilAsync(() => flow.Screen == AppScreen.Result, MatchTimeoutSeconds);
            }
            finally
            {
                Time.timeScale = 1f;
            }

            await capture.ShootAsync("result");
        }
    }
}
