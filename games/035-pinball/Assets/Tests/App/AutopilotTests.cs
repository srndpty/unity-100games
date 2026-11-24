using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class AutopilotTests
    {
        /// <summary>操縦役で 20 シードとも 3 球を失って試合が終わる（台に球が止まって終わらない場所がない）。</summary>
        [Test]
        public void EveryGameEnds()
        {
            var multiballs = 0;
            for (var seed = 1; seed <= 20; seed++)
            {
                var run = AppKit.PlayOut(seed, 60000);
                Assert.That(run.Status, Is.EqualTo(RunStatus.Over), $"seed {seed}");
                Assert.That(run.BallsLost, Is.GreaterThanOrEqualTo(3));
                multiballs += run.BallsServed - run.BallNumber;
            }

            Assert.That(multiballs, Is.GreaterThan(20), "マルチボールにもなる");
        }

        /// <summary>最終ハッシュの固定値。整数だけで進むので .NET と Mono（Unity）で一致する。</summary>
        [TestCase(1, 4289, 0x499B0F08u)]
        [TestCase(6, 27384, 0xB3FBF3A2u)]
        public void FinalHashIsFixed(int seed, long ticks, uint hash)
        {
            var run = AppKit.PlayOut(seed, 60000);
            Assert.That(run.Ticks, Is.EqualTo(ticks));
            Assert.That(run.Hash(), Is.EqualTo(hash), $"0x{run.Hash():X8}");
        }

        /// <summary>途中でやめた試合の後で Reset しても、同じシードなら同じ試合になる（判断の状態を持ち越さない、023）。</summary>
        [Test]
        public void ResetDropsThePreviousGame()
        {
            var pilot = new Autopilot();
            pilot.Reset(2);
            var abandoned = new PinballRun(new PinballConfig(), AppKit.Production(), 2);
            for (var t = 0; t < 777; t++) { abandoned.Step(pilot.InputFor(abandoned)); }

            pilot.Reset(1);
            var run = new PinballRun(new PinballConfig(), AppKit.Production(), 1);
            while (run.Status == RunStatus.Playing) { run.Step(pilot.InputFor(run)); }
            Assert.That(run.Hash(), Is.EqualTo(AppKit.PlayOut(1, 60000).Hash()));
        }

        [Test]
        public void PlungesTheLaneBall()
        {
            var pilot = new Autopilot();
            pilot.Reset(1);
            var run = new PinballRun(new PinballConfig(), AppKit.Production(), 1);
            var launches = 0;
            for (var t = 0; t < 120; t++) { launches += run.Step(pilot.InputFor(run)).Launches; }
            Assert.That(launches, Is.EqualTo(1));
            Assert.That(run.BallAt(0).State, Is.EqualTo(BallState.Free));
        }
    }
}
