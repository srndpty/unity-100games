using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class CpuTests
    {
        [Test]
        public void ThreeCpusFinishThreeLaps()
        {
            var config = new KartConfig();
            var race = TestKit.CircuitRace();
            while (!race.IsOver)
            {
                race.Step(CpuPlan.Intent(race.KartAt(0), race.Track, config, config.PaceOf(0)));
            }

            for (var i = 1; i <= 3; i++)
            {
                var kart = race.KartAt(i);
                Assert.That(kart.Finished, Is.True, $"CPU {i} が完走していない（Tick {race.Tick}、周回 {kart.Laps}、次 {kart.NextGate}）");
                Assert.That(kart.FinishTick, Is.LessThan(config.MaxRaceTicks));
                Assert.That(kart.FinishTick, Is.GreaterThan(0));
            }
        }

        [Test]
        public void ThePlayerPaceFinishesFirst()
        {
            var config = new KartConfig();
            var race = TestKit.CircuitRace();
            while (!race.IsOver)
            {
                race.Step(CpuPlan.Intent(race.KartAt(0), race.Track, config, config.PaceOf(0)));
            }

            Assert.That(race.KartAt(0).Finished, Is.True, Describe(race));
            Assert.That(race.PlaceOf(0), Is.EqualTo(1), Describe(race));
            for (var i = 1; i <= 3; i++) { Assert.That(race.KartAt(i).Finished, Is.True, $"CPU {i}"); }
        }

        [Test]
        public void AKartNoseIntoTheWallRecovers()
        {
            var config = new KartConfig();
            var race = new Race(config, TestKit.Circuit());
            for (var i = 0; i < config.StartDelayTicks + 5; i++) { race.Step(Drive.None); }
            for (var i = 0; i < 200 && race.KartAt(0).RecoverLeft == 0; i++)
            {
                race.Step(new Drive(true, false, 1, false));
            }

            var stuck = race.KartAt(0);
            Assert.That(stuck.RecoverLeft, Is.GreaterThan(0), "壁に付いても回復を始めていない");
            var gate = stuck.NextGate;
            var laps = stuck.Laps;
            var reached = false;
            for (var i = 0; i < 4000 && !race.IsOver; i++)
            {
                race.Step(CpuPlan.Intent(race.KartAt(0), race.Track, config, 1000));
                var kart = race.KartAt(0);
                if (kart.Laps > laps || kart.NextGate != gate)
                {
                    reached = true;
                    break;
                }
            }

            Assert.That(reached, Is.True, "壁から戻って次のゲートへ届かない");
        }

        [Test]
        public void ASharpCornerDriftsAndBoostsOnTheExit()
        {
            var config = new KartConfig();
            var race = TestKit.CircuitRace();
            var drifted = false;
            var boosted = false;
            var minSpeed = int.MaxValue;
            while (!race.IsOver && race.Tick < 8000)
            {
                var moment = race.Step(CpuPlan.Intent(race.KartAt(0), race.Track, config, 1000));
                var kart = race.KartAt(0);
                if (kart.DriftTicks > 0) { drifted = true; }
                if ((moment & RaceMoment.Boost) != 0 && kart.BoostLeft > 0) { boosted = true; }
                if (kart.Speed > 0 && kart.Speed < minSpeed) { minSpeed = kart.Speed; }
                if (drifted && boosted) { break; }
            }

            Assert.That(drifted, Is.True, "曲率の大きい角でドリフトしない");
            Assert.That(boosted, Is.True, "角の出口でブーストしない");
            Assert.That(minSpeed, Is.LessThan(config.MaxSpeed), "角で減速しない");
        }

        [Test]
        public void NoInputLeavesThePlayerLastAndTheRaceStillEnds()
        {
            var config = new KartConfig();
            var race = TestKit.CircuitRace();
            while (!race.IsOver) { race.Step(Drive.None); }
            Assert.That(race.Tick, Is.EqualTo(config.MaxRaceTicks));
            Assert.That(race.PlaceOf(0), Is.EqualTo(4));
            for (var i = 1; i <= 3; i++) { Assert.That(race.KartAt(i).Finished, Is.True, $"CPU {i}"); }
        }

        private static string Describe(Race race) =>
            $"順位 {race.PlaceOf(0)} 記録 {race.KartAt(0).FinishTick}/{race.KartAt(1).FinishTick}/{race.KartAt(2).FinishTick}/{race.KartAt(3).FinishTick}";
    }
}
