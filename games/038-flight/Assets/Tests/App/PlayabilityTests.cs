using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>
    /// 人が遊べる難しさか：キーだけで押す操縦役（0.2 秒遅れ）で多くのシードを通し、敵に後ろを取られた場面から反撃できるかを確かめる。
    /// 操縦役（<see cref="Autopilot"/>）は正面から先に撃ち落とすので、敵に後ろを取られる場面を通らない。
    /// </summary>
    public sealed class PlayabilityTests
    {
        private const int Delay = 12;

        private const long Altitude = 2000 * Units.PerMeter;

        [Test]
        public void AKeyboardOnlyPilotWithReactionDelayWinsMostSeeds()
        {
            var wins = 0;
            var enemyShots = 0;
            for (var seed = 1; seed <= 20; seed++)
            {
                var mission = new Mission(new FlightConfig(), new MissionConfig(), seed);
                var pilot = new KeyboardPilot(Delay);
                while (!mission.IsOver)
                {
                    var (_, moments) = mission.Step(pilot.CommandFor(mission));
                    if ((moments & MissionMoment.EnemyFired) != 0) { enemyShots++; }
                }

                if (mission.Outcome == Outcome.Won) { wins++; }
            }

            // 調整時の実測は 40 シード全勝、敵の発射は 1 試合 0.1 発未満。
            Assert.That(wins, Is.GreaterThanOrEqualTo(18), "キーだけでもほとんどのシードで勝てる");
            Assert.That(enemyShots, Is.LessThanOrEqualTo(10));
        }

        [Test]
        public void WithAnEnemyOnTheTailTheAssistedTurnAvoidsFireAndTurnsTheTables()
        {
            var shots = 0;
            var locked = 0;
            var killed = 0;
            long warned = 0;
            long ticks = 0;
            for (var seed = 1; seed <= 20; seed++)
            {
                var mission = Tailed(seed);
                var pilot = new KeyboardPilot(Delay);
                var gotLock = false;
                for (var tick = 0; tick < 3600 && !mission.IsOver; tick++)
                {
                    var (_, moments) = mission.Step(pilot.CommandFor(mission));
                    if ((moments & MissionMoment.EnemyFired) != 0) { shots++; }
                    if (mission.Fighters[1].LockTarget == 0 && mission.Fighters[1].LockTicks > 0) { warned++; }
                    ticks++;
                    gotLock |= mission.PlayerLocked;
                }

                if (gotLock) { locked++; }
                if (mission.Fighters[1].Down) { killed++; }
            }

            // 調整時の実測（60 秒）：敵の発射 2 発（20 試合の合計）、狙われている時間 5%、LOCK 11/20、撃墜 10/20。
            // 調整前は、真っすぐ逃げると 30 秒で 2.2 発撃たれ、急旋回を続けても後ろの敵を前に出せなかった（0/20）。
            Assert.That(shots, Is.LessThanOrEqualTo(6), "急旋回を続ければほとんど撃たれない");
            Assert.That(warned * 100 / ticks, Is.LessThanOrEqualTo(15), "狙われている時間が短い");
            Assert.That(locked, Is.GreaterThanOrEqualTo(8), "後ろの敵を前に回り込ませて LOCK できる");
            Assert.That(killed, Is.GreaterThanOrEqualTo(6));
        }

        [Test]
        public void HoldingATurnKeyBanksTurnsAndHoldsAltitude()
        {
            var flight = new FlightConfig().Validate();
            var plane = Aircraft.InFlight(flight, new Vec3l(0, Altitude, 0), 0, 180L * Units.MetersPerSecond, 1000);
            var runway = new Runway(0, 0, 1, 1);
            for (var tick = 0; tick < 300; tick++)
            {
                FlightModel.Step(flight, plane, TurnAssist.Apply(flight, new AircraftView(plane), new Controls(0, Controls.Full, 0, Controls.Full)), runway);
            }

            var view = new AircraftView(plane);
            var forward = view.Attitude.Forward;
            var heading = Math.Atan2(forward.X, forward.Z) * 180 / Math.PI;
            Assert.That(heading, Is.GreaterThan(60).Or.LessThan(-120), "右へ 5 秒で 60° 以上曲がる（180° を超えて負になってもよい）");
            Assert.That(Math.Abs(TurnAssist.BankOf(view.Attitude)), Is.InRange(Units.Degrees(55), Units.Degrees(110)));
            Assert.That(Math.Abs(view.Position.Y - Altitude), Is.LessThan(80 * Units.PerMeter), "高度をほぼ保つ");
            Assert.That(view.Air.Stalled, Is.False);

            // 離すと翼が水平に戻る。
            for (var tick = 0; tick < 120; tick++)
            {
                FlightModel.Step(flight, plane, TurnAssist.Apply(flight, new AircraftView(plane), new Controls(0, 0, 0, Controls.Full)), runway);
            }

            Assert.That(Math.Abs(TurnAssist.BankOf(plane.Attitude)), Is.LessThan(Units.Degrees(5)));
        }

        [Test]
        public void PitchKeysAloneDoNotRollAndTheGroundIsNotAssisted()
        {
            var flight = new FlightConfig().Validate();
            var banked = Aircraft.InFlight(flight, new Vec3l(0, Altitude, 0), 0, 180L * Units.MetersPerSecond, 1000);
            banked.Attitude = banked.Attitude.RotateBody(0, 0, -Units.Degrees(40));
            var pull = new Controls(Controls.Full, 0, 0, 0);
            Assert.That(TurnAssist.Apply(flight, new AircraftView(banked), pull), Is.EqualTo(pull), "上下だけなら傾きを変えない（宙返りできる）");

            var grounded = Aircraft.OnRunway(flight, new Runway(0, 0, 1500 * Units.PerMeter, 25 * Units.PerMeter));
            var keys = new Controls(0, Controls.Full, 0, Controls.Full);
            Assert.That(TurnAssist.Apply(flight, new AircraftView(grounded), keys), Is.EqualTo(keys));
        }

        [Test]
        public void TheAssistStopsPullingNearTheStall()
        {
            var flight = new FlightConfig().Validate();
            var plane = Aircraft.InFlight(flight, new Vec3l(0, Altitude, 0), 0, 180L * Units.MetersPerSecond, 1000);
            // 80° 傾けてから、機体の軸で機首を失速角まで上げる（速度は水平の北向きのまま）。
            plane.Attitude = Quat.Identity.RotateBody(0, 0, -Units.Degrees(80)).RotateBody(-flight.StallAngle, 0, 0);
            plane.Air = AirData.Of(flight, plane.Velocity, plane.Attitude, false);
            var command = TurnAssist.Apply(flight, new AircraftView(plane), new Controls(0, Controls.Full, 0, 0));
            Assert.That(plane.Air.Alpha, Is.GreaterThan(flight.StallAngle * 3 / 4));
            Assert.That(command.Pitch, Is.Zero);
        }

        /// <summary>プレイヤーは高度 2000 m を北へ 180 m/s。攻撃役の敵 1 が 600 m 後ろ。敵 2・3 は遠く。</summary>
        private static Mission Tailed(int seed)
        {
            var mission = new Mission(new FlightConfig(), new MissionConfig(), seed);
            var flight = mission.Flight;
            var speed = 180L * Units.MetersPerSecond;
            mission.Place(0, Aircraft.InFlight(flight, new Vec3l(0, Altitude, 0), 0, speed, 1000));
            mission.Place(1, Aircraft.InFlight(flight, new Vec3l(0, Altitude, -600 * Units.PerMeter), 0, speed, 1000));
            mission.Place(2, Aircraft.InFlight(flight, new Vec3l(-5000 * Units.PerMeter, 2500 * Units.PerMeter, 5000 * Units.PerMeter), Units.FullTurn / 4, 150L * Units.MetersPerSecond, 600));
            mission.Place(3, Aircraft.InFlight(flight, new Vec3l(5000 * Units.PerMeter, 2500 * Units.PerMeter, 5000 * Units.PerMeter), -Units.FullTurn / 4, 150L * Units.MetersPerSecond, 600));
            mission.FighterAt(1).Engaged = true;
            return mission;
        }
    }
}
