using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>敵の役割：攻撃役は 1 機、追い続けたら・撃ったら離脱する、舵の効きはプレイヤーより小さい。</summary>
    public sealed class EnemyRoleTests
    {
        private const long Altitude = 2000 * Units.PerMeter;

        private const long Cruise = 180L * Units.MetersPerSecond;

        /// <summary>プレイヤーは高度 2000 m を北へ。敵 1・2 は後ろ 600 m・800 m を同じ向きに飛び、交戦中。敵 3 は遠く。</summary>
        private static Mission TwoBehind(MissionConfig? config = null, bool pilots = false)
        {
            var mission = CombatTests.Behind(config);
            mission.EnemyPilots = pilots;
            var flight = mission.Flight;
            mission.Place(1, Aircraft.InFlight(flight, new Vec3l(0, Altitude, -600 * Units.PerMeter), 0, Cruise, 600));
            mission.Place(2, Aircraft.InFlight(flight, new Vec3l(30 * Units.PerMeter, Altitude, -800 * Units.PerMeter), 0, Cruise, 600));
            mission.FighterAt(1).Engaged = true;
            mission.FighterAt(2).Engaged = true;
            return mission;
        }

        [Test]
        public void OnlyTheAttackerLocksOn()
        {
            var mission = TwoBehind();
            mission.Attacker = 1;
            for (var tick = 0; tick < 30; tick++) { mission.Step(CombatTests.Fly()); }
            Assert.That(mission.Fighters[1].LockTicks, Is.EqualTo(30), "攻撃役はロックを進める");
            Assert.That(mission.Fighters[2].LockTicks, Is.Zero, "攻撃役でない敵は円錐に入れていてもロックしない");
        }

        [Test]
        public void TheAttackerBreaksOffAfterPursuingAndAnotherTakesOver()
        {
            var config = new MissionConfig { EnemyPursuitTicks = 120, EnemyBreakTicks = 90, EnemyLockTicks = 6000 };
            var mission = TwoBehind(config, pilots: true);
            mission.Step(CombatTests.Fly());
            var first = mission.Attacker;
            Assert.That(first, Is.InRange(1, 2));

            for (var tick = 1; tick < 120 && mission.Attacker == first; tick++) { mission.Step(CombatTests.Fly()); }
            Assert.That(mission.Attacker, Is.Not.EqualTo(first), "追い続けたら攻撃役を降りる");
            Assert.That(mission.Fighters[first].BreakTicks, Is.GreaterThan(0), "離脱する");
            Assert.That(mission.Fighters[first].LockTicks, Is.Zero);
            mission.Step(CombatTests.Fly());
            Assert.That(mission.Attacker, Is.EqualTo(3 - first), "次の Tick に、交戦中の別の敵が攻撃役になる");

            for (var tick = 0; tick < 89; tick++) { mission.Step(CombatTests.Fly()); }
            Assert.That(mission.Fighters[first].BreakTicks, Is.Zero, "離脱は決まった時間で終わる");
        }

        [Test]
        public void TheAttackerBreaksOffAfterFiring()
        {
            var config = new MissionConfig { EnemyLockTicks = 30, EnemyPursuitTicks = 36000, EnemyEvadeRange = 0 };
            var mission = TwoBehind(config, pilots: true);
            var fired = false;
            for (var tick = 0; tick < 600 && !fired; tick++)
            {
                var (_, moments) = mission.Step(CombatTests.Fly());
                fired = (moments & MissionMoment.EnemyFired) != 0;
            }

            Assert.That(fired, Is.True);
            var shooter = mission.Missiles[mission.Missiles.Count - 1].Owner;
            Assert.That(mission.Fighters[shooter].BreakTicks, Is.GreaterThan(0), "撃ったら離脱する");
            Assert.That(mission.Attacker, Is.Not.EqualTo(shooter));
        }

        [Test]
        public void NoEnemyIsTheAttackerUntilOneEngages()
        {
            var mission = new Mission(new FlightConfig(), new MissionConfig(), 1);
            for (var tick = 0; tick < 300; tick++) { mission.Step(new PlayerCommand(Controls.None, false, false)); }
            Assert.That(mission.Attacker, Is.EqualTo(-1), "地上のプレイヤーには誰も攻撃しない");
        }

        [Test]
        public void LowerAgilityTurnsTheEnemyMoreSlowly()
        {
            Assert.That(TurnAfter(25), Is.LessThan(TurnAfter(100) * 0.6), "舵の効きが小さいと、同じ時間で向きを変えられる量が小さい");
        }

        [Test]
        public void ConfigRejectsOutOfRangeEnemyBehavior()
        {
            var flight = new FlightConfig().Validate();
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MissionConfig { EnemyAgility = 0 }.Validate(flight));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MissionConfig { EnemyAgility = 101 }.Validate(flight));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MissionConfig { EnemyPursuitTicks = 0 }.Validate(flight));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new MissionConfig { EnemyBreakTicks = -1 }.Validate(flight));
        }

        /// <summary>真横（東）のプレイヤーへ向かう敵が、60 Tick で回った角度（度）。</summary>
        private static double TurnAfter(int agility)
        {
            var mission = CombatTests.Behind(new MissionConfig { EnemyAgility = agility, EnemyEvadeRange = 0 });
            mission.EnemyPilots = true;
            var flight = mission.Flight;
            mission.Place(1, Aircraft.InFlight(flight, new Vec3l(-1500 * Units.PerMeter, Altitude, 0), 0, Cruise, 600));
            mission.FighterAt(1).Engaged = true;
            mission.Attacker = 1;
            for (var tick = 0; tick < 60; tick++) { mission.Step(CombatTests.Fly()); }
            var forward = mission.Fighters[1].View.Attitude.Forward;
            return System.Math.Atan2(forward.X, forward.Z) * 180 / System.Math.PI;
        }
    }
}
