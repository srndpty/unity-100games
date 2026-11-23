using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>
    /// 自動操縦で長く走る：生成される並びは常に通れる（チャンクの検証の裏付け）、チャンクは累積しない、
    /// 固定シードの最終状態は .NET と Mono で一致する。
    /// </summary>
    public sealed class AutopilotTests
    {
        private static RunRules? _rules;

        private static RunRules Rules => _rules ??= RunRules.Build(new RunConfig(), ChunkLibrary.Default());

        /// <summary>シード 1 を 20000 Tick 走らせた最終状態のハッシュ（.NET と Mono で同じ値）。</summary>
        private const uint Seed1Hash = 0xC90FAC81u;

        [Test]
        public void ManySeeds_RunPastTheDifficultyCap_WithoutCrashing()
        {
            var cap = Difficulty.CapMeters(Rules.Config);
            for (var seed = 1; seed <= 20; seed++)
            {
                var run = new RunnerRun(Rules, seed);
                var pilot = new Autopilot();
                while (run.Meters < cap + 500 && !run.IsOver) { run.Step(pilot.CommandFor(run)); }
                Assert.That(run.IsOver, Is.False, $"シード {seed} は {run.Meters} m でぶつかった");
                Assert.That(pilot.Failures, Is.Zero);
                Assert.That(run.Speed, Is.EqualTo(Rules.Config.TopSpeed));
            }
        }

        [Test]
        public void LongRun_KeepsChunksBounded()
        {
            // 約 1.4 時間（30 万 Tick）。上限の速さ・難度のまま走り続け、生きているチャンクは上限を超えない。
            var run = new RunnerRun(Rules, 77);
            var pilot = new Autopilot();
            var bound = RunRules.ActiveChunkBound(Rules.Config, Rules.ShortestChunk);
            var maxCount = 0;
            for (var tick = 0; tick < 300_000; tick++)
            {
                run.Step(pilot.CommandFor(run));
                maxCount = Math.Max(maxCount, run.Chunks.Count);
            }

            Assert.That(run.IsOver, Is.False);
            Assert.That(maxCount, Is.LessThanOrEqualTo(bound).And.LessThanOrEqualTo(run.Chunks.Capacity));
            Assert.That(run.Chunks.Spawned - run.Chunks.Recycled, Is.EqualTo(run.Chunks.Count));
            Assert.That(run.Chunks.Recycled, Is.GreaterThan(1000));
        }

        [Test]
        public void FixedSeed_FinalHash_IsStable()
        {
            var run = new RunnerRun(Rules, 1);
            var pilot = new Autopilot();
            for (var tick = 0; tick < 20_000; tick++) { run.Step(pilot.CommandFor(run)); }
            Assert.That(run.IsOver, Is.False);
            Assert.That(run.Hash(), Is.EqualTo(Seed1Hash), $"0x{run.Hash():X8}");
        }

        [Test]
        public void NewRun_DiscardsTheOldPlan()
        {
            // 途中でやめた試合の後に、同じ操縦役で始めた試合も、新しい操縦役と同じ結果になる。
            var pilot = new Autopilot();
            var abandoned = new RunnerRun(Rules, 5);
            for (var tick = 0; tick < 777; tick++) { abandoned.Step(pilot.CommandFor(abandoned)); }

            var reused = new RunnerRun(Rules, 6);
            var fresh = new RunnerRun(Rules, 6);
            var other = new Autopilot();
            for (var tick = 0; tick < 3000; tick++)
            {
                reused.Step(pilot.CommandFor(reused));
                fresh.Step(other.CommandFor(fresh));
            }

            Assert.That(reused.Hash(), Is.EqualTo(fresh.Hash()));
        }
    }
}
