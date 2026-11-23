using System;
using System.Collections.Generic;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>
    /// 題材固有：弾道式の軌跡が期待値と一致する。
    /// 直進・等加速は閉じた式と整数でちょうど一致し、曲がる弾は円弧の式と 1 px 以内、置き場（SoA）は 1 発ずつの参照実装と一致する。
    /// </summary>
    public sealed class TrajectoryTests
    {
        private static readonly Vec2i _center = Px(192, 224);

        [TestCase(0, 2.0)]
        [TestCase(90, 1.5)]
        [TestCase(200.5, 3.25)]
        [TestCase(271, 0.75)]
        [TestCase(-33, 4.0)]
        public void Straight_MatchesClosedFormExactly(double degrees, double pxPerTick)
        {
            var spawn = Bullet(_center, Degrees(degrees), Speed(pxPerTick));
            var pool = new BulletPool(4);
            pool.Spawn(spawn);

            var cos = (long)Trig.Cos(spawn.Angle);
            var sin = (long)Trig.Sin(spawn.Angle);
            for (var t = 1; t <= 40; t++)
            {
                pool.Step();
                var x = ((long)_center.X << BulletPool.PositionShift) + (t * spawn.Speed * cos);
                var y = ((long)_center.Y << BulletPool.PositionShift) + (t * spawn.Speed * sin);
                var expected = new Vec2i((int)(x >> BulletPool.PositionShift), (int)(y >> BulletPool.PositionShift));
                Assert.That(pool.PositionOf(0), Is.EqualTo(expected), $"t = {t}");
            }
        }

        [Test]
        public void Straight_TravelsSpeedTimesTicksInPixels()
        {
            var pool = new BulletPool(1);
            pool.Spawn(Bullet(_center, Trig.Up, Speed(2.5)));
            for (var t = 0; t < 60; t++) { pool.Step(); }
            Assert.That(pool.PositionOf(0), Is.EqualTo(_center + Px(0, 150)));
        }

        /// <summary>速さ 1、加速 0.25、上限 2：1.25, 1.5, 1.75, 2, 2, … と進み、4 Tick 目から上限で頭打ちになる。</summary>
        [Test]
        public void Accelerating_MatchesArithmeticSeriesAndCapsAtMaxSpeed()
        {
            var spawn = Bullet(_center, Trig.Right, Speed(1), accel: Speed(0.25), maxSpeed: Speed(2));
            var pool = new BulletPool(1);
            pool.Spawn(spawn);

            long distance = 0;
            for (var t = 1; t <= 12; t++)
            {
                pool.Step();
                distance += Math.Min(spawn.Speed + (t * spawn.Accel), spawn.MaxSpeed);
                Assert.That(pool.SpeedOf(0), Is.EqualTo(Math.Min(Speed(1 + (0.25 * t)), Speed(2))), $"t = {t}");
                var x = ((long)_center.X << BulletPool.PositionShift) + (distance * Trig.One);
                Assert.That(pool.PositionOf(0).X, Is.EqualTo((int)(x >> BulletPool.PositionShift)), $"t = {t}");
            }

            // 閉じた式：3 Tick 目までは 1.25 + 1.5 + 1.75 = 4.5 px、以後は毎 Tick 2 px。
            Assert.That(pool.PositionOf(0).X, Is.EqualTo(_center.X + (int)((4.5 + (9 * 2)) * Field.Unit)));
        }

        [Test]
        public void Decelerating_StopsAtZeroAndStays()
        {
            var pool = new BulletPool(1);
            pool.Spawn(Bullet(_center, Trig.Down, Speed(1), accel: Speed(-0.25)));
            for (var t = 0; t < 3; t++) { pool.Step(); }

            // 0.75 + 0.5 + 0.25 = 1.5 px 進み、4 Tick 目に 0 になって止まる。
            Assert.That(pool.PositionOf(0), Is.EqualTo(_center - new Vec2i(0, Field.Unit * 3 / 2)));
            for (var t = 0; t < 30; t++) { pool.Step(); }
            Assert.That(pool.SpeedOf(0), Is.EqualTo(0));
            Assert.That(pool.PositionOf(0), Is.EqualTo(_center - new Vec2i(0, Field.Unit * 3 / 2)));
        }

        /// <summary>
        /// 一定の速さ v と角速度 ω で曲がる弾の t Tick 後の変位は Σ v (cos(θ0 + kω), sin(θ0 + kω))（k = 1..t）で、
        /// 閉じた式は v sin(tω/2) / sin(ω/2) × (cos, sin)(θ0 + (t+1)ω/2)。角度を表の刻みに合わせると 1 px 以内で一致する。
        /// </summary>
        // 円の半径 v / (2 sin(ω/2)) が 80〜100 px で、領域の中に収まる組。
        [TestCase(0, 2.0, 256)]
        [TestCase(1024 * 16, 3.0, -320)]
        [TestCase(37 * 16, 1.5, 160)]
        public void Curving_MatchesArcFormulaWithinOnePixel(int startAngle, double pxPerTick, int curve)
        {
            var spawn = Bullet(_center, startAngle, Speed(pxPerTick), curve: curve);
            var pool = new BulletPool(1);
            pool.Spawn(spawn);

            var theta0 = 2 * Math.PI * startAngle / Trig.FullTurn;
            var omega = 2 * Math.PI * curve / Trig.FullTurn;
            for (var t = 1; t <= 600; t++)
            {
                pool.Step();
                var chord = pxPerTick * Math.Sin(t * omega / 2) / Math.Sin(omega / 2);
                var mid = theta0 + ((t + 1) * omega / 2);
                var expectedX = (_center.X / (double)Field.Unit) + (chord * Math.Cos(mid));
                var expectedY = (_center.Y / (double)Field.Unit) + (chord * Math.Sin(mid));
                var actual = pool.PositionOf(0);
                Assert.That(actual.X / (double)Field.Unit, Is.EqualTo(expectedX).Within(1.0), $"t = {t}");
                Assert.That(actual.Y / (double)Field.Unit, Is.EqualTo(expectedY).Within(1.0), $"t = {t}");
            }
        }

        [Test]
        public void Curving_ReturnsToStartAfterFullCircle()
        {
            // 角速度 1/256 周で 256 Tick 回ると、ちょうど 1 周して出た位置に戻る（正多角形が閉じる）。
            var pool = new BulletPool(1);
            pool.Spawn(Bullet(_center, Trig.Up, Speed(2), curve: Trig.FullTurn / 256));
            for (var t = 0; t < 256; t++) { pool.Step(); }
            var back = pool.PositionOf(0) - _center;
            Assert.That(Math.Abs(back.X), Is.LessThanOrEqualTo(Field.Unit / 8));
            Assert.That(Math.Abs(back.Y), Is.LessThanOrEqualTo(Field.Unit / 8));
        }

        /// <summary>乱数で作った 400 発（直進・加速・減速・曲がる・寿命・領域外へ出る）を、置き場と参照実装の両方で 400 Tick 追う。</summary>
        [Test]
        public void Pool_MatchesReferenceBulletsIncludingRemovalOrder()
        {
            var random = new SeededRandom(2027);
            var pool = new BulletPool(400);
            var references = new List<(TestKit.ReferenceBullet Bullet, int Life, int Age)>();
            for (var i = 0; i < 400; i++)
            {
                var spawn = Bullet(
                    Px(random.Range(0, 384), random.Range(0, 448)),
                    random.Range(0, Trig.FullTurn - 1),
                    random.Range(0, Speed(4)),
                    accel: random.Range(-Speed(0.05), Speed(0.05)),
                    maxSpeed: Speed(6),
                    curve: random.Range(-Degrees(3), Degrees(3)),
                    life: random.Next(3) == 0 ? 0 : random.Range(1, 400));
                Assert.That(pool.Spawn(spawn), Is.True);
                references.Add((new TestKit.ReferenceBullet(spawn), spawn.Life, 0));
            }

            for (var t = 1; t <= 400; t++)
            {
                // 参照側も同じ規則で消す：寿命に達した弾と、前の Tick に余白の外へ出た弾。順序は保つ。
                references.RemoveAll(r => (r.Life > 0 && r.Age >= r.Life) || !Field.BulletBounds.Contains(r.Bullet.Position));
                pool.Step();
                for (var i = 0; i < references.Count; i++)
                {
                    references[i].Bullet.Step();
                    references[i] = (references[i].Bullet, references[i].Life, references[i].Age + 1);
                }

                Assert.That(pool.Count, Is.EqualTo(references.Count), $"t = {t}");
                for (var i = 0; i < references.Count; i++)
                {
                    Assert.That(pool.PositionOf(i), Is.EqualTo(references[i].Bullet.Position), $"t = {t}, #{i}");
                }
            }

            Assert.That(references.Count, Is.LessThan(400), "途中で消える弾を含む");
        }

        [Test]
        public void StartOf_IsPositionBeforeThisTicksMove()
        {
            var pool = new BulletPool(1);
            pool.Spawn(Bullet(_center, Trig.Right, Speed(3)));
            pool.Step();
            Assert.That(pool.StartOf(0), Is.EqualTo(_center), "出た Tick の移動は出た位置から始まる");
            Assert.That(pool.PositionOf(0), Is.EqualTo(_center + Px(3, 0)));
            pool.Step();
            Assert.That(pool.StartOf(0), Is.EqualTo(_center + Px(3, 0)));
            Assert.That(pool.PositionOf(0), Is.EqualTo(_center + Px(6, 0)));
        }
    }
}
