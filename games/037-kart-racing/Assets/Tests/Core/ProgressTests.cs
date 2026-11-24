using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class ProgressTests
    {
        [Test]
        public void ReverseCrossingsDoNotAddALap()
        {
            var track = TestKit.Circuit();
            var kart = new Kart { NextGate = 0, FinishTick = -1 };
            var (before, after) = Across(track, 0, 800);
            for (var i = 0; i < 5; i++)
            {
                Assert.That(Progress.Cross(track, ref kart, after, before, i + 1), Is.False);
            }

            Assert.That(kart.Laps, Is.EqualTo(0));
            Assert.That(kart.NextGate, Is.EqualTo(0));
        }

        [Test]
        public void AForwardCircuitAddsOneLap()
        {
            var track = TestKit.Circuit();
            var kart = new Kart { NextGate = 1, FinishTick = -1 };
            for (var gate = 1; gate < track.CheckpointCount; gate++)
            {
                var (before, after) = Across(track, gate, 800);
                Assert.That(Progress.Cross(track, ref kart, before, after, gate), Is.True, $"ゲート {gate}");
            }

            var finish = Across(track, 0, 800);
            Assert.That(Progress.Cross(track, ref kart, finish.Before, finish.After, 10), Is.True);
            Assert.That(kart.Laps, Is.EqualTo(1));
            Assert.That(kart.Finished, Is.False);
        }

        [Test]
        public void SittingOnTheLineDoesNotCount()
        {
            var track = TestKit.Circuit();
            var gate = track.Gates[1];
            var kart = new Kart { NextGate = 1, FinishTick = -1 };
            Assert.That(Progress.Cross(track, ref kart, gate.A, gate.B, 1), Is.False);
            Assert.That(kart.NextGate, Is.EqualTo(1));
        }

        [Test]
        public void SkippingAGateDoesNotAdvance()
        {
            var track = TestKit.Circuit();
            var kart = new Kart { NextGate = 1, FinishTick = -1 };
            var skipped = Across(track, 2, 800);
            Assert.That(Progress.Cross(track, ref kart, skipped.Before, skipped.After, 1), Is.False);
            Assert.That(kart.NextGate, Is.EqualTo(1));

            var first = Across(track, 1, 800);
            Assert.That(Progress.Cross(track, ref kart, first.Before, first.After, 2), Is.True);
            var jumped = Across(track, 3, 800);
            Assert.That(Progress.Cross(track, ref kart, jumped.Before, jumped.After, 3), Is.False);
            Assert.That(kart.NextGate, Is.EqualTo(2));
        }

        [Test]
        public void APathThatStopsBeforeTheGateDoesNotPassIt()
        {
            var track = TestKit.Circuit();
            var (before, after) = Across(track, 1, 2000);
            var stopped = before + IntMath.Scale(after - before, 400);
            var kart = new Kart { NextGate = 1, FinishTick = -1 };
            Assert.That(Track.ForwardCross(track.Gates[1], before, after), Is.True);
            Assert.That(Progress.Cross(track, ref kart, before, stopped, 1), Is.False);
            Assert.That(kart.NextGate, Is.EqualTo(1));
        }

        [Test]
        public void TheThirdFinishLineCompletesTheRace()
        {
            var track = TestKit.Circuit();
            var kart = new Kart { NextGate = 0, Laps = 2, Speed = 300, FinishTick = -1 };
            var (before, after) = Across(track, 0, 800);
            Assert.That(Progress.Cross(track, ref kart, before, after, 77), Is.True);
            Assert.That(kart.Laps, Is.EqualTo(3));
            Assert.That(kart.Finished, Is.True);
            Assert.That(kart.FinishTick, Is.EqualTo(77));
            Assert.That(kart.Speed, Is.EqualTo(0));
            Assert.That(Progress.Cross(track, ref kart, before, after, 78), Is.False);
        }

        private static (Vec2i Before, Vec2i After) Across(Track track, int gate, int distance)
        {
            var station = track.CheckpointStation(gate);
            var forward = new Vec2i(Trig.Cos(track.Direction(station)), Trig.Sin(track.Direction(station)));
            var mid = track.StationPosition(station);
            return (mid - IntMath.Scale(forward, distance), mid + IntMath.Scale(forward, distance));
        }
    }
}
