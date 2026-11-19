using System;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class ShiftRepeaterTests
    {
        private const int Delay = 3;
        private const int Interval = 2;

        private static ShiftRepeater NewRepeater() => new(Delay, Interval);

        private static void TickQuietly(ShiftRepeater repeater, int count)
        {
            for (var i = 0; i < count; i++) { Assert.That(repeater.Tick(), Is.Null, $"{i} Tick 目"); }
        }

        [Test]
        public void NothingRepeatsWithoutAKey()
        {
            TickQuietly(NewRepeater(), 10);
        }

        [Test]
        public void RepeatStartsAfterTheDelayAndContinuesAtTheInterval()
        {
            var repeater = NewRepeater();
            repeater.Press(ShiftDirection.Left);

            TickQuietly(repeater, Delay - 1);
            Assert.That(repeater.Tick(), Is.EqualTo(ShiftDirection.Left));
            TickQuietly(repeater, Interval - 1);
            Assert.That(repeater.Tick(), Is.EqualTo(ShiftDirection.Left));
        }

        [Test]
        public void ReleasingStopsTheRepeat()
        {
            var repeater = NewRepeater();
            repeater.Press(ShiftDirection.Left);
            repeater.Release(ShiftDirection.Left);
            TickQuietly(repeater, 10);
        }

        [Test]
        public void LaterKeyWinsAndRestartsTheDelay()
        {
            var repeater = NewRepeater();
            repeater.Press(ShiftDirection.Left);
            TickQuietly(repeater, Delay - 1);

            repeater.Press(ShiftDirection.Right);

            TickQuietly(repeater, Delay - 1);
            Assert.That(repeater.Tick(), Is.EqualTo(ShiftDirection.Right));
        }

        [Test]
        public void ReleasingTheLaterKeyReturnsToTheEarlierKeyAfterTheDelay()
        {
            var repeater = NewRepeater();
            repeater.Press(ShiftDirection.Left);
            repeater.Press(ShiftDirection.Right);
            repeater.Release(ShiftDirection.Right);

            TickQuietly(repeater, Delay - 1);
            Assert.That(repeater.Tick(), Is.EqualTo(ShiftDirection.Left));
        }

        [Test]
        public void ReleasingTheEarlierKeyKeepsTheCurrentTiming()
        {
            var repeater = NewRepeater();
            repeater.Press(ShiftDirection.Left);
            repeater.Press(ShiftDirection.Right);
            TickQuietly(repeater, Delay - 1);

            repeater.Release(ShiftDirection.Left);

            Assert.That(repeater.Tick(), Is.EqualTo(ShiftDirection.Right));
        }

        [Test]
        public void ClearForgetsHeldKeys()
        {
            var repeater = NewRepeater();
            repeater.Press(ShiftDirection.Left);
            repeater.Clear();
            TickQuietly(repeater, 10);
        }

        [Test]
        public void RestartIsReportedOnceWhenTheDelayIsSetAgain()
        {
            var repeater = NewRepeater();
            Assert.That(repeater.TakeRestarted(), Is.False);

            repeater.Press(ShiftDirection.Left);
            Assert.That(repeater.TakeRestarted(), Is.True);
            Assert.That(repeater.TakeRestarted(), Is.False);

            repeater.Press(ShiftDirection.Right);
            repeater.TakeRestarted();
            repeater.Release(ShiftDirection.Right);
            Assert.That(repeater.TakeRestarted(), Is.True);
        }

        [Test]
        public void ReleasingTheEarlierKeyIsNotARestart()
        {
            var repeater = NewRepeater();
            repeater.Press(ShiftDirection.Left);
            repeater.Press(ShiftDirection.Right);
            repeater.TakeRestarted();

            repeater.Release(ShiftDirection.Left);

            Assert.That(repeater.TakeRestarted(), Is.False);
        }

        [TestCase(0, 1)]
        [TestCase(1, 0)]
        [TestCase(-1, 1)]
        public void InvalidTimingIsRejected(int delay, int interval)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ShiftRepeater(delay, interval));
        }
    }
}
