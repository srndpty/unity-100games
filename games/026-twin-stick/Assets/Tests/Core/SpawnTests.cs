using System;
using System.Linq;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>出現の制御（スポーンレート・密度の上限・予告・安全距離）と、題材固有：画面外の敵が累積しない。</summary>
    public sealed class SpawnTests
    {
        /// <summary>敵が自機に届かない設定（遅い Bouncer だけ・遠くに出す）。</summary>
        private static SiegeConfig Slow(Action<SiegeConfig>? edit = null)
        {
            var config = new SiegeConfig
            {
                Bouncer = new EnemyStats { Radius = 104, Speed = 1, Health = 1, Score = 20 },
                SafeRadius = 6 * T,
                OffscreenLimitTicks = SiegeConfig.MaxStatusTicks,
                MaxAlive = SiegeConfig.MaxAliveLimit,
            };
            edit?.Invoke(config);
            return config;
        }

        private static SpawnStage Stage(int interval, int chaser = 0, int bouncer = 1, int lancer = 0) =>
            new() { IntervalTicks = interval, ChaserWeight = chaser, BouncerWeight = bouncer, LancerWeight = lancer };

        [Test]
        public void SpawnCount_FollowsStageTable()
        {
            var config = Slow(c =>
            {
                c.DurationTicks = 200;
                c.StageTicks = 100;
                c.Stages = new[] { Stage(10), Stage(25) };
            });
            var run = Run(config);
            var perStage = new int[2];
            while (!run.IsOver)
            {
                run.Step(Idle(run));
                if ((run.TakeEvents() & RunEvents.EnemyWarned) != 0) { perStage[(run.Elapsed - 1) / 100]++; }
            }

            Assert.That(perStage[0], Is.EqualTo(10));
            Assert.That(perStage[1], Is.EqualTo(4));
            Assert.That(run.Spawned, Is.EqualTo(14));
        }

        [Test]
        public void KindWeights_AreRespected()
        {
            var config = Slow(c =>
            {
                c.DurationTicks = 2000;
                c.StageTicks = 1000;
                c.Stages = new[] { Stage(20, chaser: 0, bouncer: 1, lancer: 0), Stage(20, chaser: 0, bouncer: 0, lancer: 1) };
                c.Lancer = new EnemyStats { Radius = 120, Speed = 1, Health = 3, Score = 50 };
                c.LancerDashSpeed = 1;
                c.Lives = 9;
            });
            var run = Run(config);
            Wait(run, 1000);
            Assert.That(run.Enemies.All(e => e.Kind == EnemyKind.Bouncer), Is.True);
            Wait(run, 1000);
            Assert.That(run.Enemies.Any(e => e.Kind == EnemyKind.Lancer), Is.True);
        }

        [Test]
        public void AliveCount_NeverExceedsCap_AndWarningsCount()
        {
            var config = Slow(c =>
            {
                c.MaxAlive = 5;
                c.Stages = new[] { Stage(1) };
                c.StageTicks = c.DurationTicks;
            });
            var run = Run(config);
            for (var i = 0; i < 300; i++)
            {
                run.Step(Idle(run));
                Assert.That(run.Enemies.Count, Is.LessThanOrEqualTo(5));
                if (run.Elapsed == 5)
                {
                    Assert.That(run.Enemies, Has.Count.EqualTo(5));
                    Assert.That(run.Enemies.All(e => !e.IsActive), Is.True, "予告中も上限に数える");
                }
            }
        }

        /// <summary>上限に張り付いている間に予算を貯めない：空きができた Tick に 1 体出た後は、次の間隔まで出ない。</summary>
        [Test]
        public void Budget_IsCappedWhileAtLimit()
        {
            var config = Slow(c =>
            {
                c.MaxAlive = 3;
                c.InvincibleTicks = 1;
                c.Lives = 9;
                c.SpawnWarnTicks = 1;
                c.Stages = new[] { Stage(10) };
                c.StageTicks = c.DurationTicks;
            });
            var run = Run(config);
            Wait(run, 200);
            Assert.That(run.Enemies, Has.Count.EqualTo(3));
            var spawned = run.Spawned;

            // 自機を敵に重ねて 1 体消す（被弾）。予算は満ちているので同じ Tick に 1 体出る。
            run.PlaceShip(run.Enemies[0].Position);
            run.Step(Idle(run));
            Assert.That(run.TakeEvents() & RunEvents.Hurt, Is.EqualTo(RunEvents.Hurt));
            Assert.That(run.Spawned, Is.EqualTo(spawned + 1));
            Assert.That(run.Enemies, Has.Count.EqualTo(3));

            // 次の Tick にもう 1 体消しても、間隔が貯まるまで補充しない。
            run.PlaceShip(run.Enemies[0].Position);
            run.Step(Idle(run));
            Assert.That(run.TakeEvents() & RunEvents.Hurt, Is.EqualTo(RunEvents.Hurt));
            for (var i = 0; i < 9; i++)
            {
                Assert.That(run.Enemies, Has.Count.EqualTo(2), $"空いてから {i + 1} Tick 目");
                run.Step(Idle(run));
            }

            Assert.That(run.Enemies, Has.Count.EqualTo(3), "間隔の 10 Tick で 1 体出る");
            Assert.That(run.Spawned, Is.EqualTo(spawned + 2));
        }

        [Test]
        public void SpawnPosition_IsInViewAndOutsideSafeRadius_ForManySeeds()
        {
            for (var seed = 1; seed <= 20; seed++)
            {
                var config = Config();
                var run = new SiegeRun(config, seed);
                var known = 0;
                var safe = (long)config.SafeRadius * config.SafeRadius;
                for (var i = 0; i < 1500 && !run.IsOver; i++)
                {
                    // 自機をばらばらに動かす（隅にも寄る）。
                    var mx = ((i / 40) % 3) - 1;
                    var my = ((i / 70) % 3) - 1;
                    run.Step(Move(mx, my, run.Ship.Position + At(T, 0)));
                    foreach (var enemy in run.Enemies.Where(e => e.Id > known))
                    {
                        Assert.That(enemy.IsActive, Is.False, "新しい敵は予告から始まる");
                        Assert.That(Vec2i.DistanceSquared(enemy.Position, run.Ship.Position), Is.GreaterThanOrEqualTo(safe), $"シード {seed}");
                        Assert.That(run.View.Overlaps(enemy.Position, enemy.Radius), Is.True, $"シード {seed}");
                    }

                    known = run.Enemies.Count == 0 ? known : Math.Max(known, run.Enemies.Max(e => e.Id));
                }
            }
        }

        [Test]
        public void Warning_LastsConfiguredTicks_AndCannotBeHitOrHurt()
        {
            var config = Slow(c =>
            {
                c.DurationTicks = 600;
                c.StageTicks = 600;
                c.Stages = new[] { Stage(1) };
                c.MaxAlive = 1;
            });
            var run = Run(config);
            run.Step(Idle(run));
            var enemy = run.Enemies.Single();
            Assert.That(enemy.IsActive, Is.False);

            // 予告中の敵に自機を重ね、敵へ向けて撃っても、何も起きない。
            run.PlaceShip(enemy.Position);
            for (var i = 1; i < config.SpawnWarnTicks; i++)
            {
                run.Step(Move(0, 0, enemy.Position + At(T, 0), fire: true));
                Assert.That(enemy.IsActive, Is.False, $"出現から {i + 1} Tick 目");
                Assert.That(run.TakeEvents() & (RunEvents.Hurt | RunEvents.EnemyHit), Is.EqualTo(RunEvents.None));
            }

            run.Step(Idle(run));
            Assert.That(enemy.IsActive, Is.True, "予告の Tick 数（出現した Tick を含む）の次の Tick から動く");
            Assert.That(run.TakeEvents() & RunEvents.Hurt, Is.EqualTo(RunEvents.Hurt), "実体化したら当たる");
        }

        /// <summary>題材固有：画面外の敵は一定 Tick で消え、溜まらない。</summary>
        [Test]
        public void OffscreenEnemy_IsCulledAtLimit_WithoutScore()
        {
            var config = Quiet(c => c.OffscreenLimitTicks = 30);
            var run = Run(config);
            run.PlaceShip(At(2 * T, 2 * T));
            var far = run.Place(EnemyKind.Bouncer, At(config.ArenaWidth - (2 * T), config.ArenaHeight - (2 * T)));
            Assert.That(run.View.Overlaps(far.Position, far.Radius), Is.False);

            Wait(run, 29);
            Assert.That(run.Enemies, Has.Member(far));
            Assert.That(far.OffscreenTicks, Is.EqualTo(29));
            var events = Wait(run, 1);
            Assert.That(run.Enemies, Is.Empty);
            Assert.That(events & RunEvents.EnemyCulled, Is.EqualTo(RunEvents.EnemyCulled));
            Assert.That(run.Score, Is.Zero);
        }

        [Test]
        public void ReturningIntoView_ResetsOffscreenCount()
        {
            var config = Quiet(c => c.OffscreenLimitTicks = 30);
            var run = Run(config);
            run.PlaceShip(At(2 * T, 2 * T));
            var far = run.Place(EnemyKind.Bouncer, At(config.ArenaWidth - (2 * T), config.ArenaHeight - (2 * T)));
            Wait(run, 20);
            run.PlaceShip(far.Position - At(6 * T, 0));
            Wait(run, 1);
            Assert.That(far.OffscreenTicks, Is.Zero);
        }

        /// <summary>自機が隅にとどまっても、画面外の敵は上限 Tick を超えて残らず、生存数は上限以下のまま。</summary>
        [Test]
        public void OffscreenEnemies_DoNotAccumulate_WhenShipCamps()
        {
            for (var seed = 1; seed <= 5; seed++)
            {
                var config = Config();
                config.Lives = 9;
                config.InvincibleTicks = SiegeConfig.MaxStatusTicks;
                var run = new SiegeRun(config, seed);
                for (var i = 0; i < 6000 && !run.IsOver; i++)
                {
                    // 最初は左下の隅へ、後半は右上の隅へ移ってとどまる（置いてきた敵が画面外に残る）。
                    var toward = i < 3000 ? -1 : 1;
                    run.Step(Move(toward, toward, run.Ship.Position));
                    Assert.That(run.Enemies.Count, Is.LessThanOrEqualTo(config.MaxAlive));
                    foreach (var enemy in run.Enemies)
                    {
                        Assert.That(enemy.OffscreenTicks, Is.LessThan(config.OffscreenLimitTicks));
                    }
                }
            }
        }
    }
}
