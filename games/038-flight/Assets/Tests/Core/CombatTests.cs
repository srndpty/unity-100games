using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>ロックオン・発射の拒否・追尾の終了。敵の操縦を止め、目標を直進させて場面を作る。</summary>
    public sealed class CombatTests
    {
        private const long Altitude = 2000 * Units.PerMeter;

        private const long Cruise = 180L * Units.MetersPerSecond;

        /// <summary>プレイヤーは高度 2000 m で北へ、敵 1 は 600 m 前を同じ向き・速さで飛ぶ。敵 2・3 は遠くにいる。</summary>
        internal static Mission Behind(MissionConfig? config = null, long gap = 600)
        {
            var mission = new Mission(new FlightConfig(), config ?? new MissionConfig(), 1) { EnemyPilots = false };
            var flight = mission.Flight;
            mission.Place(0, Aircraft.InFlight(flight, new Vec3l(0, Altitude, 0), 0, Cruise, 600));
            mission.Place(1, Aircraft.InFlight(flight, new Vec3l(0, Altitude, gap * Units.PerMeter), 0, Cruise, 600));
            mission.Place(2, Aircraft.InFlight(flight, new Vec3l(-5000 * Units.PerMeter, Altitude, -5000 * Units.PerMeter), Units.FullTurn / 2, Cruise, 600));
            mission.Place(3, Aircraft.InFlight(flight, new Vec3l(5000 * Units.PerMeter, Altitude, -5000 * Units.PerMeter), Units.FullTurn / 2, Cruise, 600));
            return mission;
        }

        internal static PlayerCommand Fly(bool fire = false) => new(Controls.None, fire, false);

        /// <summary>最高速度で北（+1）か南（-1）へ飛ぶミサイルの速度。</summary>
        internal static Vec3l Toward(int north) => new(0, 0, north * new MissionConfig().MissileSpeed);

        // ---- ロックオンと発射の拒否 ----

        [Test]
        public void FireIsRejectedOneTickBeforeLockAndAcceptedOnLock()
        {
            var mission = Behind();
            var needed = mission.Config.LockTicks;
            for (var tick = 1; tick < needed; tick++)
            {
                mission.Step(Fly());
                Assert.That(mission.PlayerLocked, Is.False, $"tick {tick}");
            }

            // LOCK は Tick の終わりに決まり、発射は次の Tick の始めに判定する。
            var (early, _) = mission.Step(Fly(fire: true));
            Assert.That(early, Is.EqualTo(FireResult.NotLocked));
            Assert.That(mission.Player.Ammo, Is.EqualTo(mission.Config.Missiles));
            Assert.That(mission.PlayerLocked, Is.True);

            var (launched, moments) = mission.Step(Fly(fire: true));
            Assert.That(launched, Is.EqualTo(FireResult.Launched));
            Assert.That(moments & MissionMoment.PlayerFired, Is.EqualTo(MissionMoment.PlayerFired));
            Assert.That(mission.Player.Ammo, Is.EqualTo(mission.Config.Missiles - 1));
            Assert.That(mission.Missiles, Has.Count.EqualTo(1));
            Assert.That(mission.Missiles[0].Target, Is.EqualTo(1));
        }

        [Test]
        public void LeavingTheConeForOneTickDropsTheLockAndRejectsFire()
        {
            var mission = Behind();
            Lock(mission);

            // 敵 1 を機首から 30° 右へ置き直す（円錐は 12°）。
            var flight = mission.Flight;
            var side = 600 * Units.PerMeter;
            mission.Place(1, Aircraft.InFlight(flight, new Vec3l(Fixed.Round(Fixed.Sin(Units.Degrees(30)) * side), Altitude, Fixed.Round(Fixed.Cos(Units.Degrees(30)) * side)), 0, Cruise, 600));
            mission.Step(Fly());
            Assert.That(mission.PlayerLocked, Is.False);
            Assert.That(mission.Player.LockTicks, Is.Zero);

            var (fire, _) = mission.Step(Fly(fire: true));
            Assert.That(fire, Is.EqualTo(FireResult.NotLocked));
            Assert.That(mission.Missiles, Is.Empty);
        }

        [Test]
        public void TargetBeyondLockRangeIsNeverLocked()
        {
            var mission = Behind(gap: 2600);
            for (var tick = 0; tick < 120; tick++) { mission.Step(Fly()); }
            Assert.That(mission.Player.LockTarget, Is.EqualTo(-1));
            Assert.That(mission.Step(Fly(fire: true)).Fire, Is.EqualTo(FireResult.NotLocked));
        }

        [Test]
        public void StallingDropsTheLock()
        {
            var mission = Behind();
            Lock(mission);
            var flight = mission.Flight;
            var plane = Aircraft.InFlight(flight, new Vec3l(0, Altitude, 0), 0, flight.StallSpeed - Units.MetersPerSecond, 0);
            mission.Place(0, plane);
            mission.Step(Fly());
            Assert.That(mission.Player.View.Air.Stalled, Is.True);
            Assert.That(mission.Player.LockTicks, Is.Zero);
            Assert.That(mission.Step(Fly(fire: true)).Fire, Is.EqualTo(FireResult.NotLocked));
        }

        [Test]
        public void RejectedFireDoesNotChangeTheState()
        {
            AssertRejected(Behind(), FireResult.NotLocked, _ => { });
            AssertRejected(Behind(new MissionConfig { Missiles = 0 }), FireResult.NoAmmo, Lock);
            AssertRejected(Behind(), FireResult.Reloading, m =>
            {
                Lock(m);
                Assert.That(m.Step(Fly(fire: true)).Fire, Is.EqualTo(FireResult.Launched));
            });

            // 滑走路の上（初期配置のまま）。
            var grounded = new Mission(new FlightConfig(), new MissionConfig(), 1) { EnemyPilots = false };
            AssertRejected(grounded, FireResult.Grounded, _ => { });

            // 墜落した後。
            AssertRejected(Behind(), FireResult.Down, m => m.FighterAt(0).Down = true);
        }

        [Test]
        public void FireAfterTheMissionEndsIsRejected()
        {
            var mission = Behind(new MissionConfig { MissionTicks = 60 });
            while (!mission.IsOver) { mission.Step(Fly()); }
            var hash = mission.Hash();
            var (fire, moments) = mission.Step(new PlayerCommand(new Controls(Controls.Full, Controls.Full, 0, Controls.Full), true, true));
            Assert.That(fire, Is.EqualTo(FireResult.Finished));
            Assert.That(moments, Is.EqualTo(MissionMoment.None));
            Assert.That(mission.Hash(), Is.EqualTo(hash));
        }

        [Test]
        public void CycleTargetMovesToTheNextEnemyInTheConeAndRestartsTheCount()
        {
            var mission = Behind();
            var flight = mission.Flight;
            mission.Place(2, Aircraft.InFlight(flight, new Vec3l(40 * Units.PerMeter, Altitude, 800 * Units.PerMeter), 0, Cruise, 600));
            Lock(mission);
            Assert.That(mission.Player.LockTarget, Is.EqualTo(1), "最も機首に近い敵");

            mission.Step(new PlayerCommand(Controls.None, false, true));
            Assert.That(mission.Player.LockTarget, Is.EqualTo(2));
            Assert.That(mission.Player.LockTicks, Is.EqualTo(1), "切り替えた Tick から数え直す");

            mission.Step(new PlayerCommand(Controls.None, false, true));
            Assert.That(mission.Player.LockTarget, Is.EqualTo(1), "円錐の中の敵を順に回る（敵 3 は円錐の外）");
        }

        [Test]
        public void EnemiesLockOnlyInsideTheirOwnNarrowerCone()
        {
            // 敵 1 がプレイヤーの 600 m 後ろを同じ向きに飛ぶ。プレイヤーを横へずらして、敵の機首から 10°（敵の円錐 8° の外、プレイヤーの 12° の内）と 6° に置く。
            foreach (var (degrees, expected) in new[] { (10, false), (6, true) })
            {
                var mission = Behind();
                var flight = mission.Flight;
                var side = (long)Math.Round(600 * Math.Tan(degrees * Math.PI / 180) * Units.PerMeter);
                mission.Place(0, Aircraft.InFlight(flight, new Vec3l(side, Altitude, 0), 0, Cruise, 600));
                mission.Place(1, Aircraft.InFlight(flight, new Vec3l(0, Altitude, -600 * Units.PerMeter), 0, Cruise, 600));
                mission.FighterAt(1).Engaged = true;
                for (var tick = 0; tick < 10; tick++) { mission.Step(Fly()); }
                Assert.That(mission.Fighters[1].LockTicks > 0, Is.EqualTo(expected), $"{degrees}°");
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => new MissionConfig { EnemyLockCone = 0 }.Validate(new FlightConfig().Validate()));
        }

        // ---- 追尾の終了 ----

        [Test]
        public void MissileStopsTrackingWhenItsTargetIsShotDownByAnotherMissile()
        {
            var mission = Behind(new MissionConfig { Reload = 0 });
            Lock(mission);
            Assert.That(mission.Step(Fly(fire: true)).Fire, Is.EqualTo(FireResult.Launched));
            Assert.That(mission.Step(Fly(fire: true)).Fire, Is.EqualTo(FireResult.Launched));
            var first = mission.Missiles[0];
            var second = mission.Missiles[1];

            while (first.Active) { mission.Step(Fly()); }
            Assert.That(first.End, Is.EqualTo(MissileEnd.Hit));
            Assert.That(mission.Fighters[1].ShotDown, Is.True);
            Assert.That(second.Active, Is.True);
            Assert.That(second.Tracking, Is.False, "目標の撃墜で追尾をやめる");

            // 以後は向きを変えずに直進し（加速だけ）、他の敵へ乗り換えず、寿命で消える。
            var heading = second.Velocity;
            while (second.Active)
            {
                mission.Step(Fly());
                var v = second.Velocity;
                var cross = Guidance(heading, v);
                Assert.That(cross, Is.LessThan(1e-6), "向きが変わらない");
            }

            Assert.That(second.End, Is.EqualTo(MissileEnd.Expired));
            Assert.That(second.Age, Is.EqualTo(mission.Config.MissileLife));
            Assert.That(mission.Fighters[2].Down, Is.False);
            Assert.That(mission.Fighters[3].Down, Is.False);
        }

        [Test]
        public void TwoHitsInTheSameTickDestroyTheTargetOnce()
        {
            var mission = Behind();
            var target = mission.Fighters[1].View;

            // 2 発を目標の 15 m 後ろに置く。毎 Tick 4 m ずつ近づき、どちらも次の Tick に近接信管の半径（12 m）に入る。
            var start = target.Position - new Vec3l(0, 0, 15 * Units.PerMeter);
            var velocity = Toward(1);
            var a = mission.Launch(0, 1, start, velocity);
            var b = mission.Launch(0, 1, start, velocity);

            var (_, moments) = mission.Step(Fly());
            Assert.That(moments & MissionMoment.EnemyDown, Is.EqualTo(MissionMoment.EnemyDown));
            Assert.That(a.End, Is.EqualTo(MissileEnd.Hit), "発射の順に処理する");
            Assert.That(b.End, Is.EqualTo(MissileEnd.None), "撃墜済みの機体には当たらない");
            Assert.That(b.Tracking, Is.False);
            Assert.That(mission.Fighters[1].Health, Is.Zero);
            Assert.That(mission.EnemiesLeft, Is.EqualTo(2));
        }

        [Test]
        public void PlayerSurvivesHitsUntilHealthRunsOut()
        {
            var mission = Behind(new MissionConfig { PlayerHealth = 2 });
            var player = mission.Player.View;
            for (var hit = 1; hit <= 2; hit++)
            {
                mission.Launch(1, 0, player.Position + new Vec3l(0, 0, 15 * Units.PerMeter), Toward(-1));
                var (_, moments) = mission.Step(Fly());
                Assert.That(moments & MissionMoment.PlayerHit, Is.EqualTo(MissionMoment.PlayerHit));
                Assert.That(mission.Player.Health, Is.EqualTo(2 - hit));
            }

            Assert.That(mission.Outcome, Is.EqualTo(Outcome.ShotDown));
        }

        // ---- 共通 ----

        internal static void Lock(Mission mission)
        {
            for (var guard = 0; guard < 600 && !mission.PlayerLocked; guard++) { mission.Step(Fly()); }
            Assert.That(mission.PlayerLocked, Is.True);
        }

        /// <summary>発射を要求した 1 Tick と、要求しなかった 1 Tick で、ミッションの状態（ハッシュ）が同じ。</summary>
        private static void AssertRejected(Mission mission, FireResult expected, Action<Mission> prepare)
        {
            prepare(mission);
            var twin = mission.Clone();
            var (fire, _) = mission.Step(Fly(fire: true));
            twin.Step(Fly());
            Assert.That(fire, Is.EqualTo(expected));
            Assert.That(mission.Hash(), Is.EqualTo(twin.Hash()), expected.ToString());
        }

        /// <summary>2 つの速度の向きの差（1 - cos）。</summary>
        private static double Guidance(Vec3l a, Vec3l b)
        {
            double dot = ((double)a.X * b.X) + ((double)a.Y * b.Y) + ((double)a.Z * b.Z);
            var la = Math.Sqrt(((double)a.X * a.X) + ((double)a.Y * a.Y) + ((double)a.Z * a.Z));
            var lb = Math.Sqrt(((double)b.X * b.X) + ((double)b.Y * b.Y) + ((double)b.Z * b.Z));
            return 1 - (dot / (la * lb));
        }
    }
}
