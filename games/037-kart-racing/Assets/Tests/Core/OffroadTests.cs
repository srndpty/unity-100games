using System.Reflection;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class OffroadTests
    {
        [Test]
        public void TheTarmacEndsAtTheDrawnEdge()
        {
            var track = TestKit.Circuit();
            var place = new TrackPlace(5, 2000);
            var half = track.HalfWidth(5);
            Assert.That(track.OnTarmac(track.Offset(place, 0), 5), Is.True);
            Assert.That(track.OnTarmac(track.Offset(place, half - 1), 5), Is.True);
            Assert.That(track.OnTarmac(track.Offset(place, half), 5), Is.True, "端の線の上はターマック");
            Assert.That(track.OnTarmac(track.Offset(place, half + 1), 5), Is.False);
            Assert.That(track.OnTarmac(track.Offset(place, -(half + 1)), 5), Is.False);
            Assert.That(track.OnTarmac(track.Offset(place, half + track.Runoff - 1), 5), Is.False);
        }

        [Test]
        public void GuardWallsSitOutsideTheRunoff()
        {
            var track = TestKit.Circuit();
            Assert.That(track.Runoff, Is.EqualTo(3000));
            for (var i = 0; i < track.StationCount; i++)
            {
                var center = track.StationPosition(i);
                var tarmac = IntMath.Sqrt((track.Left(i) - center).LengthSquared);
                var guard = IntMath.Sqrt((track.GuardLeft(i) - center).LengthSquared);
                Assert.That(guard - tarmac, Is.InRange(track.Runoff - 2, track.Runoff + 2), $"点 {i}");
                Assert.That(track.Walls[i].A, Is.EqualTo(track.GuardLeft(i)));
                Assert.That(track.Walls[track.StationCount + i].A, Is.EqualTo(track.GuardRight(i)));
            }
        }

        [Test]
        public void GrassBleedsSpeedDownToTheOffroadLimitEvenWhileBoosting()
        {
            var config = new KartConfig();
            var kart = new Kart { Speed = config.BoostSpeed, BoostLeft = 100 };
            var accel = new Drive(true, false, 0, false);
            var previous = kart.Speed;
            for (var i = 0; i < 100; i++)
            {
                Vehicle.Integrate(config, ref kart, accel, offroad: true, out _);
                Assert.That(kart.Speed, Is.GreaterThanOrEqualTo(config.OffroadSpeed));
                Assert.That(previous - kart.Speed, Is.LessThanOrEqualTo(config.OffroadDrag + config.Drag));
                previous = kart.Speed;
            }

            Assert.That(kart.Speed, Is.EqualTo(config.OffroadSpeed));
            Vehicle.Integrate(config, ref kart, accel, offroad: false, out _);
            Assert.That(kart.Speed, Is.EqualTo(config.OffroadSpeed + config.Accel), "ターマックに戻れば加速する");
        }

        [Test]
        public void SlowKartsAreNotSlowedByGrass()
        {
            var config = new KartConfig();
            var kart = new Kart { Speed = config.OffroadSpeed - config.Accel };
            Vehicle.Integrate(config, ref kart, new Drive(true, false, 0, false), offroad: true, out _);
            Assert.That(kart.Speed, Is.EqualTo(config.OffroadSpeed));
            var reverse = new Kart { Speed = -config.ReverseSpeed };
            Vehicle.Integrate(config, ref reverse, new Drive(false, true, 0, false), offroad: true, out _);
            Assert.That(reverse.Speed, Is.EqualTo(-config.ReverseSpeed), "後退は芝の上限より遅い");
        }

        [Test]
        public void AKartLeavesTheTarmacButStopsAtTheGuard()
        {
            // 下の直線から内側（+Y）へ真っすぐ走る。芝には入れるが、ガードの手前で止まり、内側を横切る近道はできない。
            var config = new KartConfig { StartDelayTicks = 0 };
            var race = new Race(config, TestKit.Circuit());
            Place(race, new Vec2i(150000, 0), Trig.FullTurn / 4, 200);
            var sawGrass = false;
            for (var i = 0; i < 300; i++)
            {
                race.Step(new Drive(true, false, 0, false));
                var kart = race.KartAt(0);
                if (kart.Offroad) { sawGrass = true; }
                Assert.That(kart.Position.Y, Is.LessThanOrEqualTo(5000 + race.Track.Runoff - config.Radius), $"Tick {race.Tick}");
            }

            var stopped = race.KartAt(0);
            Assert.That(sawGrass, Is.True, "ターマックの外へ出られない");
            Assert.That(stopped.Position.Y, Is.GreaterThan(5000 + race.Track.Runoff - config.Radius - 50), "ガードまで届いていない");
            Assert.That(stopped.Laps, Is.EqualTo(0));
            Assert.That(stopped.NextGate, Is.EqualTo(1));
        }

        [Test]
        public void AGateCountsWhenItIsCrossedOnTheGrass()
        {
            var track = TestKit.Circuit();
            var station = track.CheckpointStation(1);
            var lateral = track.HalfWidth(station) + (track.Runoff / 2);
            var before = track.Offset(new TrackPlace(station - 1, 3000), lateral);
            var after = track.Offset(new TrackPlace(station, 1000), lateral);
            Assert.That(track.OnTarmac(before, station), Is.False);
            var kart = new Kart { NextGate = 1, FinishTick = -1 };
            Assert.That(Progress.Cross(track, ref kart, before, after, 1), Is.True);
            Assert.That(kart.NextGate, Is.EqualTo(2));
        }

        [Test]
        public void TheRaceMarksKartsOnTheGrass()
        {
            var config = new KartConfig { StartDelayTicks = 0 };
            var race = new Race(config, TestKit.Circuit());
            Place(race, new Vec2i(150000, -6500), 0, 300);
            race.Step(new Drive(true, false, 0, false));
            Assert.That(race.KartAt(0).Offroad, Is.True);
            Assert.That(race.KartAt(0).Speed, Is.EqualTo(300 + config.Accel - config.OffroadDrag));
            Assert.That(race.KartAt(1).Offroad, Is.False);
        }

        [Test]
        public void ParseRejectsAMissingOrRepeatedRunoff()
        {
            var body = "station 0 0 5000\nstation 4000 0 5000\nstation 4000 4000 5000\nstation 0 4000 5000\n" +
                       "checkpoint 0\ncheckpoint 1\ncheckpoint 2\ncheckpoint 3\n";
            Assert.That(Assert.Throws<System.FormatException>(() => Track.Parse(body, new KartConfig()))!.Message, Does.Contain("runoff"));
            Assert.Throws<System.FormatException>(() => Track.Parse("runoff 1\nrunoff 2\n" + body, new KartConfig()));
            Assert.Throws<System.FormatException>(() => Track.Parse("runoff -1\n" + body, new KartConfig()));
            Assert.Throws<System.FormatException>(() => Track.Parse($"runoff {Track.MaxRunoff + 1}\n" + body, new KartConfig()));
        }

        /// <summary>0 番の車を置き直し、他の 3 台は遠いグリッドに残す。</summary>
        private static void Place(Race race, Vec2i position, int heading, int speed)
        {
            var field = typeof(Race).GetField("_karts", BindingFlags.Instance | BindingFlags.NonPublic);
            var karts = (Kart[])field!.GetValue(race)!;
            var place = race.Track.Project(position, karts[0].Station + 10);
            karts[0].Position = position;
            karts[0].Heading = heading;
            karts[0].Speed = speed;
            karts[0].Station = race.Track.Project(position, place.Station).Station;
            karts[0].Along = place.Along;
        }
    }
}
