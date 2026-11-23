using System.Diagnostics;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>自動操縦で試合を最後まで通す（完走・不変条件・再現性）。</summary>
    public sealed class AutopilotTests
    {
        /// <summary>シード 1 を自動操縦で最後まで進めた最終ハッシュ。.NET と Mono の両方で一致する（整数だけで計算するため）。</summary>
        private const uint SeedOneFinalHash = 0x15E3CB72u;

        private static TideRun Play(int seed, out int peak)
        {
            var config = new TideConfig();
            var run = new TideRun(config, seed);
            var pilot = new Autopilot();
            peak = 0;
            while (!run.IsOver)
            {
                while (run.IsChoosing) { run.Choose(pilot.ChoiceFor(run)); }
                run.Step(pilot.CommandFor(run));
                if (run.NormalCount > peak) { peak = run.NormalCount; }
                Assert.That(run.NormalCount, Is.LessThanOrEqualTo(config.MaxEnemies));
                Assert.That(run.Elapsed, Is.LessThanOrEqualTo(config.MaxRunTicks));
            }

            return run;
        }

        [Test]
        public void FullRuns_EndByWinOrLoss_WithinTheTimeLimit()
        {
            var watch = Stopwatch.StartNew();
            var wins = 0;
            const int Seeds = 12;
            for (var seed = 1; seed <= Seeds; seed++)
            {
                var run = Play(seed, out var peak);
                Assert.That(run.Status, Is.Not.EqualTo(RunStatus.Playing));
                Assert.That(run.Kills, Is.GreaterThan(0));
                if (run.Status == RunStatus.Won) { wins++; }
                TestContext.WriteLine($"シード {seed}: {run.Status}/{run.End} {run.Elapsed} Tick レベル {run.Level} 撃破 {run.Kills} 出現 {run.Spawned} 体力 {run.Health} 最大生存 {peak}");
            }

            TestContext.WriteLine($"勝ち {wins}/{Seeds}・{watch.ElapsedMilliseconds} ms");
        }

        [Test]
        public void SameSeed_GivesSameFinalHash()
        {
            var first = Play(1, out _);
            TestContext.WriteLine($"シード 1 の最終ハッシュ 0x{first.Hash():X8}（{first.Status} {first.Elapsed} Tick）");
            Assert.That(Play(1, out _).Hash(), Is.EqualTo(first.Hash()));
            Assert.That(first.Hash(), Is.EqualTo(SeedOneFinalHash));
        }
    }
}
