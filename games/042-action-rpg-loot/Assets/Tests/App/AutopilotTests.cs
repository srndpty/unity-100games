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

        [Test]
        public void SeeksPotionsEvenWhenTheBagIsFull()
        {
            // 回復薬は持ち物のマスを使わない。持ち物が満杯でも、近くの回復薬を拾いに行く。
            var run = AppLab.NewRun(1);
            foreach (var f in run.Foes)
            {
                f.Dead = true;
                f.Dropped = true;
            }

            var filler = run.EquippedAt(Slot.Weapon)!; // 装備しても強くならない品（操縦役が付け替えて空きを作らない）
            while (run.Inventory.Count < run.Config.InventorySize) { run.AddInventoryForTest(filler); }
            run.Hero.Potions = 0;
            run.AddGroundForTest(FloorMap.TileCenter(36, 21), null);
            var pilot = new Autopilot();
            for (int i = 0; i < 2400 && run.Hero.Potions == 0 && run.FloorNumber == 1; i++) { run.Step(pilot.CommandFor(run)); }
            Assert.That(run.FloorNumber, Is.EqualTo(1), "回復薬を残して階段を降りない");
            Assert.That(run.Hero.Potions, Is.EqualTo(1), "階段への道から外れた行き止まりの部屋（北東）の回復薬を拾いに行く");
        }

        private const uint ExpectedHash = 0x7F2DB26Eu;
    }
}
