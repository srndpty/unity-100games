using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>試合の進行（勝敗・終了後の操作・再現性・複製）と設定の検証。</summary>
    public sealed class RunTests
    {
        [Test]
        public void SurvivingDuration_Wins_WithLifeBonus()
        {
            var config = Quiet(c => c.DurationTicks = 100);
            var run = Run(config);
            var events = Wait(run, 99);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Playing));
            events |= Wait(run, 1);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Won));
            Assert.That(events & RunEvents.Won, Is.EqualTo(RunEvents.Won));
            Assert.That(run.Score, Is.EqualTo((long)config.Lives * config.LifeBonus));
            Assert.That(run.RemainingTicks, Is.Zero);
        }

        [Test]
        public void LosingOnLastTick_IsLossNotWin()
        {
            var config = Quiet(c =>
            {
                c.DurationTicks = 10;
                c.Lives = 1;
            });
            var run = Run(config);
            Wait(run, 9);
            run.Place(EnemyKind.Bouncer, run.Ship.Position);
            run.Step(Idle(run));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Lost));
            Assert.That(run.Score, Is.Zero);
        }

        [Test]
        public void StepsAfterEnd_ChangeNothing()
        {
            var run = Run(Quiet(c => c.Lives = 1));
            run.Place(EnemyKind.Bouncer, run.Ship.Position);
            run.Step(Idle(run));
            Assert.That(run.IsOver, Is.True);
            run.TakeEvents();
            var hash = run.Hash();
            var elapsed = run.Elapsed;
            for (var i = 0; i < 10; i++) { run.Step(Move(1, 1, run.Ship.Position + At(T, 0), fire: true)); }
            Assert.That(run.Hash(), Is.EqualTo(hash));
            Assert.That(run.Elapsed, Is.EqualTo(elapsed));
            Assert.That(run.TakeEvents(), Is.EqualTo(RunEvents.None));
            Assert.That(() => run.Place(EnemyKind.Chaser, run.Ship.Position), Throws.InvalidOperationException);
        }

        [Test]
        public void SameSeedAndInputs_GiveSameHash_DifferentSeedsDiffer()
        {
            uint Play(int seed)
            {
                var run = new SiegeRun(Config(), seed);
                for (var i = 0; i < 1200 && !run.IsOver; i++)
                {
                    run.Step(Move((i / 50 % 3) - 1, (i / 80 % 3) - 1, run.Ship.Position + At((i % 7) - 3, (i % 5) - 2), fire: i % 3 == 0));
                }

                return run.Hash();
            }

            Assert.That(Play(5), Is.EqualTo(Play(5)));
            Assert.That(Play(5), Is.Not.EqualTo(Play(6)));
        }

        [Test]
        public void Clone_DoesNotShareFutureProgress()
        {
            var run = new SiegeRun(Config(), 3);
            Wait(run, 300);
            var copy = run.Clone();
            Assert.That(copy.Hash(), Is.EqualTo(run.Hash()));
            for (var i = 0; i < 200; i++) { run.Step(Move(1, 0, run.Ship.Position + At(0, T), fire: true)); }
            Assert.That(copy.Hash(), Is.Not.EqualTo(run.Hash()));
            Assert.That(copy.Elapsed, Is.EqualTo(300));

            // 複製を同じ入力で進めれば、元と一致する（乱数の系列も複製されている）。
            for (var i = 0; i < 200; i++) { copy.Step(Move(1, 0, copy.Ship.Position + At(0, T), fire: true)); }
            Assert.That(copy.Hash(), Is.EqualTo(run.Hash()));
        }

        [Test]
        public void HashIncludesTimers_ThatDecideTheFuture()
        {
            // 位置は同じでも、連射の待ちが違えば以後が分かれる。
            var a = Run();
            var b = Run();
            a.Step(Move(0, 0, a.Ship.Position + At(T, 0), fire: true));
            b.Step(Idle(b));
            a.Step(Idle(a));
            b.Step(Idle(b));
            Wait(a, 60);
            Wait(b, 60);
            Assert.That(a.Ship.Position, Is.EqualTo(b.Ship.Position));
            Assert.That(a.Bullets.Count, Is.EqualTo(b.Bullets.Count));
            Assert.That(a.Hash(), Is.Not.EqualTo(b.Hash()), "弾の番号が違う");
        }

        [Test]
        public void ChangingConfigAfterStart_DoesNotAffectRun()
        {
            var config = Config();
            var run = new SiegeRun(config, 1);
            config.ShipSpeed = 1;
            config.Stages[0].IntervalTicks = 1;
            run.Step(Move(1, 0, run.Ship.Position));
            Assert.That(run.Ship.Position.X, Is.EqualTo(Center(run.Config).X + run.Config.ShipSpeed));
            Assert.That(run.Config.ShipSpeed, Is.EqualTo(new SiegeConfig().ShipSpeed));
            Assert.That(run.Config.Stages[0].IntervalTicks, Is.EqualTo(new SiegeConfig().Stages[0].IntervalTicks));
        }

        [Test]
        public void ShipStaysInsideArena()
        {
            var run = Run();
            for (var i = 0; i < 2000; i++) { run.Step(Move(-1, 1, run.Ship.Position)); }
            Assert.That(run.Ship.Position, Is.EqualTo(At(run.Config.ShipRadius, run.Config.ArenaHeight - run.Config.ShipRadius)));
        }

        [Test]
        public void PlacingBeyondCap_IsRejected()
        {
            var run = Run(Quiet(c => c.MaxAlive = 2));
            run.Place(EnemyKind.Chaser, At(T, T));
            run.Place(EnemyKind.Chaser, At(2 * T, T));
            Assert.That(() => run.Place(EnemyKind.Chaser, At(3 * T, T)), Throws.InvalidOperationException);
        }

        [Test]
        public void DefaultConfig_IsValid()
        {
            Assert.That(() => Config().Validate(), Throws.Nothing);
            Assert.That(Config().DurationTicks, Is.EqualTo(18000), "5 分");
        }

        [TestCase(nameof(SiegeConfig.ShipSpeed), 0)]
        [TestCase(nameof(SiegeConfig.ShipSpeed), SiegeConfig.MaxSpeed + 1)]
        [TestCase(nameof(SiegeConfig.Lives), 0)]
        [TestCase(nameof(SiegeConfig.InvincibleTicks), 0)]
        [TestCase(nameof(SiegeConfig.FireIntervalTicks), 0)]
        [TestCase(nameof(SiegeConfig.BulletLifeTicks), (6 * SiegeConfig.MaxBulletsLimit) + 1)]
        [TestCase(nameof(SiegeConfig.MaxAlive), 0)]
        [TestCase(nameof(SiegeConfig.MaxAlive), SiegeConfig.MaxAliveLimit + 1)]
        [TestCase(nameof(SiegeConfig.ViewWidthCells), 49)]
        [TestCase(nameof(SiegeConfig.SpawnAttempts), 0)]
        [TestCase(nameof(SiegeConfig.OffscreenLimitTicks), 0)]
        [TestCase(nameof(SiegeConfig.DurationTicks), 0)]
        public void OutOfRange_IsRejected(string field, int value)
        {
            var config = Config();
            typeof(SiegeConfig).GetField(field)!.SetValue(config, value);
            Assert.That(() => config.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new SiegeRun(config, 1), Throws.TypeOf<ArgumentOutOfRangeException>(), "試合を作るときも検証する");
        }

        [Test]
        public void BulletLife_AtCapacityBoundary_IsAccepted()
        {
            var config = Config();
            config.BulletLifeTicks = config.FireIntervalTicks * SiegeConfig.MaxBulletsLimit;
            Assert.That(() => config.Validate(), Throws.Nothing);
            Assert.That(config.BulletCapacity, Is.EqualTo(SiegeConfig.MaxBulletsLimit));
        }

        [Test]
        public void SafeRadius_MustLeaveRoomInView()
        {
            var config = Config();
            var largest = Math.Max(config.Chaser.Radius, Math.Max(config.Bouncer.Radius, config.Lancer.Radius));
            config.SafeRadius = (config.ViewHeight / 2) - largest - 1;
            Assert.That(() => config.Validate(), Throws.Nothing);
            config.SafeRadius++;
            Assert.That(() => config.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Stages_MustCoverDurationAndHaveWeights()
        {
            var short1 = Config();
            short1.Stages = new[] { short1.Stages[0] };
            Assert.That(() => short1.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>(), "区間が 5 分を覆わない");

            var empty = Config();
            empty.Stages = Array.Empty<SpawnStage>();
            Assert.That(() => empty.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());

            var zero = Config();
            zero.Stages[2] = new SpawnStage { IntervalTicks = 10 };
            Assert.That(() => zero.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>(), "重みの合計 0");

            var nullStage = Config();
            nullStage.Stages[1] = null!;
            Assert.That(() => nullStage.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());

            var badStats = Config();
            badStats.Lancer = new EnemyStats { Radius = 0, Speed = 1, Health = 1 };
            Assert.That(() => badStats.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
