using System;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>
    /// 完了判定：6 弾幕を再現しクリア可能。本番の台本を自動操縦で通し、勝って終わること・各弾幕を体力 0 で突破すること・
    /// 最終の状態ハッシュが .NET と Mono で同じ固定値になることを確かめる。同時の弾数も記録する。
    /// </summary>
    public sealed class AutopilotTests
    {
        /// <summary>シード 1 の通しの最終ハッシュ（.NET と Mono の両方で照合する）。</summary>
        private const uint FinalHashSeed1 = 0x3364D004;

        [Test]
        public void FixedSeed_ClearsAllSixPatternsByDepletingHealth()
        {
            var result = Play(1);
            Assert.That(result.Run.Status, Is.EqualTo(RunStatus.Won));
            Assert.That(result.Defeated, Is.EqualTo(6), "6 弾幕とも体力を削りきる（時間切れなし）");
            Assert.That(result.Run.Hash(), Is.EqualTo(FinalHashSeed1));
            TestContext.Out.WriteLine($"ticks {result.Run.Elapsed}, score {result.Run.Score}, hits {result.Run.Hits}, peak bullets [{string.Join(", ", result.Peak)}]");
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void OtherSeeds_FinishWithAWin(int seed)
        {
            var result = Play(seed);
            Assert.That(result.Run.Status, Is.EqualTo(RunStatus.Won));
            Assert.That(result.Defeated, Is.GreaterThanOrEqualTo(5), "時間切れは多くても 1 つ");
            Assert.That(result.Peak.Max(), Is.LessThan(result.Run.Bullets.Capacity), "弾数は置き場の容量に届かない");
        }

        [Test]
        public void ReusedAfterAbandonedRun_PlaysTheSame()
        {
            var bosses = TestScripts.Load();
            var pilot = new Autopilot();
            var abandoned = new VeilRun(new VeilConfig(), bosses, 1);
            for (var t = 0; t < 333; t++) { abandoned.Step(pilot.CommandFor(abandoned)); }

            var reused = new VeilRun(new VeilConfig(), bosses, 2);
            var fresh = new VeilRun(new VeilConfig(), bosses, 2);
            var freshPilot = new Autopilot();
            for (var t = 0; t < 1500; t++)
            {
                reused.Step(pilot.CommandFor(reused));
                fresh.Step(freshPilot.CommandFor(fresh));
            }

            Assert.That(reused.Hash(), Is.EqualTo(fresh.Hash()), "前の試合の判断を持ち越さない（023 の知見）");
        }

        [Test]
        public void ThroughFlow_PilotPlaysToTheResultScreen()
        {
            var flow = new VeilFlow(new VeilConfig(), TestScripts.Load(), 1) { Pilot = new Autopilot() };
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            for (var n = 0; n < 60 * 60 * 10 && flow.Screen == AppScreen.Playing; n++)
            {
                flow.StepTick();
                flow.EndFrame();
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Run!.Hash(), Is.EqualTo(FinalHashSeed1), "Flow を通しても同じ試合になる");
        }

        private static (VeilRun Run, int Defeated, int[] Peak) Play(int seed)
        {
            var run = new VeilRun(new VeilConfig(), TestScripts.Load(), seed);
            var pilot = new Autopilot();
            var defeated = 0;
            var peak = new int[6];
            for (var n = 0; n < 60 * 60 * 10 && !run.IsOver; n++)
            {
                var stage = run.StageIndex;
                run.Step(pilot.CommandFor(run));
                peak[stage] = Math.Max(peak[stage], run.Bullets.Count);
                if ((run.TakeEvents() & RunEvents.PatternDefeated) != 0) { defeated++; }
            }

            return (run, defeated, peak);
        }
    }
}
