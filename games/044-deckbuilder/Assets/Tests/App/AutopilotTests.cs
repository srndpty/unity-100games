using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>操縦役で通す。勝率は README に記録する。</summary>
    public sealed class AutopilotTests
    {
        [Test]
        public void AutopilotWinsMostRuns()
        {
            var wins = 0;
            var log = new System.Text.StringBuilder();
            for (int seed = 1; seed <= 20; seed++)
            {
                var flow = AppLab.NewFlow(seed);
                AppLab.Drive(flow, new Autopilot());
                var run = flow.Run!;
                if (run.Stage == RunStage.Victory) { wins++; }
                log.Append($"{seed}:{run.Stage}/{run.Hp}/{run.BattlesWon} ");
            }

            TestContext.WriteLine($"操縦役 {wins}/20 {log}");
            Assert.That(wins, Is.GreaterThanOrEqualTo(16), log.ToString());
        }

        [Test]
        public void HumanlikePilotFinishesRunsAndSometimesLoses()
        {
            var wins = 0;
            var log = new System.Text.StringBuilder();
            for (int seed = 1; seed <= 10; seed++)
            {
                var flow = AppLab.NewFlow(seed);
                AppLab.Drive(flow, new Autopilot(depth: 1, mistakePercent: 15, seed: seed));
                var run = flow.Run!;
                Assert.That(run.IsOver, Is.True);
                if (run.Stage == RunStage.Victory) { wins++; }
                log.Append($"{seed}:{run.Stage}/{run.Hp}/{run.BattlesWon} ");
            }

            TestContext.WriteLine($"人寄りの操縦役 {wins}/10 {log}");
            Assert.That(wins, Is.GreaterThanOrEqualTo(1), log.ToString());
        }

        [Test]
        public void SeedOneRunEndsWithAFixedHash()
        {
            var flow = AppLab.NewFlow(1);
            AppLab.Drive(flow, new Autopilot());
            var run = flow.Run!;
            TestContext.WriteLine($"0x{run.Hash():X8} {run.Stage} HP {run.Hp} 枚数 {run.Deck.Count} ターン {run.TotalTurns}");
            Assert.That(run.Stage, Is.EqualTo(RunStage.Victory));
            Assert.That(run.Hash(), Is.EqualTo(0x0C54A0A7u));
        }

        [Test]
        public void PilotWorksThroughTheScreensWithRealPlaybackTicks()
        {
            var flow = AppLab.NewFlow(4);
            var pilot = new Autopilot();
            for (int i = 0; i < 200000 && flow.Screen != AppScreen.Result; i++)
            {
                var intent = pilot.Decide(flow);
                if (intent != null) { flow.Apply(intent.Value); }
                flow.Step();
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Run!.IsOver, Is.True);
            Assert.That(flow.Run.Battle!.Events.Count(e => e.Kind == EventKind.CardPlayed), Is.GreaterThan(0));
        }
    }
}
