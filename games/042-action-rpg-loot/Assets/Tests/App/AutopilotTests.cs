using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>完了判定「3 階層を突破可能」。操縦役で入口から番人まで通す。</summary>
    public sealed class AutopilotTests
    {
        /// <summary>通しの上限（約 8 分）。</summary>
        private const int Limit = 60 * 60 * 8;

        [Test]
        public void AutopilotBeatsTheBossOnTwentySeeds()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var run = AppLab.Play(seed, new Autopilot(), Limit);
                Assert.That(run.IsOver, Is.True, $"シード {seed} は {run.FloorNumber} 階で止まった");
                Assert.That(run.Status, Is.EqualTo(RunStatus.Won));
                Assert.That(run.Record.DropRolls, Is.EqualTo(run.Record.Kills), "ドロップの判定は倒した数と同じ（二重ドロップなし）");
                Assert.That(run.Ground.Count, Is.LessThanOrEqualTo(run.Config.GroundCap));
                Assert.That(run.Inventory.Count, Is.LessThanOrEqualTo(run.Config.InventorySize));
            }
        }

        [Test]
        public void HumanlikePilotWithSlowReactionsAlsoWins()
        {
            // 攻撃・照準・薬を 10 Tick（約 0.17 秒）遅れて決める操縦役。
            int wins = 0;
            int deaths = 0;
            for (int seed = 1; seed <= 10; seed++)
            {
                var run = AppLab.Play(seed, new HumanlikePilot(10), Limit);
                if (run.IsOver) { wins++; }
                deaths += run.Record.Deaths;
            }

            TestContext.WriteLine($"humanlike wins {wins}/10 deaths {deaths}");
            Assert.That(wins, Is.EqualTo(10));
            Assert.That(deaths, Is.GreaterThan(0), "一度も倒れないほど易しくはない");
        }

        [Test]
        public void FixedFinalHashMatchesAcrossRuntimes()
        {
            // 整数と Tick だけの規則なので、.NET と Mono で同じ値になる（013）。
            var run = AppLab.Play(1, new Autopilot(), Limit);
            TestContext.WriteLine($"seed 1 tick {run.Tick} hash 0x{run.Hash():X8}");
            Assert.That(run.Hash(), Is.EqualTo(ExpectedHash));
        }

        [Test]
        public void ReusingThePilotAfterAnAbandonedRunGivesTheSameResult()
        {
            // 023：判断の状態は試合が変わったら捨てる。
            var pilot = new Autopilot();
            var abandoned = AppLab.NewRun(1);
            for (int i = 0; i < 3000; i++) { abandoned.Step(pilot.CommandFor(abandoned)); }
            var reused = AppLab.NewRun(2);
            while (!reused.IsOver && reused.Tick < Limit) { reused.Step(pilot.CommandFor(reused)); }
            var fresh = AppLab.Play(2, new Autopilot(), Limit);
            Assert.That(reused.Hash(), Is.EqualTo(fresh.Hash()));
        }

        [Test]
        public void IdleInputDoesNotProgress()
        {
            var run = AppLab.NewRun(1);
            for (int i = 0; i < 3000; i++) { run.Step(HeroCommand.Idle(run.Hero.Position)); }
            Assert.That(run.FloorNumber, Is.EqualTo(1));
            Assert.That(run.Record.Kills, Is.EqualTo(0));
        }

        private const uint ExpectedHash = 0xA6B0053Cu;
    }
}
