using System;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>誘導（<see cref="ProNav"/>）・運動（<see cref="MissileBody"/>）・近接（<see cref="Proximity"/>）を別々に確かめる。</summary>
    public sealed class GuidanceTests
    {
        private const long Mps = Units.MetersPerSecond;

        private const long Meter = Units.PerMeter;

        [Test]
        public void ProNavDoesNotTurnOnACollisionCourse()
        {
            var nav = new ProNav(4);

            // 視線が回らない：目標は真正面を同じ直線の上で遠ざかる / 一定の方位で近づく。
            Assert.That(nav.Command(Vec3l.Zero, new Vec3l(0, 0, 300 * Mps), new Vec3l(0, 0, 2000 * Meter), new Vec3l(0, 0, 200 * Mps)), Is.EqualTo(Vec3l.Zero));
            Assert.That(nav.Command(Vec3l.Zero, new Vec3l(0, 0, 300 * Mps), new Vec3l(1000 * Meter, 0, 1000 * Meter), new Vec3l(-150 * Mps, 0, 150 * Mps)), Is.EqualTo(Vec3l.Zero));
        }

        [Test]
        public void ProNavTurnsTowardTheSideTheTargetMovesTo()
        {
            var nav = new ProNav(4);
            var command = nav.Command(Vec3l.Zero, new Vec3l(0, 0, 300 * Mps), new Vec3l(0, 0, 2000 * Meter), new Vec3l(150 * Mps, 0, 0));
            Assert.That(command.X, Is.GreaterThan(0), "目標が右へ動けば右へ曲げる");
            Assert.That(command.Y, Is.Zero);
            Assert.That(command.Z, Is.Zero, "速度に直交（pure PN）");

            // 係数に比例する。
            var doubled = new ProNav(8).Command(Vec3l.Zero, new Vec3l(0, 0, 300 * Mps), new Vec3l(0, 0, 2000 * Meter), new Vec3l(150 * Mps, 0, 0));
            Assert.That(doubled.X, Is.EqualTo(command.X * 2).Within(2));
        }

        [Test]
        public void ProNavMatchesTheDoubleFormula()
        {
            var nav = new ProNav(4);
            var random = new SeededRandom(5);
            for (var i = 0; i < 500; i++)
            {
                var m = new Vec3l(random.Range(-3000, 3000) * Meter, random.Range(100, 3000) * Meter, random.Range(-3000, 3000) * Meter);
                var t = new Vec3l(random.Range(-3000, 3000) * Meter, random.Range(100, 3000) * Meter, random.Range(-3000, 3000) * Meter);
                var vm = new Vec3l(random.Range(-400, 400) * Mps, random.Range(-100, 100) * Mps, random.Range(-400, 400) * Mps);
                var vt = new Vec3l(random.Range(-200, 200) * Mps, random.Range(-50, 50) * Mps, random.Range(-200, 200) * Mps);
                var actual = nav.Command(m, vm, t, vt);

                // a = N (r × v) × V / |r|²（倍精度、m・m/Tick）
                double[] r = { (t.X - m.X) / 1e6, (t.Y - m.Y) / 1e6, (t.Z - m.Z) / 1e6 };
                double[] v = { (vt.X - vm.X) / 1e6, (vt.Y - vm.Y) / 1e6, (vt.Z - vm.Z) / 1e6 };
                double[] V = { vm.X / 1e6, vm.Y / 1e6, vm.Z / 1e6 };
                var w = Cross(r, v);
                var a = Cross(w, V);
                var r2 = (r[0] * r[0]) + (r[1] * r[1]) + (r[2] * r[2]);
                var scale = 4 * 1e6 / r2;
                Assert.That(actual.X, Is.EqualTo(a[0] * scale).Within(Math.Abs(a[0] * scale * 1e-3) + 20));
                Assert.That(actual.Y, Is.EqualTo(a[1] * scale).Within(Math.Abs(a[1] * scale * 1e-3) + 20));
                Assert.That(actual.Z, Is.EqualTo(a[2] * scale).Within(Math.Abs(a[2] * scale * 1e-3) + 20));
            }
        }

        [Test]
        public void BodyClampsTheTurnAndOnlyChangesSpeedByAcceleration()
        {
            var velocity = new Vec3l(0, 0, 300 * Mps);
            var maxTurn = 50_000L;
            var next = MissileBody.Step(velocity, new Vec3l(10_000_000, 0, 5_000_000), maxTurn, 1000, 400 * Mps);

            // 横向きの変化は上限まで、前向きの要求は捨てて速さは加速の分だけ増える。
            Assert.That(next.X, Is.EqualTo(maxTurn).Within(400));
            Assert.That(next.Length, Is.EqualTo(velocity.Length + 1000).Within(2));

            var capped = MissileBody.Step(new Vec3l(0, 0, 400 * Mps), Vec3l.Zero, maxTurn, 1000, 400 * Mps);
            Assert.That(capped, Is.EqualTo(new Vec3l(0, 0, 400 * Mps)), "最高速度で止まり、要求がなければ直進する");

            var huge = MissileBody.Step(velocity, new Vec3l(long.MaxValue / 4, long.MaxValue / 4, 0), maxTurn, 0, 400 * Mps);
            Assert.That(Math.Sqrt(((double)huge.X * huge.X) + ((double)huge.Y * huge.Y)), Is.LessThanOrEqualTo(maxTurn + 400), "巨大な要求でも上限まで");
        }

        [Test]
        public void ProximityIsStrictAndCatchesFastFlybys()
        {
            var r = 12 * Meter;

            // 静止した 2 点：ちょうど半径は近づかない、1 単位内側は近づく。
            Assert.That(Proximity.Within(Vec3l.Zero, Vec3l.Zero, new Vec3l(r, 0, 0), new Vec3l(r, 0, 0), r), Is.False);
            Assert.That(Proximity.Within(Vec3l.Zero, Vec3l.Zero, new Vec3l(r - 1, 0, 0), new Vec3l(r - 1, 0, 0), r), Is.True);

            // 1 Tick で 7 m 横を 400 m 飛び抜ける：始めも終わりも遠いが、途中で近づく。
            var a0 = new Vec3l(0, 0, -200 * Meter);
            var a1 = new Vec3l(0, 0, 200 * Meter);
            var b = new Vec3l(7 * Meter, 0, 0);
            Assert.That(Proximity.Within(a0, a1, b, b, r), Is.True);
            Assert.That(Proximity.Within(a0, a1, new Vec3l(r, 0, 0), new Vec3l(r, 0, 0), r), Is.False, "最も近い点でちょうど半径");

            // 最も近い時刻が Tick の外（もう離れていく・まだ遠い）。
            Assert.That(Proximity.Within(new Vec3l(0, 0, 100 * Meter), new Vec3l(0, 0, 300 * Meter), Vec3l.Zero, Vec3l.Zero, r), Is.False);
        }

        [Test]
        public void ProximityMatchesASubdividedReference()
        {
            var random = new SeededRandom(11);
            var r = 12 * Meter;
            for (var i = 0; i < 3000; i++)
            {
                var a0 = RandomPoint(random);
                var a1 = a0 + RandomStep(random);
                var b0 = a0 + new Vec3l(random.Range(-60, 60) * Meter, random.Range(-60, 60) * Meter, random.Range(-60, 60) * Meter);
                var b1 = b0 + RandomStep(random);
                var exact = Proximity.Within(a0, a1, b0, b1, r);

                // 倍精度で最小の距離を解析的に求める（境界の近くは除く）。
                var min = MinDistance(a0, a1, b0, b1);
                if (Math.Abs(min - r) < 1000) { continue; }
                Assert.That(exact, Is.EqualTo(min < r), $"case {i} min {min}");
            }
        }

        [Test]
        public void HardBreakDefeatsAnEnemyMissileThatHitsAStraightTarget()
        {
            // 6 G の敵のミサイルが 800 m 後ろから迫る。直進なら当たり、500 m で 90° 傾けて引けば外れる。
            Assert.That(Chase(breakAtMeters: -1), Is.True);
            Assert.That(Chase(breakAtMeters: 500), Is.False);
        }

        private static bool Chase(int breakAtMeters)
        {
            var flight = new FlightConfig().Validate();
            var config = new MissionConfig();
            var runway = new Runway(0, 0, 1, 1);
            var nav = new ProNav(config.NavGain);
            var plane = Aircraft.InFlight(flight, new Vec3l(0, 2000 * Meter, 0), 0, 180 * Mps, 1000);
            var missile = new Vec3l(0, 2000 * Meter, -800 * Meter);
            var velocity = new Vec3l(0, 0, 210 * Mps);
            var broke = false;
            for (var tick = 0; tick < config.MissileLife; tick++)
            {
                if (breakAtMeters > 0 && (missile - plane.Position).Length < breakAtMeters * Meter) { broke = true; }
                var controls = new Controls(0, 0, 0, Controls.Full);
                if (broke)
                {
                    var bank = Fixed.Wrap(Trig.Atan2(-plane.Attitude.Right.Y, plane.Attitude.Up.Y, 0));
                    var error = (Units.FullTurn / 4) - bank;
                    controls = new Controls(Math.Abs(error) < 4000 ? Controls.Full : 0, Math.Clamp(error / 8, -Controls.Full, Controls.Full), 0, Controls.Full);
                }

                var planeStart = plane.Position;
                FlightModel.Step(flight, plane, controls, runway);
                var start = missile;
                velocity = MissileBody.Step(velocity, nav.Command(missile, velocity, plane.Position, plane.Velocity), config.EnemyMissileTurn, config.MissileAccel, config.EnemyMissileSpeed);
                missile += velocity;
                if (Proximity.Within(start, missile, planeStart, plane.Position, config.FuseRadius)) { return true; }
            }

            return false;
        }

        private static Vec3l RandomPoint(SeededRandom random) =>
            new(random.Range(-5000, 5000) * Meter, random.Range(0, 5000) * Meter, random.Range(-5000, 5000) * Meter);

        private static Vec3l RandomStep(SeededRandom random) =>
            new(random.Range(-8000, 8000) * Units.PerMillimeter, random.Range(-8000, 8000) * Units.PerMillimeter, random.Range(-8000, 8000) * Units.PerMillimeter);

        private static double MinDistance(Vec3l a0, Vec3l a1, Vec3l b0, Vec3l b1)
        {
            double[] d = { b0.X - a0.X, b0.Y - a0.Y, b0.Z - a0.Z };
            double[] v = { (b1.X - b0.X) - (a1.X - a0.X), (b1.Y - b0.Y) - (a1.Y - a0.Y), (b1.Z - b0.Z) - (a1.Z - a0.Z) };
            var vv = (v[0] * v[0]) + (v[1] * v[1]) + (v[2] * v[2]);
            var t = vv == 0 ? 0 : Math.Clamp(-((d[0] * v[0]) + (d[1] * v[1]) + (d[2] * v[2])) / vv, 0, 1);
            var x = d[0] + (t * v[0]);
            var y = d[1] + (t * v[1]);
            var z = d[2] + (t * v[2]);
            return Math.Sqrt((x * x) + (y * y) + (z * z));
        }

        private static double[] Cross(double[] a, double[] b) => new[]
        {
            (a[1] * b[2]) - (a[2] * b[1]),
            (a[2] * b[0]) - (a[0] * b[2]),
            (a[0] * b[1]) - (a[1] * b[0]),
        };
    }
}
