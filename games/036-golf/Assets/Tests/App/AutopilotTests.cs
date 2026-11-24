using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>操縦役の通し（完了判定「3 ホールを規定打数で完走」）と再現性。</summary>
    public sealed class AutopilotTests
    {
        /// <summary>シード 1 の最終ハッシュ（.NET と Mono で同じ値になる）。</summary>
        private const uint SeedOneHash = 0x6E6BB7DAu;

        /// <summary>20 シードの全打球について、ずれ 0 の打球は予測と実際が一致し、ずれのある打球の差も記録する。</summary>
        [Test]
        public void TwentySeedsFinishEveryHoleWithinPar()
        {
            var course = AppKit.Production();
            var shots = 0;
            var exact = 0;
            var bent = 0;
            long worst = 0;
            long total = 0;
            var strokes = 0;
            for (var seed = 1; seed <= 20; seed++)
            {
                var round = AppKit.PlayOut(seed, course: course);
                Assert.That(round.Phase, Is.EqualTo(RoundPhase.Finished), $"シード {seed}");
                for (var hole = 0; hole < Course.HoleCount; hole++)
                {
                    Assert.That(round.ScoreOf(hole), Is.InRange(1, course.Holes[hole].Par), $"シード {seed} の {hole + 1} 番");
                }

                strokes += round.Total;
                foreach (var shot in round.Shots)
                {
                    shots++;
                    if (shot.Setup.Error == 0 && !shot.Setup.Mishit)
                    {
                        Assert.That(shot.Difference, Is.EqualTo(0), $"シード {seed} の {shot.Hole} 番 {shot.Stroke} 打目");
                        exact++;
                        continue;
                    }

                    bent++;
                    total += shot.Difference;
                    worst = System.Math.Max(worst, shot.Difference);
                }
            }

            TestContext.Out.WriteLine($"20 シード：{strokes} 打（par 合計 {20 * 12}）、打球 {shots}（ずれ 0 が {exact}、ずれ ±1 が {bent}）。" +
                $"ずれ ±1 の差：平均 {(bent == 0 ? 0 : total / bent) / 1e6:F2} m・最大 {worst / 1e6:F2} m");
            Assert.That(exact, Is.GreaterThan(0));
            Assert.That(bent, Is.GreaterThan(0), "ずれのある打球も含む");
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
    }
}
