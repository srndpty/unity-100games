using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>操縦役の通し（完了判定「3 ホールを規定打数で完走」）と再現性。</summary>
    public sealed class AutopilotTests
    {
        /// <summary>シード 1 の最終ハッシュ（.NET と Mono で同じ値になる）。</summary>
        private const uint SeedOneHash = 0x6E6BB7DAu;

        [Test]
        public void TwentySeedsFinishEveryHoleWithinPar()
        {
            var course = AppKit.Production();
            for (var seed = 1; seed <= 20; seed++)
            {
                var round = AppKit.PlayOut(seed, course: course);
                Assert.That(round.Phase, Is.EqualTo(RoundPhase.Finished), $"シード {seed}");
                for (var hole = 0; hole < Course.HoleCount; hole++)
                {
                    Assert.That(round.ScoreOf(hole), Is.InRange(1, course.Holes[hole].Par), $"シード {seed} の {hole + 1} 番");
                }
            }
        }

        [Test]
        public void SeedOneEndsWithTheSameHashOnEveryRuntime()
        {
            var round = AppKit.PlayOut(1);
            Assert.That(round.Phase, Is.EqualTo(RoundPhase.Finished));
            TestContext.Out.WriteLine($"seed 1: {round.Total} strokes, {round.Ticks} ticks, hash 0x{round.Hash():X8}");
            Assert.That(round.Hash(), Is.EqualTo(SeedOneHash));
        }

        [Test]
        public void ResetDiscardsThePlanOfAnAbandonedRound()
        {
            var pilot = new Autopilot();
            pilot.Reset(9);
            var abandoned = new GolfRound(new GolfConfig(), AppKit.Production(), 9);
            for (var i = 0; i < 40; i++)
            {
                pilot.Act(abandoned);
                abandoned.Step();
            }

            Assert.That(abandoned.Phase, Is.EqualTo(RoundPhase.Swinging));
            pilot.Reset(2);
            var round = new GolfRound(new GolfConfig(), AppKit.Production(), 2);
            while (round.Phase != RoundPhase.Finished)
            {
                pilot.Act(round);
                round.Step();
            }

            Assert.That(round.Hash(), Is.EqualTo(AppKit.PlayOut(2).Hash()));
        }

        [Test]
        public void PredictionsMatchWhenThereIsNoImpactError()
        {
            var round = AppKit.PlayOut(3);
            var exact = 0;
            foreach (var shot in round.Shots)
            {
                if (shot.Setup.Error != 0 || shot.Setup.Mishit) { continue; }
                Assert.That(shot.Difference, Is.EqualTo(0), $"{shot.Hole} 番の {shot.Stroke} 打目");
                exact++;
            }

            Assert.That(exact, Is.GreaterThan(0));
        }
    }
}
