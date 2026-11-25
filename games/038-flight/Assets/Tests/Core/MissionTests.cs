using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class MissionTests
    {
        private static Mission New(MissionConfig? config = null, int seed = 1) => new(new FlightConfig(), config ?? new MissionConfig(), seed);

        [Test]
        public void MissionStartsOnTheRunwayWithThreeEnemiesInTheAir()
        {
            var mission = New();
            Assert.That(mission.Outcome, Is.EqualTo(Outcome.Running));
            Assert.That(mission.Fighters, Has.Count.EqualTo(4));
            Assert.That(mission.Player.View.Grounded, Is.True);
            Assert.That(mission.Runway.Contains(mission.Player.View.Position), Is.True);
            Assert.That(mission.EnemiesLeft, Is.EqualTo(3));
            for (var i = 1; i <= 3; i++)
            {
                var enemy = mission.Fighters[i].View;
                Assert.That(enemy.Grounded, Is.False);
                Assert.That(enemy.Position.Y, Is.GreaterThan(1000 * Units.PerMeter));
            }
        }

        [Test]
        public void DestroyingAllThreeEnemiesWins()
        {
            var mission = CombatTests.Behind();
            for (var i = 1; i <= 3; i++)
            {
                var target = mission.Fighters[i].View;
                mission.Launch(0, i, target.Position - new Vec3l(0, 0, 15 * Units.PerMeter), CombatTests.Toward(1));
            }

            var (_, moments) = mission.Step(CombatTests.Fly());
            Assert.That(mission.Outcome, Is.EqualTo(Outcome.Won));
            Assert.That(moments & MissionMoment.Finished, Is.EqualTo(MissionMoment.Finished));
            Assert.That(mission.FinishedTick, Is.EqualTo(mission.Tick));
        }

        [Test]
        public void LosingAndWinningInTheSameTickIsALoss()
        {
            var mission = CombatTests.Behind(new MissionConfig { PlayerHealth = 1 });
            for (var i = 1; i <= 3; i++)
            {
                var target = mission.Fighters[i].View;
                mission.Launch(0, i, target.Position - new Vec3l(0, 0, 15 * Units.PerMeter), CombatTests.Toward(1));
            }

            var player = mission.Player.View;
            mission.Launch(1, 0, player.Position + new Vec3l(0, 0, 15 * Units.PerMeter), CombatTests.Toward(-1));
            mission.Step(CombatTests.Fly());
            Assert.That(mission.Outcome, Is.EqualTo(Outcome.ShotDown), "勝ちはプレイヤーが残っているときだけ");
        }

        [Test]
        public void CrashingIntoTheGroundFails()
        {
            var mission = CombatTests.Behind();
            var flight = mission.Flight;
            var plane = Aircraft.InFlight(flight, new Vec3l(0, 50 * Units.PerMeter, 0), 0, 150L * Units.MetersPerSecond, 0);
            mission.Place(0, plane);
            while (!mission.IsOver) { mission.Step(new PlayerCommand(new Controls(-Controls.Full, 0, 0, 0), false, false)); }
            Assert.That(mission.Outcome, Is.EqualTo(Outcome.Crashed));
        }

        [Test]
        public void StayingOutOfTheAreaFailsOnlyAfterTheLimit()
        {
            var config = new MissionConfig { OutOfAreaTicks = 30 };
            var mission = CombatTests.Behind(config);
            var flight = mission.Flight;
            var edge = MissionConfig.Meters(config.ArenaHalfWidth);
            mission.Place(0, Aircraft.InFlight(flight, new Vec3l(edge + (100 * Units.PerMeter), 2000 * Units.PerMeter, 0), Units.FullTurn / 4, 150L * Units.MetersPerSecond, 600));
            for (var tick = 1; tick < config.OutOfAreaTicks; tick++)
            {
                mission.Step(CombatTests.Fly());
                Assert.That(mission.OutOfArea, Is.EqualTo(tick));
                Assert.That(mission.IsOver, Is.False);
            }

            mission.Step(CombatTests.Fly());
            Assert.That(mission.Outcome, Is.EqualTo(Outcome.OutOfArea));
        }

        [Test]
        public void ReturningToTheAreaResetsTheCount()
        {
            var config = new MissionConfig { OutOfAreaTicks = 30 };
            var mission = CombatTests.Behind(config);
            var flight = mission.Flight;
            var edge = MissionConfig.Meters(config.ArenaHalfWidth);

            // 東へ 1 Tick で境界を越える位置から、西向き（中へ）に飛ぶ。
            mission.Place(0, Aircraft.InFlight(flight, new Vec3l(edge + (20 * Units.PerMeter), 2000 * Units.PerMeter, 0), -Units.FullTurn / 4, 150L * Units.MetersPerSecond, 600));
            mission.Step(CombatTests.Fly());
            Assert.That(mission.OutOfArea, Is.EqualTo(1));
            for (var tick = 0; tick < 10; tick++) { mission.Step(CombatTests.Fly()); }
            Assert.That(mission.OutOfArea, Is.Zero);
        }

        [Test]
        public void TimeLimitEndsTheMission()
        {
            var mission = CombatTests.Behind(new MissionConfig { MissionTicks = 120 });
            for (var tick = 1; tick < 120; tick++) { mission.Step(CombatTests.Fly()); }
            Assert.That(mission.IsOver, Is.False);
            mission.Step(CombatTests.Fly());
            Assert.That(mission.Outcome, Is.EqualTo(Outcome.TimeUp));
            Assert.That(mission.Tick, Is.EqualTo(120));
        }

        [Test]
        public void StepsAfterTheEndDoNotChangeTheState()
        {
            var mission = CombatTests.Behind(new MissionConfig { MissionTicks = 60 });
            while (!mission.IsOver) { mission.Step(CombatTests.Fly()); }
            var hash = mission.Hash();
            for (var tick = 0; tick < 30; tick++) { mission.Step(new PlayerCommand(new Controls(Controls.Full, -Controls.Full, Controls.Full, Controls.Full), true, true)); }
            Assert.That(mission.Hash(), Is.EqualTo(hash));
            Assert.That(mission.Tick, Is.EqualTo(60));
        }

        [Test]
        public void InvalidControlsAreRejectedBeforeAnythingChanges()
        {
            var mission = New();
            var hash = mission.Hash();
            Assert.Throws<ArgumentOutOfRangeException>(() => mission.Step(new PlayerCommand(new Controls(Controls.Full + 1, 0, 0, 0), true, true)));
            Assert.That(mission.Hash(), Is.EqualTo(hash));
            Assert.That(mission.Tick, Is.Zero);
        }

        [Test]
        public void ConfigRejectsOutOfRangeValuesAndAShortRunway()
        {
            var flight = new FlightConfig().Validate();
            Assert.Throws<ArgumentOutOfRangeException>(() => new MissionConfig { LockTicks = 0 }.Validate(flight));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MissionConfig { FuseRadius = 0 }.Validate(flight));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MissionConfig { RunwayStartZ = 5000 }.Validate(flight), "滑走路が区域からはみ出す");
            Assert.Throws<ArgumentOutOfRangeException>(() => new MissionConfig { EnemyAltitude = 100 }.Validate(flight));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MissionConfig { MissileSpeed = flight.SpeedLimit - 1 }.Validate(flight), "ミサイルは機体より速い");
            Assert.Throws<ArgumentNullException>(() => new MissionConfig().Validate(null!));

            // 離陸の滑走は約 250 m。200 m の滑走路では離陸できない（飛行モデルを動かして確かめる）。
            Assert.Throws<ArgumentException>(() => new MissionConfig { RunwayLength = 200 }.Validate(flight));
            Assert.That(MissionConfig.TakeOffRoll(flight, new MissionConfig().Runway), Is.InRange(1, 600));
        }

        [Test]
        public void ConfigPropertiesAreCopies()
        {
            var mission = New();
            mission.Config.LockTicks = 1;
            mission.Flight.Thrust = 1;
            Assert.That(mission.Config.LockTicks, Is.EqualTo(new MissionConfig().LockTicks));
            Assert.That(mission.Flight.Thrust, Is.EqualTo(new FlightConfig().Thrust));
        }

        [Test]
        public void SameSeedGivesTheSameLayoutAndDifferentSeedsDiffer()
        {
            Assert.That(New(seed: 7).Hash(), Is.EqualTo(New(seed: 7).Hash()));
            Assert.That(New(seed: 7).Hash(), Is.Not.EqualTo(New(seed: 8).Hash()));
            Assert.That(New(seed: 7).Fighters[1].View.Position, Is.Not.EqualTo(New(seed: 8).Fighters[1].View.Position));
        }

        [Test]
        public void CloneIsIndependent()
        {
            var mission = CombatTests.Behind();
            CombatTests.Lock(mission);
            mission.Step(CombatTests.Fly(fire: true));
            var hash = mission.Hash();
            var copy = mission.Clone();
            Assert.That(copy.Hash(), Is.EqualTo(hash));
            for (var tick = 0; tick < 60; tick++) { copy.Step(new PlayerCommand(new Controls(Controls.Full, 0, 0, 0), false, false)); }
            Assert.That(mission.Hash(), Is.EqualTo(hash));
            Assert.That(copy.Missiles[0].Position, Is.Not.EqualTo(mission.Missiles[0].Position));
        }

        [Test]
        public void HashSeesLockProgressReloadAndTracking()
        {
            var a = CombatTests.Behind();
            CombatTests.Lock(a);
            var b = a.Clone();
            b.FighterAt(0).LockTicks--;
            Assert.That(b.Hash(), Is.Not.EqualTo(a.Hash()));

            b = a.Clone();
            b.FighterAt(0).Reload = 1;
            Assert.That(b.Hash(), Is.Not.EqualTo(a.Hash()));

            a.Step(CombatTests.Fly(fire: true));
            b = a.Clone();
            b.Missiles[0].Tracking = false;
            Assert.That(b.Hash(), Is.Not.EqualTo(a.Hash()));
        }

        [Test]
        public void EnemiesEngageOnlyAfterThePlayerIsAirborneAndClose()
        {
            var mission = New();
            for (var tick = 0; tick < 300; tick++) { mission.Step(new PlayerCommand(Controls.None, false, false)); }
            for (var i = 1; i <= 3; i++)
            {
                Assert.That(mission.Fighters[i].Engaged, Is.False, "地上のプレイヤーには向かってこない");
                Assert.That(mission.Fighters[i].Down, Is.False, "巡回中に墜落しない");
            }

            var close = CombatTests.Behind();
            close.EnemyPilots = true;
            close.Step(CombatTests.Fly());
            Assert.That(close.Fighters[1].Engaged, Is.True);
        }
    }
}
