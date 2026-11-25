using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class VehicleTests
    {
        [Test]
        public void AccelerationReachesMaxSpeedAndDoesNotPassIt()
        {
            var config = new KartConfig();
            var kart = new Kart();
            var drive = new Drive(accelerate: true, brake: false, steer: 0, drift: false);
            var ticks = (config.MaxSpeed - 1) / config.Accel;
            for (var i = 0; i < ticks; i++) { Vehicle.Integrate(config, ref kart, drive, false, out _); }
            Assert.That(kart.Speed, Is.EqualTo(ticks * config.Accel));
            Vehicle.Integrate(config, ref kart, drive, false, out _);
            Assert.That(kart.Speed, Is.EqualTo(config.MaxSpeed));
            Vehicle.Integrate(config, ref kart, drive, false, out _);
            Assert.That(kart.Speed, Is.EqualTo(config.MaxSpeed));
        }

        [Test]
        public void ZeroSpeedDoesNotTurnOrMove()
        {
            var config = new KartConfig();
            var kart = new Kart { Heading = 12345 };
            var delta = Vehicle.Integrate(config, ref kart, new Drive(false, false, 1, false), false, out _);
            Assert.That(kart.Heading, Is.EqualTo(12345));
            Assert.That(delta, Is.EqualTo(IntGeometry.Vec2i.Zero));
        }

        [Test]
        public void ReverseInvertsTheSteerSign()
        {
            var config = new KartConfig();
            var forward = new Kart { Speed = 200, Heading = 10000 };
            var reverse = new Kart { Speed = -200, Heading = 10000 };
            Vehicle.Integrate(config, ref forward, new Drive(false, false, 1, false), false, out _);
            Vehicle.Integrate(config, ref reverse, new Drive(false, false, 1, false), false, out _);
            Assert.That(forward.Heading, Is.GreaterThan(10000));
            Assert.That(reverse.Heading, Is.LessThan(10000));
            Assert.That(forward.Heading - 10000, Is.EqualTo(10000 - reverse.Heading));
        }

        [Test]
        public void DriftBoostsOnTheExactTickAndNotOneShort()
        {
            var config = new KartConfig();
            var enough = Hold(config, config.MiniTurboTicks);
            var shortByOne = Hold(config, config.MiniTurboTicks - 1);
            Assert.That(enough, Is.EqualTo(config.MiniTurboTicks));
            Assert.That(shortByOne, Is.EqualTo(config.MiniTurboTicks - 1));
        }

        [Test]
        public void TheRaceGrantsBoostOnlyWhenTheChargeIsLongEnough()
        {
            var granted = ReleaseInLane(new KartConfig().MiniTurboTicks);
            var denied = ReleaseInLane(new KartConfig().MiniTurboTicks - 1);
            Assert.That(granted.BoostLeft, Is.EqualTo(new KartConfig().BoostTicks));
            Assert.That(denied.BoostLeft, Is.EqualTo(0));
            Assert.That(denied.DriftTicks, Is.EqualTo(0));
        }

        [Test]
        public void AWallClearsTheCharge()
        {
            var config = new KartConfig { StartDelayTicks = 0 };
            var race = new Race(config, TestKit.Circuit());
            for (var i = 0; i < 30; i++) { race.Step(new Drive(true, false, 0, false)); }
            var sawDrift = false;
            var cleared = false;
            for (var i = 0; i < 200; i++)
            {
                var moment = race.Step(new Drive(true, false, 1, true));
                var kart = race.KartAt(0);
                if (kart.DriftTicks > 0) { sawDrift = true; }
                if (!sawDrift || (moment & RaceMoment.Wall) == 0) { continue; }
                cleared = kart.DriftTicks == 0 && kart.BoostLeft == 0;
                break;
            }

            Assert.That(sawDrift, Is.True, "ドリフトが始まらない");
            Assert.That(cleared, Is.True, "壁に当たっても溜めが残っている");
        }

        [Test]
        public void ReleasingAccelerateClearsTheChargeWithoutABoost()
        {
            var config = new KartConfig();
            var kart = Charging(config, 10);
            Vehicle.Integrate(config, ref kart, new Drive(false, false, 1, true), false, out var released);
            Assert.That(released, Is.EqualTo(0));
            Assert.That(kart.DriftTicks, Is.EqualTo(0));
        }

        [Test]
        public void DriftLagsTheHeading()
        {
            var config = new KartConfig();
            var kart = new Kart { Speed = config.MaxSpeed, Heading = 0 };
            var drive = new Drive(true, false, 1, true);
            Vehicle.Integrate(config, ref kart, drive, false, out _);
            var delta = Vehicle.Integrate(config, ref kart, drive, false, out _);
            Assert.That(kart.DriftTicks, Is.GreaterThan(0));
            Assert.That(delta.Y, Is.LessThan((int)(((long)kart.Speed * IntGeometry.Trig.Sin(kart.Heading)) / IntGeometry.Trig.One)));
        }

        private static Kart ReleaseInLane(int ticks)
        {
            var config = new KartConfig { StartDelayTicks = 0, TurnAtLow = 30, TurnAtHigh = 20, DriftTurn = 10 };
            var race = new Race(config, TestKit.Wide());
            var straight = new Drive(true, false, steer: 0, drift: false);
            for (var i = 0; i < 40; i++) { race.Step(straight); }
            var hold = new Drive(true, false, 1, true);
            for (var i = 0; i < ticks; i++) { race.Step(hold); }
            race.Step(new Drive(true, false, 1, false));
            return race.KartAt(0);
        }

        private static int Hold(KartConfig config, int ticks)
        {
            var kart = Charging(config, ticks);
            Vehicle.Integrate(config, ref kart, new Drive(true, false, 1, false), false, out var released);
            return released;
        }

        private static Kart Charging(KartConfig config, int ticks)
        {
            var kart = new Kart { Speed = config.MaxSpeed };
            var drive = new Drive(true, false, 1, true);
            for (var i = 0; i < ticks; i++) { Vehicle.Integrate(config, ref kart, drive, false, out _); }
            return kart;
        }
    }
}
