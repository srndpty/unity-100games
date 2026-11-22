using System.Diagnostics;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>自動操縦で 5 分の試合を通す（完走・不変条件・再現性）。</summary>
    public sealed class AutopilotTests
    {
        /// <summary>シード 1 を自動操縦で最後まで進めた最終ハッシュ。.NET と Mono の両方で一致する（整数だけで計算するため）。</summary>
        private const uint SeedOneFinalHash = 0xC45939FCu;

        private static SiegeRun Play(int seed, out int maxAlive)
        {
            var config = new SiegeConfig();
            var run = new SiegeRun(config, seed);
            var pilot = new Autopilot();
            maxAlive = 0;
            while (!run.IsOver)
            {
                run.Step(pilot.CommandFor(run));
                if (run.Enemies.Count > maxAlive) { maxAlive = run.Enemies.Count; }
                Assert.That(run.Enemies.Count, Is.LessThanOrEqualTo(config.MaxAlive), "敵密度の上限");
                Assert.That(run.Bullets.Count, Is.LessThanOrEqualTo(config.BulletCapacity));
                Assert.That(run.Elapsed, Is.LessThanOrEqualTo(config.DurationTicks));
            }

            return run;
        }

        [Test]
        public void FullRuns_EndByWinOrLoss_WithinDuration()
        {
            var watch = Stopwatch.StartNew();
            var wins = 0;
            var peak = 0;
            long totalScore = 0;
            const int Seeds = 8;
            for (var seed = 1; seed <= Seeds; seed++)
            {
                var run = Play(seed, out var maxAlive);
                Assert.That(run.Status, Is.Not.EqualTo(RunStatus.Playing));
                Assert.That(run.Kills, Is.GreaterThan(0));
                if (run.Status == RunStatus.Won) { wins++; }
                if (maxAlive > peak) { peak = maxAlive; }
                totalScore += run.Score;
                TestContext.WriteLine($"シード {seed}: {run.Status} {run.Elapsed} Tick 得点 {run.Score} 撃破 {run.Kills} 出現 {run.Spawned} 残機 {run.Ship.Lives} 最大生存 {maxAlive}");
            }

            TestContext.WriteLine($"勝ち {wins}/{Seeds}・最大生存 {peak}・平均得点 {totalScore / Seeds}・{watch.ElapsedMilliseconds} ms");
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
