using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>題材固有：移動方向と射撃方向を独立して指定できる。</summary>
    public sealed class AimTests
    {
        private static readonly int[] _directions = { -1, 0, 1 };

        /// <summary>移動 9 通り × 照準 8 方向のすべてで、自機は移動キーどおりに、弾は照準どおりに動く。</summary>
        [Test]
        public void MoveAndAim_AreIndependent_ForEveryCombination()
        {
            var config = Quiet();
            foreach (var mx in _directions)
            {
                foreach (var my in _directions)
                {
                    foreach (var ax in _directions)
                    {
                        foreach (var ay in _directions)
                        {
                            if (ax == 0 && ay == 0) { continue; }
                            var run = Run(config);
                            var start = run.Ship.Position;
                            var speed = mx != 0 && my != 0 ? SiegeConfig.DiagonalOf(config.ShipSpeed) : config.ShipSpeed;
                            var end = start + At(mx * speed, my * speed);
                            var aimOffset = At(ax * 16 * T, ay * 16 * T);

                            run.Step(Move(mx, my, end + aimOffset, fire: true));

                            var label = $"移動({mx},{my}) 照準({ax},{ay})";
                            Assert.That(run.Ship.Position, Is.EqualTo(end), label);
                            Assert.That(run.Bullets, Has.Count.EqualTo(1), label);
                            var expected = IntMath.Scale(aimOffset, config.BulletSpeed);
                            Assert.That(run.Bullets[0].Velocity, Is.EqualTo(expected), label);
                            Assert.That(run.Bullets[0].Position, Is.EqualTo(end + expected), label + "：撃った Tick にも進む");
                        }
                    }
                }
            }
        }

        [Test]
        public void MovingAwayWhileShootingBack_KeepsBothDirections()
        {
            var run = Run();
            for (var i = 0; i < 30; i++)
            {
                // 右へ逃げながら、常に左を撃つ。
                run.Step(Move(1, 0, run.Ship.Position + At(-10 * T, 0), fire: true));
            }

            Assert.That(run.Ship.Position.X, Is.GreaterThan(Center(run.Config).X));
            Assert.That(run.Bullets, Is.Not.Empty);
            foreach (var bullet in run.Bullets)
            {
                Assert.That(bullet.Velocity.X, Is.LessThan(0), "移動の向きに引きずられない");
            }
        }

        [Test]
        public void AimOnly_ChangesShotDirectionWithoutMoving()
        {
            var run = Run();
            var start = run.Ship.Position;
            run.Step(Move(0, 0, start + At(0, 4 * T)));
            Assert.That(run.Ship.ShotVelocity, Is.EqualTo(At(0, run.Config.BulletSpeed)));
            run.Step(Move(0, 0, start + At(-4 * T, 0)));
            Assert.That(run.Ship.ShotVelocity, Is.EqualTo(At(-run.Config.BulletSpeed, 0)));
            Assert.That(run.Ship.Position, Is.EqualTo(start));
        }

        [Test]
        public void AimPointOnShip_KeepsPreviousDirection()
        {
            var run = Run();
            run.Step(Move(0, 0, run.Ship.Position + At(0, -3 * T)));
            var previous = run.Ship.ShotVelocity;

            // 移動の後の位置にちょうど照準を重ねる。
            var speed = run.Config.ShipSpeed;
            run.Step(Move(1, 0, run.Ship.Position + At(speed, 0), fire: true));

            Assert.That(run.Ship.ShotVelocity, Is.EqualTo(previous));
            Assert.That(run.Bullets[0].Velocity, Is.EqualTo(previous));
        }

        [Test]
        public void HeldFire_ShootsEveryInterval()
        {
            var run = Run();
            var interval = run.Config.FireIntervalTicks;
            var fired = 0;
            for (var tick = 1; tick <= (interval * 3) + 1; tick++)
            {
                run.Step(Move(0, 0, run.Ship.Position + At(T, 0), fire: true));
                if ((run.TakeEvents() & RunEvents.Fired) != 0)
                {
                    fired++;
                    Assert.That((tick - 1) % interval, Is.Zero, $"{tick} Tick 目");
                }
            }

            Assert.That(fired, Is.EqualTo(4));
        }

        /// <summary>弾の数は寿命 ÷ 間隔の切り上げを超えない（理屈どおりに上限まで使い、超えない）。設定を乱択して確かめる。</summary>
        [Test]
        public void BulletCount_NeverExceedsCapacity_ForRandomValidConfigs()
        {
            var random = new SeededRandom(7);
            for (var trial = 0; trial < 200; trial++)
            {
                var interval = random.Range(1, 12);
                var config = Quiet(c =>
                {
                    c.FireIntervalTicks = interval;
                    c.BulletLifeTicks = random.Range(1, interval * SiegeConfig.MaxBulletsLimit);
                    c.BulletSpeed = random.Range(1, 12);
                });
                var run = Run(config);
                var max = 0;
                for (var i = 0; i < config.BulletLifeTicks + (interval * 3); i++)
                {
                    run.Step(Move(0, 0, run.Ship.Position + At(T, 0), fire: true));
                    max = Math.Max(max, run.Bullets.Count);
                }

                Assert.That(max, Is.EqualTo(config.BulletCapacity), $"寿命 {config.BulletLifeTicks}・間隔 {interval}");
                Assert.That(config.BulletCapacity, Is.LessThanOrEqualTo(SiegeConfig.MaxBulletsLimit));
            }
        }

        [Test]
        public void Bullet_LivesExactlyLifeTicks()
        {
            var run = Run(Quiet(c => c.BulletSpeed = 1));
            run.Step(Move(0, 0, run.Ship.Position + At(T, 0), fire: true));
            Wait(run, run.Config.BulletLifeTicks - 1);
            Assert.That(run.Bullets, Has.Count.EqualTo(1), "撃った Tick を含めて寿命の Tick 数まで残る");
            Wait(run, 1);
            Assert.That(run.Bullets, Is.Empty);
        }

        [Test]
        public void Bullet_LeavingArena_Disappears()
        {
            var run = Run();
            run.PlaceShip(At(run.Config.ArenaWidth - T, run.Config.ArenaHeight / 2));
            run.Step(Move(0, 0, run.Ship.Position + At(T, 0), fire: true));
            Wait(run, 3);
            Assert.That(run.Bullets, Is.Empty);
        }

        [TestCase(2, 0)]
        [TestCase(0, -2)]
        public void UndefinedMove_IsRejected(int x, int y)
        {
            Assert.That(() => new ShipCommand(x, y, false, default), Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
