using Game.Core;

namespace Game.App.Tests
{
    /// <summary>App のテストの道具。</summary>
    internal static class AppLab
    {
        /// <summary>操縦役で 1 試合を最後まで（または上限まで）進める。</summary>
        public static CrawlRun Play(int seed, int limit = 40_000, CrawlConfig? config = null)
        {
            config ??= new CrawlConfig();
            var run = new CrawlRun(config, seed);
            var pilot = new Autopilot(config);
            for (int i = 0; i < limit && run.State == RunState.Playing; i++)
            {
                run.Submit(pilot.Decide(run.View));
            }

            return run;
        }
    }
}
