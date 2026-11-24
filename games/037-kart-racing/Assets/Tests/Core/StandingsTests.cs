using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class StandingsTests
    {
        [Test]
        public void FinishedKartsRankByTimeThenId()
        {
            var karts = Fresh();
            for (var i = 0; i < karts.Length; i++)
            {
                karts[i].Finished = true;
                karts[i].FinishTick = 100;
            }

            karts[2].FinishTick = 50;
            karts[0].FinishTick = 80;
            var places = Standings.Places(karts, 4);
            AssertPermutation(places);
            Assert.That(places[2], Is.EqualTo(1));
            Assert.That(places[0], Is.EqualTo(2));
            Assert.That(places[1], Is.EqualTo(3));
            Assert.That(places[3], Is.EqualTo(4));
        }

        [Test]
        public void UnfinishedKartsRankByLapsGatesAndProgress()
        {
            var karts = Fresh();
            karts[3].Laps = 2;
            karts[1].Laps = 1;
            karts[1].NextGate = 3;
            karts[2].Laps = 1;
            karts[2].NextGate = 2;
            karts[0].NextGate = 2;
            karts[0].SectionAlong = 100;
            var places = Standings.Places(karts, 4);
            AssertPermutation(places);
            Assert.That(places[3], Is.EqualTo(1));
            Assert.That(places[1], Is.EqualTo(2));
            Assert.That(places[2], Is.EqualTo(3));
            Assert.That(places[0], Is.EqualTo(4));
        }

        [Test]
        public void ATieBreaksTowardTheSmallerId()
        {
            var places = Standings.Places(Fresh(), 4);
            AssertPermutation(places);
            Assert.That(places[0], Is.EqualTo(1));
            Assert.That(places[1], Is.EqualTo(2));
            Assert.That(places[2], Is.EqualTo(3));
            Assert.That(places[3], Is.EqualTo(4));
        }

        [Test]
        public void InputAfterTheResultDoesNotChangePlaces()
        {
            var config = new KartConfig { MaxRaceTicks = 30, StartDelayTicks = 0 };
            var race = new Race(config, TestKit.Wide());
            while (!race.IsOver) { race.Step(new Drive(true, false, 0, false)); }
            var before = Places(race);
            race.Step(new Drive(true, true, 1, true));
            Assert.That(Places(race), Is.EqualTo(before));
            Assert.That(race.Tick, Is.EqualTo(config.MaxRaceTicks));
        }

        private static Kart[] Fresh()
        {
            var karts = new Kart[KartConfig.KartCount];
            for (var i = 0; i < karts.Length; i++) { karts[i].FinishTick = -1; }
            return karts;
        }

        private static int[] Places(Race race)
        {
            var places = new int[KartConfig.KartCount];
            for (var i = 0; i < places.Length; i++) { places[i] = race.PlaceOf(i); }
            return places;
        }

        private static void AssertPermutation(int[] places)
        {
            var seen = new bool[places.Length];
            foreach (var place in places)
            {
                Assert.That(place, Is.InRange(1, places.Length));
                Assert.That(seen[place - 1], Is.False);
                seen[place - 1] = true;
            }
        }
    }
}
