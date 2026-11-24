using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class MeterTests
    {
        private static readonly GolfConfig _config = new();

        private static Meter At(int t)
        {
            var meter = new Meter(_config);
            for (var i = 0; i < t; i++) { Assert.That(meter.Step(), Is.EqualTo(MeterResult.None)); }
            return meter;
        }

        private static Meter WithPower(int powerTick, int t)
        {
            var meter = At(powerTick);
            Assert.That(meter.Press(), Is.EqualTo(MeterResult.PowerSet));
            for (var i = powerTick; i < t; i++) { meter.Step(); }
            return meter;
        }

        [Test]
        public void GaugeRisesToFullAtTheHalfAndFallsBackToZero()
        {
            var meter = new Meter(_config);
            var half = _config.MeterHalfTicks;
            Assert.That(meter.GaugeAt(0), Is.EqualTo(0));
            Assert.That(meter.GaugeAt(half), Is.EqualTo(1000));
            Assert.That(meter.GaugeAt(half * 2), Is.EqualTo(0));
            Assert.That(meter.GaugeAt(half / 2), Is.EqualTo(meter.GaugeAt((half * 2) - (half / 2))));
        }

        [Test]
        public void PressAtTheStartTickIsIgnored()
        {
            var meter = new Meter(_config);
            Assert.That(meter.Press(), Is.EqualTo(MeterResult.None));
            Assert.That(meter.PowerSet, Is.False);
        }

        [Test]
        public void FirstPressSetsThePowerOfThatTick()
        {
            var meter = WithPower(_config.MeterHalfTicks, _config.MeterHalfTicks);
            Assert.That(meter.Power, Is.EqualTo(1000));
            var low = WithPower(9, 9);
            Assert.That(low.Power, Is.EqualTo(9 * 1000 / _config.MeterHalfTicks));
        }

        [Test]
        public void PowerCanBeSetUntilJustBeforeTheImpactBandThenCancels()
        {
            var last = _config.IdealImpactTick - _config.ImpactWindow - 1;
            Assert.That(At(last).Press(), Is.EqualTo(MeterResult.PowerSet));

            var meter = At(last);
            Assert.That(meter.Step(), Is.EqualTo(MeterResult.Canceled));
            Assert.That(meter.Done, Is.True);
            Assert.That(meter.Press(), Is.EqualTo(MeterResult.None));
        }

        [TestCase(0, MeterResult.Impact, 0)]
        [TestCase(-8, MeterResult.Impact, -8)]
        [TestCase(8, MeterResult.Impact, 8)]
        [TestCase(-9, MeterResult.Mishit, -8)]
        public void ImpactBandBoundaries(int offset, MeterResult result, int error)
        {
            var meter = WithPower(20, _config.IdealImpactTick + offset);
            Assert.That(meter.Press(), Is.EqualTo(result));
            Assert.That(meter.Error, Is.EqualTo(error));
        }

        [Test]
        public void MissingTheSecondPressIsAMishitAfterTheBand()
        {
            var meter = WithPower(20, _config.IdealImpactTick + _config.ImpactWindow);
            Assert.That(meter.Step(), Is.EqualTo(MeterResult.Mishit));
            Assert.That(meter.Error, Is.EqualTo(_config.ImpactWindow));
        }

        [Test]
        public void PressesAfterTheResultAreIgnored()
        {
            var meter = WithPower(20, _config.IdealImpactTick);
            Assert.That(meter.Press(), Is.EqualTo(MeterResult.Impact));
            Assert.That(meter.Press(), Is.EqualTo(MeterResult.None));
            Assert.That(meter.Step(), Is.EqualTo(MeterResult.None));
            Assert.That(meter.Error, Is.EqualTo(0));
        }
    }
}
