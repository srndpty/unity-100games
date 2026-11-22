using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>敵 3 種の動きと、弾との当たり。</summary>
    public sealed class EnemyTests
    {
        [Test]
        public void Chaser_MovesTowardShipAtItsSpeed()
        {
            var run = Run();
            var start = run.Ship.Position + At(6 * T, 0);
            var chaser = run.Place(EnemyKind.Chaser, start);
            Wait(run, 1);
            Assert.That(chaser.Position, Is.EqualTo(start - At(run.Config.Chaser.Speed, 0)));
        }

        [Test]
        public void Bouncer_ReflectsAtArenaWall()
        {
            var run = Run();
            var config = run.Config;
            var radius = config.Bouncer.Radius;
            run.PlaceShip(At(config.ArenaWidth - (8 * T), config.ArenaHeight / 2));
            var bouncer = run.Place(EnemyKind.Bouncer, At(config.ArenaWidth - radius - 5, config.ArenaHeight / 2), At(20, 3));
            Wait(run, 1);
            Assert.That(bouncer.Position.X, Is.EqualTo(config.ArenaWidth - radius), "外周で止まる");
            Assert.That(bouncer.Velocity, Is.EqualTo(At(-20, 3)), "当たった軸だけ反転する");
            Wait(run, 1);
            Assert.That(bouncer.Position.X, Is.EqualTo(config.ArenaWidth - radius - 20));
        }

        [Test]
        public void Lancer_RestsAimsAndDashes_ForConfiguredTicks()
        {
            var run = Run();
            var config = run.Config;
            run.PlaceShip(At(10 * T, config.ArenaHeight / 2));
            var lancer = run.Place(EnemyKind.Lancer, At(22 * T, config.ArenaHeight / 2));

            Wait(run, config.LancerRestTicks);
            Assert.That(lancer.Mode, Is.EqualTo(LancerMode.Rest));
            Assert.That(lancer.Position.X, Is.EqualTo((22 * T) - (config.Lancer.Speed * config.LancerRestTicks)), "歩く間は自機へ近づく");

            Wait(run, 1);
            Assert.That(lancer.Mode, Is.EqualTo(LancerMode.Aim));
            Assert.That(lancer.Velocity, Is.EqualTo(At(-config.LancerDashSpeed, 0)), "狙い始めに自機の方向へ向く");
            var aimAt = lancer.Position;
            Wait(run, config.LancerAimTicks - 1);
            Assert.That(lancer.Mode, Is.EqualTo(LancerMode.Aim));
            Assert.That(lancer.Position, Is.EqualTo(aimAt), "狙う間は止まる");

            Wait(run, config.LancerDashTicks);
            Assert.That(lancer.Mode, Is.EqualTo(LancerMode.Dash));
            Assert.That(lancer.Position, Is.EqualTo(aimAt - At(config.LancerDashSpeed * config.LancerDashTicks, 0)));

            Wait(run, 1);
            Assert.That(lancer.Mode, Is.EqualTo(LancerMode.Rest));
        }

        [Test]
        public void Lancer_StopsDashAtWall()
        {
            var run = Run(Quiet(c => c.LancerDashTicks = 100));
            var config = run.Config;
            var radius = config.Lancer.Radius;
            run.PlaceShip(At(4 * T, config.ArenaHeight / 2));
            var lancer = run.Place(EnemyKind.Lancer, At(14 * T, config.ArenaHeight / 2));
            Wait(run, config.LancerRestTicks + 1);
            Assert.That(lancer.Mode, Is.EqualTo(LancerMode.Aim));

            // 狙いが決まった後に自機が通り道からどくと、突進は左の外周まで届いて止まる。
            run.PlaceShip(At(14 * T, config.ArenaHeight - T));
            Wait(run, config.LancerAimTicks);
            Assert.That(lancer.Mode, Is.EqualTo(LancerMode.Dash));
            var dashTicks = 1;
            while (lancer.Mode == LancerMode.Dash)
            {
                Wait(run, 1);
                dashTicks++;
            }

            Assert.That(run.Enemies, Has.Member(lancer));
            Assert.That(lancer.Position.X, Is.EqualTo(radius), "外周で止まる");
            Assert.That(dashTicks, Is.LessThan(config.LancerDashTicks), "外周に着いたら突進をやめる");
        }

        [Test]
        public void Bullet_DamagesLancerUntilDestroyed_AndScores()
        {
            var run = Run();
            var config = run.Config;
            var lancer = run.Place(EnemyKind.Lancer, run.Ship.Position + At(4 * T, 0));
            var hits = 0;
            for (var i = 0; i < 60 && run.Enemies.Count > 0; i++)
            {
                run.Step(Move(0, 0, lancer.Position, fire: true));
                if ((run.TakeEvents() & RunEvents.EnemyHit) != 0) { hits++; }
            }

            Assert.That(hits, Is.EqualTo(config.Lancer.Health));
            Assert.That(run.Enemies, Is.Empty);
            Assert.That(run.Score, Is.EqualTo(config.Lancer.Score));
            Assert.That(run.Kills, Is.EqualTo(1));
        }

        [Test]
        public void FastBullet_DoesNotPassThroughSmallEnemy()
        {
            var config = Quiet(c =>
            {
                c.BulletSpeed = 256;
                c.BulletRadius = 8;
                c.Bouncer.Radius = 16;
            });
            var run = Run(config);
            var target = run.Place(EnemyKind.Bouncer, run.Ship.Position + At(128, 0));
            run.Step(Move(0, 0, target.Position, fire: true));
            Assert.That(run.Enemies, Is.Empty, "端点はどちらも 128 離れているが、通り道で当たる");
            Assert.That(run.Bullets, Is.Empty);
        }

        [Test]
        public void Bullet_HitsOnlyFirstEnemyOnItsPath()
        {
            var config = Quiet(c =>
            {
                c.BulletSpeed = 256;
                c.Bouncer.Radius = 16;
            });
            var run = Run(config);
            var far = run.Place(EnemyKind.Bouncer, run.Ship.Position + At(220, 0));
            var near = run.Place(EnemyKind.Bouncer, run.Ship.Position + At(120, 0));
            run.Step(Move(0, 0, far.Position, fire: true));
            Assert.That(run.Enemies, Has.No.Member(near));
            Assert.That(run.Enemies, Has.Member(far), "1 発で倒すのは最初に触れた 1 体だけ");
        }
    }
}
