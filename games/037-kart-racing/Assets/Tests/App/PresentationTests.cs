using System;
using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class PresentationTests
    {
        [Test]
        public void TheMinimapFitsTheWholeCircuitInsideThePadding()
        {
            var track = Track.Parse(TestKit.DataText("Track.txt"), new KartConfig());
            var frame = new MinimapFrame(track, 500f, 190f, 20f);
            var touchesSide = false;
            for (var i = 0; i < track.StationCount; i++)
            {
                foreach (var point in new[] { track.GuardLeft(i), track.GuardRight(i), track.StationPosition(i) })
                {
                    var mapped = frame.Map(point);
                    Assert.That(mapped.X, Is.InRange(19.99f, 480.01f), $"点 {i}");
                    Assert.That(mapped.Y, Is.InRange(19.99f, 170.01f), $"点 {i}");
                    if (mapped.X < 20.5f || mapped.Y < 20.5f) { touchesSide = true; }
                }
            }

            Assert.That(touchesSide, Is.True, "余白まで広げて使う");
        }

        [Test]
        public void TheMinimapKeepsTheAspectAndPutsNorthUp()
        {
            var track = Track.Parse(TestKit.DataText("Track.txt"), new KartConfig());
            var frame = new MinimapFrame(track, 500f, 190f, 20f);
            var origin = frame.Map(new Vec2i(100000, 10000));
            var east = frame.Map(new Vec2i(110000, 10000));
            var north = frame.Map(new Vec2i(100000, 20000));
            Assert.That(east.X - origin.X, Is.EqualTo(origin.Y - north.Y).Within(0.001f), "縦横で同じ縮尺");
            Assert.That(north.Y, Is.LessThan(origin.Y), "北が上");
            Assert.That(east.X, Is.GreaterThan(origin.X));
        }

        [Test]
        public void TheMinimapRejectsABoxSmallerThanItsPadding()
        {
            var track = Track.Parse(TestKit.DataText("Track.txt"), new KartConfig());
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new MinimapFrame(track, 40f, 190f, 20f));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new MinimapFrame(track, 500f, float.NaN, 20f));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new MinimapFrame(track, 500f, 190f, -1f));
        }

        [TestCase(true, false, 0, false, 80, Smoke.Launch)]
        [TestCase(true, false, 0, false, TyreSmoke.LaunchBelow, Smoke.None)]
        [TestCase(true, false, 0, false, 0, Smoke.None)]
        [TestCase(false, true, 0, false, 200, Smoke.Brake)]
        [TestCase(false, true, 0, false, TyreSmoke.BrakeAbove, Smoke.None)]
        [TestCase(false, true, 0, false, -100, Smoke.None)]
        [TestCase(true, false, 6, false, 300, Smoke.Drift)]
        [TestCase(true, false, 0, true, 120, Smoke.Dust)]
        [TestCase(true, false, 0, true, TyreSmoke.DustAbove, Smoke.None)]
        [TestCase(false, false, 0, false, 300, Smoke.None)]
        public void TyreSmokeFollowsTheInputAndSurface(bool accelerate, bool brake, int drift, bool offroad, int speed, Smoke expected)
        {
            var kart = new Kart { Input = new Drive(accelerate, brake, 0, drift > 0), Speed = speed, DriftTicks = drift, Offroad = offroad, FinishTick = -1 };
            Assert.That(TyreSmoke.Of(kart), Is.EqualTo(expected));
        }

        [Test]
        public void AFinishedKartDoesNotSmoke()
        {
            var kart = new Kart { Input = new Drive(true, false, 0, false), Speed = 50, Finished = true };
            Assert.That(TyreSmoke.Of(kart), Is.EqualTo(Smoke.None));
        }

        [Test]
        public void TheRaceRecordsTheInputAndTheSurfaceForTheSmoke()
        {
            var config = new KartConfig { StartDelayTicks = 0 };
            var race = new Race(config, Track.Parse(TestKit.DataText("Track.txt"), config));
            var drive = new Drive(true, false, 0, false);
            race.Step(drive);
            Assert.That(race.KartAt(0).Input, Is.EqualTo(drive));
            Assert.That(race.KartAt(0).Offroad, Is.False);
            Assert.That(TyreSmoke.Of(race.KartAt(0)), Is.EqualTo(Smoke.Launch), "スタートの加速");
        }
    }
}
