using System.Reflection;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class HashTests
    {
        private const uint Scripted = 1723866012u;

        [Test]
        public void TheSameInputsMatchTheFixedHash()
        {
            var race = TestKit.CircuitRace();
            for (var i = 0; i < 400; i++)
            {
                var steer = i > 120 && i < 220 ? 1 : 0;
                var drift = i > 140 && i < 190;
                race.Step(new Drive(true, false, steer, drift));
            }

            Assert.That(race.Hash(), Is.EqualTo(Scripted));
        }

        [Test]
        public void BoostChargeRecoveryAndTheNextGateChangeTheHash()
        {
            var race = TestKit.CircuitRace();
            var baseline = race.Hash();
            var karts = Karts(race);
            karts[0].BoostLeft = 3;
            Assert.That(race.Hash(), Is.Not.EqualTo(baseline));
            karts[0].BoostLeft = 0;
            Assert.That(race.Hash(), Is.EqualTo(baseline));

            karts[0].DriftTicks = 4;
            Assert.That(race.Hash(), Is.Not.EqualTo(baseline));
            karts[0].DriftTicks = 0;

            karts[0].RecoverLeft = 2;
            Assert.That(race.Hash(), Is.Not.EqualTo(baseline));
            karts[0].RecoverLeft = 0;

            karts[0].NextGate = 2;
            Assert.That(race.Hash(), Is.Not.EqualTo(baseline));
        }

        private static Kart[] Karts(Race race)
        {
            var field = typeof(Race).GetField("_karts", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (Kart[])field!.GetValue(race)!;
        }
    }
}
