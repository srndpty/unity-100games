using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>題材固有：無敵時間中の被弾は無効。</summary>
    public sealed class InvincibilityTests
    {
        private static Enemy Blocker(SiegeRun run) => run.Place(EnemyKind.Bouncer, run.Ship.Position);

        [Test]
        public void Contact_CostsALifeAndStartsInvincibility()
        {
            var run = Run();
            var enemy = Blocker(run);
            run.Step(Idle(run));

            Assert.That(run.TakeEvents() & RunEvents.Hurt, Is.EqualTo(RunEvents.Hurt));
            Assert.That(run.Ship.Lives, Is.EqualTo(run.Config.Lives - 1));
            Assert.That(run.Ship.IsInvincible, Is.True);
            Assert.That(run.Enemies, Has.No.Member(enemy), "触れた敵は消える");
            Assert.That(run.Score, Is.Zero, "被弾で消えた敵は得点にならない");
        }

        /// <summary>被弾した Tick を h とすると h〜h+N-1 は無敵、h+N から当たる（N = InvincibleTicks）。</summary>
        [Test]
        public void Invincibility_LastsExactlyConfiguredTicks()
        {
            var run = Run();
            var n = run.Config.InvincibleTicks;
            Blocker(run);
            run.Step(Idle(run));
            Assert.That(run.Elapsed, Is.EqualTo(1));
            var lives = run.Ship.Lives;

            Wait(run, n - 2);
            Blocker(run);
            run.Step(Idle(run));
            Assert.That(run.Elapsed, Is.EqualTo(n), "無敵の最後の Tick");
            Assert.That(run.Ship.Lives, Is.EqualTo(lives), "最後の Tick までは触れても減らない");
            Assert.That(run.Enemies, Has.Count.EqualTo(1), "無敵中に触れた敵は消えない");

            run.Step(Idle(run));
            Assert.That(run.Elapsed, Is.EqualTo(n + 1));
            Assert.That(run.Ship.Lives, Is.EqualTo(lives - 1), "切れた直後の Tick に当たる");
        }

        [Test]
        public void ManyContactsDuringInvincibility_AreIgnored()
        {
            var run = Run(Quiet(c => c.Lives = 9));
            Blocker(run);
            run.Step(Idle(run));
            var lives = run.Ship.Lives;
            for (var i = 0; i < 5; i++)
            {
                run.Place(EnemyKind.Chaser, run.Ship.Position);
                run.Place(EnemyKind.Lancer, run.Ship.Position);
            }

            Wait(run, run.Config.InvincibleTicks - 2);
            Assert.That(run.Ship.Lives, Is.EqualTo(lives));
        }

        [Test]
        public void TwoEnemiesInOneTick_CostOnlyOneLife()
        {
            var run = Run();
            run.Place(EnemyKind.Bouncer, run.Ship.Position + At(10, 0));
            run.Place(EnemyKind.Bouncer, run.Ship.Position - At(10, 0));
            run.Step(Idle(run));
            Assert.That(run.Ship.Lives, Is.EqualTo(run.Config.Lives - 1));
            Assert.That(run.Enemies, Has.Count.EqualTo(1), "先に触れた 1 体だけが消える");
        }

        [Test]
        public void SweptContact_HitsShipPassingThroughEnemy()
        {
            // 速く動く自機が、端点では重ならない小さな敵を通り抜ける。
            var config = Quiet(c =>
            {
                c.ShipSpeed = 200;
                c.ShipRadius = 8;
                c.Bouncer.Radius = 8;
            });
            var run = Run(config);
            run.Place(EnemyKind.Bouncer, run.Ship.Position + At(100, 0));
            run.Step(Move(1, 0, run.Ship.Position + At(T, T)));
            Assert.That(run.Ship.Lives, Is.EqualTo(config.Lives - 1));
        }

        [Test]
        public void LosingLastLife_EndsRun()
        {
            var run = Run(Quiet(c => c.Lives = 1));
            Blocker(run);
            run.Step(Idle(run));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Lost));
            Assert.That(run.TakeEvents() & RunEvents.Lost, Is.EqualTo(RunEvents.Lost));
        }
    }
}
