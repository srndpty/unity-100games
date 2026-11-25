using System;
using NUnit.Framework;
using TileMotion;

namespace Game.Core.Tests
{
    public sealed class FlightModelTests
    {
        private const int Full = Controls.Full;

        private static readonly Runway _runway = new(0, 0, 1500 * Units.PerMeter, 25 * Units.PerMeter);

        private static FlightConfig Config() => new FlightConfig().Validate();

        private static Aircraft Level(FlightConfig config, int metersPerSecond, int throttle = 600) =>
            Aircraft.InFlight(config, new Vec3l(0, 2000 * Units.PerMeter, 0), 0, (long)metersPerSecond * Units.MetersPerSecond, throttle);

        private static Controls Pitch(int value) => new(value, 0, 0, 0);

        // ---- 機首と速度方向のずれ ----

        [Test]
        public void PullingUpPutsTheNoseAboveTheVelocity()
        {
            var config = Config();
            var plane = Level(config, 150);
            FlightModel.Step(config, plane, Pitch(Full), _runway);

            // 機首は 1 Tick で上がるが、速度の向きはまだほとんど水平のまま。
            Assert.That(plane.Air.Alpha, Is.GreaterThan(0));
            Assert.That(plane.Attitude.Forward.Y, Is.GreaterThan(Math.Abs(plane.Velocity.Y * Fixed.One / plane.Velocity.Length)));
        }

        [Test]
        public void VelocityTurnsTowardTheNoseAfterReleasing()
        {
            var config = Config();
            var plane = Level(config, 150);
            for (var tick = 0; tick < 20; tick++) { FlightModel.Step(config, plane, Pitch(Full), _runway); }
            var pulled = plane.Air.Alpha;
            Assert.That(pulled, Is.GreaterThan(config.LevelAngle), "引いている間は水平飛行より大きい迎角");

            for (var tick = 0; tick < 120; tick++) { FlightModel.Step(config, plane, Controls.None, _runway); }
            Assert.That(Math.Abs(plane.Air.Alpha), Is.LessThan(pulled / 2), "離すと揚力が速度を機首の向きへ寄せる");
            Assert.That(plane.Velocity.Y, Is.GreaterThan(0), "速度は機首の向き（上向き）へ曲がった");
        }

        [Test]
        public void YawCreatesSideslipThatWeathervaningRemoves()
        {
            var config = Config();
            var plane = Level(config, 150);
            for (var tick = 0; tick < 30; tick++) { FlightModel.Step(config, plane, new Controls(0, 0, Full, 0), _runway); }

            // 機首を右へ振ると、速度は機首より左に残る（横滑り角は負）。
            var slipped = plane.Air.Beta;
            Assert.That(slipped, Is.LessThan(0));

            for (var tick = 0; tick < 120; tick++) { FlightModel.Step(config, plane, Controls.None, _runway); }
            Assert.That(Math.Abs(plane.Air.Beta), Is.LessThan(Math.Abs(slipped) / 4));
        }

        [Test]
        public void AirDataMeasuresTheAngleBetweenNoseAndVelocity()
        {
            var config = Config();

            // 速度は水平の北向き。機首を 4.4°（800）上げ、別に 2.2°（400）右へ向ける。
            var velocity = new Vec3l(0, 0, 150 * Units.MetersPerSecond);
            var pitched = AirData.Of(config, velocity, Quat.FromHeadingPitch(0, 800), false);
            Assert.That(pitched.Alpha, Is.EqualTo(800));
            Assert.That(pitched.Beta, Is.EqualTo(0));

            var yawed = AirData.Of(config, velocity, Quat.FromHeadingPitch(400, 0), false);
            Assert.That(yawed.Alpha, Is.EqualTo(0));
            Assert.That(yawed.Beta, Is.EqualTo(-400), "機首が右なら速度は機首の左");

            var nosedDown = AirData.Of(config, velocity, Quat.FromHeadingPitch(0, -800), false);
            Assert.That(nosedDown.Alpha, Is.EqualTo(-800));
        }

        [TestCase(40, 45)]
        [TestCase(40, 90)]
        [TestCase(40, 180)]
        [TestCase(1000, 45)]
        [TestCase(1000, 90)]
        [TestCase(1000, 180)]
        public void WeathervaneTurnsByTheIntendedAngleInOneTick(int weathervane, int slipDegrees)
        {
            // 機首は北、速度は機首から slipDegrees 右（横滑り角 = slipDegrees）。舵は使わない。
            var config = new FlightConfig { Weathervane = weathervane }.Validate();
            var plane = Level(config, 150);
            var beta = Units.Degrees(slipDegrees);
            var speed = 150L * Units.MetersPerSecond;
            plane.Velocity = new Vec3l(Fixed.Round(Fixed.Sin(beta) * speed), 0, Fixed.Round(Fixed.Cos(beta) * speed));
            Assert.That(Math.Abs(AirData.Of(config, plane.Velocity, plane.Attitude, false).Beta), Is.EqualTo(Fixed.Wrap(beta) == Units.FullTurn / 2 ? Units.FullTurn / 2 : beta).Within(16));

            FlightModel.Step(config, plane, Controls.None, _runway);

            // 要求は 横滑り角 × 割合、ただし 1 Tick に 10° まで。1 次近似でも要求どおりに回る。
            var intended = Math.Min(slipDegrees * weathervane / 1000.0, 10.0);
            var forward = plane.Attitude.Forward;
            var turned = Math.Atan2(forward.X, forward.Z) * 180 / Math.PI;
            Assert.That(turned, Is.EqualTo(intended).Within(0.1), $"風見 {weathervane}‰、横滑り {slipDegrees}°");
        }

        // ---- 失速・速度低下 ----

        [Test]
        public void LiftDropsJustBeyondTheStallAngle()
        {
            var config = Config();
            var speed = 150L * Units.MetersPerSecond;
            var atStall = config.Lift(config.StallAngle, speed);
            var beyond = config.Lift(config.StallAngle + 1, speed);
            Assert.That(atStall, Is.GreaterThan(config.Lift(config.StallAngle - 1, speed)));
            Assert.That(beyond, Is.EqualTo(atStall * config.StallLiftPercent / 100).Within(2));
            Assert.That(config.Lift(-(config.StallAngle + 1), speed), Is.EqualTo(-beyond), "負の迎角も対称");
        }

        [Test]
        public void StallFlagChangesExactlyAtTheStallAngle()
        {
            // 迎角は角度 16 単位の刻みで測るので、刻みに乗る失速角で境界を確かめる。
            var config = new FlightConfig { StallAngle = 3648 }.Validate();
            var velocity = new Vec3l(0, 0, 150 * Units.MetersPerSecond);
            Assert.That(AirData.Of(config, velocity, Quat.FromHeadingPitch(0, 3648), false).Stalled, Is.False);
            Assert.That(AirData.Of(config, velocity, Quat.FromHeadingPitch(0, 3664), false).Stalled, Is.True);
            Assert.That(AirData.Of(config, velocity, Quat.FromHeadingPitch(0, 3664), true).Stalled, Is.False, "地上では失速しない");
        }

        [Test]
        public void StallFlagChangesAtTheStallSpeed()
        {
            var config = Config();
            var forward = Quat.Identity;
            Assert.That(AirData.Of(config, new Vec3l(0, 0, config.StallSpeed), forward, false).Stalled, Is.False);
            Assert.That(AirData.Of(config, new Vec3l(0, 0, config.StallSpeed - 1), forward, false).Stalled, Is.True);
        }

        [Test]
        public void ControlsLoseAuthorityAtLowSpeed()
        {
            var config = Config();
            var fast = Level(config, 80);
            var slow = Level(config, 40);
            FlightModel.Step(config, fast, new Controls(0, Full, 0, 0), _runway);
            FlightModel.Step(config, slow, new Controls(0, Full, 0, 0), _runway);

            // 舵の効きは (速さ / 舵が全部効く速さ)²。半分の速さではロールの角速度が 1/4。
            var fastRoll = -Fixed.Wrap(Trig(fast.Attitude.Right));
            var slowRoll = -Fixed.Wrap(Trig(slow.Attitude.Right));
            Assert.That(fastRoll, Is.EqualTo(config.RollRate).Within(16));
            Assert.That(slowRoll, Is.EqualTo(config.RollRate / 4).Within(16));
        }

        [Test]
        public void StalledAircraftDropsItsNoseEvenWhilePulling()
        {
            var config = Config();
            var plane = Level(config, 45, 0);
            var startAltitude = plane.Position.Y;
            for (var tick = 0; tick < 240; tick++) { FlightModel.Step(config, plane, Pitch(Full), _runway); }
            Assert.That(plane.Attitude.Forward.Y, Is.LessThan(0), "失速中は引き続けても機首が下がる");
            Assert.That(plane.Position.Y, Is.LessThan(startAltitude - (50 * Units.PerMeter)), "高度を失う");
        }

        [Test]
        public void ClimbingWithoutThrottleEndsInAStall()
        {
            var config = Config();
            var plane = Level(config, 140, 0);
            var stalledAt = -1;
            for (var tick = 0; tick < 1200 && stalledAt < 0; tick++)
            {
                var climbing = plane.Attitude.Forward.Y < Fixed.One / 2;
                FlightModel.Step(config, plane, new Controls(climbing ? Full : 0, 0, 0, -Full), _runway);
                if (plane.Air.Stalled) { stalledAt = tick; }
            }

            Assert.That(stalledAt, Is.GreaterThan(0), "推力なしで上り続けると速度が落ちて失速する");
            Assert.That(plane.Air.Speed, Is.LessThan(100L * Units.MetersPerSecond));
        }

        [Test]
        public void SustainedPullBleedsSpeed()
        {
            var config = Config();
            var straight = Level(config, 150, 1000);
            var turning = Level(config, 150, 1000);
            for (var tick = 0; tick < 120; tick++)
            {
                FlightModel.Step(config, straight, Controls.None, _runway);
                FlightModel.Step(config, turning, Pitch(Full), _runway);
            }

            Assert.That(turning.Air.Speed, Is.LessThan(straight.Air.Speed - (20L * Units.MetersPerSecond)));
        }

        [Test]
        public void SpeedNeverExceedsTheLimitInAFullThrottleDive()
        {
            var config = Config();
            var plane = Aircraft.InFlight(config, new Vec3l(0, 9_000 * Units.PerMeter, 0), 0, 200L * Units.MetersPerSecond, 1000);
            plane.Attitude = Quat.FromHeadingPitch(0, -16000);
            for (var tick = 0; tick < 1500 && !plane.Crashed; tick++)
            {
                FlightModel.Step(config, plane, Controls.None, _runway);
                Assert.That(plane.Velocity.Length, Is.LessThanOrEqualTo(config.SpeedLimit));
            }
        }

        // ---- 離陸 ----

        [Test]
        public void PitchHasNoEffectBelowRotateSpeed()
        {
            var config = Config();
            var plane = Aircraft.OnRunway(config, _runway);
            while (plane.Velocity.Length + config.Thrust < config.RotateSpeed)
            {
                Assert.That(FlightModel.Step(config, plane, new Controls(Full, 0, 0, Full), _runway), Is.EqualTo(FlightEvent.None));
                Assert.That(plane.GroundPitch, Is.Zero);
                Assert.That(plane.Grounded, Is.True);
            }
        }

        [Test]
        public void RotatingAboveRotateSpeedLiftsOff()
        {
            var config = Config();
            var plane = Aircraft.OnRunway(config, _runway);
            var tookOff = -1;
            for (var tick = 0; tick < 1200 && tookOff < 0; tick++)
            {
                var rotate = plane.Velocity.Length >= config.RotateSpeed ? Full : 0;
                if (FlightModel.Step(config, plane, new Controls(rotate, 0, 0, Full), _runway) == FlightEvent.TookOff) { tookOff = tick; }
            }

            Assert.That(tookOff, Is.GreaterThan(0));
            Assert.That(plane.Grounded, Is.False);
            Assert.That(plane.Velocity.Y, Is.GreaterThan(0));
            Assert.That(plane.Velocity.Length, Is.GreaterThanOrEqualTo(config.LiftOffSpeed() - 1));
            Assert.That(_runway.Contains(plane.Position), Is.True, "滑走路の中で浮く");

            for (var tick = 0; tick < 60; tick++) { FlightModel.Step(config, plane, Controls.None, _runway); }
            Assert.That(plane.Crashed, Is.False);
            Assert.That(plane.Position.Y, Is.GreaterThan(config.GearHeight));
        }

        [Test]
        public void RunningOffTheRunwayEndWithoutRotatingCrashes()
        {
            var config = Config();
            var plane = Aircraft.OnRunway(config, _runway);
            var result = FlightEvent.None;
            for (var tick = 0; tick < 3000 && result == FlightEvent.None; tick++)
            {
                result = FlightModel.Step(config, plane, new Controls(0, 0, 0, Full), _runway);
            }

            Assert.That(result, Is.EqualTo(FlightEvent.Crashed));
            Assert.That(plane.Position.Z, Is.GreaterThan(_runway.EndZ));
        }

        [Test]
        public void SteeringOffTheSideOfTheRunwayCrashes()
        {
            var config = Config();
            var plane = Aircraft.OnRunway(config, _runway);
            var result = FlightEvent.None;
            for (var tick = 0; tick < 3000 && result == FlightEvent.None; tick++)
            {
                result = FlightModel.Step(config, plane, new Controls(0, 0, Full, 500), _runway);
            }

            Assert.That(result, Is.EqualTo(FlightEvent.Crashed));
            Assert.That(Math.Abs(plane.Position.X), Is.GreaterThan(_runway.HalfWidth));
        }

        [Test]
        public void StandingStillIgnoresSteering()
        {
            var config = Config();
            var plane = Aircraft.OnRunway(config, _runway);
            for (var tick = 0; tick < 30; tick++) { FlightModel.Step(config, plane, new Controls(Full, Full, Full, 0), _runway); }
            Assert.That(plane.Velocity, Is.EqualTo(Vec3l.Zero));
            Assert.That(plane.GroundHeading, Is.Zero);
            Assert.That(plane.Position, Is.EqualTo(new Vec3l(0, config.GearHeight, 0)));
        }

        // ---- 墜落 ----

        [Test]
        public void TouchingTheGroundExactlyWhileDescendingCrashes()
        {
            var config = Config();
            var plane = Level(config, 100, 0);
            plane.Velocity = new Vec3l(0, -1000, plane.Velocity.Z);
            var exact = Probe(config, plane, Controls.None);

            // 1 Tick 後にちょうど車輪の高さに着く位置から始める。
            plane.Position = new Vec3l(0, plane.Position.Y - (exact.Position.Y - config.GearHeight), 0);
            Assert.That(FlightModel.Step(config, plane, Controls.None, _runway), Is.EqualTo(FlightEvent.Crashed));

            // 1 単位上なら触れない。
            var above = Level(config, 100, 0);
            above.Velocity = new Vec3l(0, -1000, above.Velocity.Z);
            above.Position = new Vec3l(0, above.Position.Y - (exact.Position.Y - config.GearHeight) + 1, 0);
            Assert.That(FlightModel.Step(config, above, Controls.None, _runway), Is.EqualTo(FlightEvent.None));
        }

        [Test]
        public void FastDiveThroughTheGroundInOneTickCrashes()
        {
            var config = Config();
            var plane = Aircraft.InFlight(config, new Vec3l(0, config.GearHeight + (3 * Units.PerMeter), 0), 0, 300L * Units.MetersPerSecond, 1000);
            plane.Attitude = Quat.FromHeadingPitch(0, -16384);
            plane.Velocity = new Vec3l(0, -300L * Units.MetersPerSecond, 0);
            Assert.That(FlightModel.Step(config, plane, Controls.None, _runway), Is.EqualTo(FlightEvent.Crashed));
            Assert.That(plane.Position.Y, Is.EqualTo(config.GearHeight));
        }

        [Test]
        public void CrashedAircraftNoLongerChanges()
        {
            var config = Config();
            var plane = Level(config, 100);
            plane.Crashed = true;
            var before = Hash(plane);
            Assert.That(FlightModel.Step(config, plane, new Controls(Full, Full, Full, Full), _runway), Is.EqualTo(FlightEvent.None));
            Assert.That(Hash(plane), Is.EqualTo(before));
        }

        // ---- 負テスト ----

        [Test]
        public void OutOfRangeControlsAreRejectedWithoutChangingState()
        {
            var config = Config();
            var plane = Level(config, 100);
            var before = Hash(plane);
            Assert.Throws<ArgumentOutOfRangeException>(() => FlightModel.Step(config, plane, new Controls(Full + 1, 0, 0, 0), _runway));
            Assert.Throws<ArgumentOutOfRangeException>(() => FlightModel.Step(config, plane, new Controls(0, -Full - 1, 0, 0), _runway));
            Assert.Throws<ArgumentOutOfRangeException>(() => FlightModel.Step(config, plane, new Controls(0, 0, int.MaxValue, 0), _runway));
            Assert.Throws<ArgumentOutOfRangeException>(() => FlightModel.Step(config, plane, new Controls(0, 0, 0, int.MinValue), _runway));
            Assert.That(Hash(plane), Is.EqualTo(before));
        }

        [Test]
        public void ConfigRejectsOutOfRangeAndInconsistentValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlightConfig { Gravity = 0 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlightConfig { MaxSpeed = FlightConfig.MaxSpeedLimit + 1 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlightConfig { SpeedLimit = new FlightConfig().MaxSpeed - 1 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlightConfig { LevelAngle = new FlightConfig().StallAngle }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlightConfig { StallPitchDrop = new FlightConfig().PitchRate }.Validate(), "失速中は引いても機首が下がる");
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlightConfig { RotateSpeed = new FlightConfig().StallSpeed - 1 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlightConfig { MaxGroundPitch = new FlightConfig().StallAngle + 1 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlightConfig { Weathervane = -1 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlightConfig { ThrottleRate = 0 }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new FlightConfig { LevelSpeed = (20 * Units.MetersPerSecond) - 1 }.Validate());
        }

        [Test]
        public void ConfigRejectsSettingsThatCannotTakeOff()
        {
            // 推力が転がり抵抗と抗力に負ける。
            Assert.Throws<ArgumentException>(() => new FlightConfig { Thrust = 200, RollingFriction = 200 }.Validate());

            // 機首上げの上限が小さすぎて、最高速度でも揚力が重さに届かない。
            Assert.Throws<ArgumentException>(() => new FlightConfig { MaxGroundPitch = 100, LevelSpeed = 190 * Units.MetersPerSecond }.Validate());
        }

        [Test]
        public void ValidateReturnsAnIndependentCopy()
        {
            var original = new FlightConfig();
            var validated = original.Validate();
            original.Thrust = 1;
            Assert.That(validated.Thrust, Is.EqualTo(new FlightConfig().Thrust));
        }

        // ---- 再現性 ----

        [Test]
        public void SameInputsGiveTheSameFixedHash()
        {
            // 整数だけで計算するので、.NET と Mono（Unity）で同じ値になる。
            var config = Config();
            var plane = Aircraft.OnRunway(config, _runway);
            for (var tick = 0; tick < 3000; tick++)
            {
                FlightModel.Step(config, plane, Script(tick, plane, config), _runway);
            }

            Assert.That(plane.Crashed, Is.False);
            Assert.That(Hash(plane), Is.EqualTo(1290656686u));
        }

        /// <summary>離陸してから上昇・旋回・横滑り・急降下を混ぜた決まった入力列。</summary>
        private static Controls Script(int tick, Aircraft plane, FlightConfig config)
        {
            if (plane.Grounded) { return new Controls(plane.Velocity.Length >= config.RotateSpeed ? Full : 0, 0, 0, Full); }

            // 低くなったら翼を水平に戻して上がる。
            if (plane.Position.Y < 400 * Units.PerMeter)
            {
                var bank = plane.Attitude.Right.Y;
                var level = Math.Abs(bank) < Fixed.One / 8;
                return new Controls(level ? Full : 0, bank > 0 ? Full : -Full, 0, Full);
            }

            var phase = tick / 200 % 6;
            return phase switch
            {
                0 => new Controls(Full / 2, 0, 0, Full),
                1 => new Controls(Full, Full, 0, 0),
                2 => new Controls(0, -Full / 3, Full, 0),
                3 => new Controls(-Full / 4, 0, -Full, -Full / 2),
                4 => new Controls(Full, -Full, 0, Full),
                _ => new Controls(0, Full / 5, 0, Full),
            };
        }

        private static Aircraft Probe(FlightConfig config, Aircraft plane, Controls controls)
        {
            var copy = plane.Clone();
            copy.Position = new Vec3l(copy.Position.X, 1000 * Units.PerMeter, copy.Position.Z);
            FlightModel.Step(config, copy, controls, _runway);
            return new Aircraft { Position = new Vec3l(0, plane.Position.Y + (copy.Position.Y - (1000 * Units.PerMeter)), 0) };
        }

        /// <summary>右翼が水平から上がった角度（左へ傾くと正）。</summary>
        private static int Trig(Vec3l right) => IntGeometry.Trig.Atan2(right.Y, right.X, 0);

        private static uint Hash(Aircraft plane)
        {
            var hash = StateHash.Start();
            plane.Mix(ref hash);
            return hash.Value;
        }
    }
}
