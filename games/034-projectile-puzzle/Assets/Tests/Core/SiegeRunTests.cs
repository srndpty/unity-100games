using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    public sealed class SiegeRunTests
    {
        private static readonly SiegeConfig _config = new();

        // ---- 射出と命令 ----

        [Test]
        public void LaunchTakesEffectAtTheNextTickBoundaryOnce()
        {
            var run = Run();
            Assert.That(run.Launch(Pull), Is.True);
            Assert.That(run.Launch(Pull), Is.False, "同じ Tick に 2 回は撃てない");
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Aiming));
            var commands = run.BeginTick();
            Assert.That(commands.Launch, Is.EqualTo(Launch.Velocity(Pull, _config)));
            Assert.That(commands.Kind, Is.EqualTo(ShotKind.Normal));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Flying));
            Assert.That(run.ShotsUsed, Is.EqualTo(1));
            run.EndTick(Quiet(10f));
            Assert.That(run.BeginTick().IsEmpty, Is.True);
            Assert.That(run.Launch(Pull), Is.False, "飛行中は撃てない");
        }

        [Test]
        public void CancelledPullDoesNotConsumeAShot()
        {
            var run = Run();
            Assert.That(run.Launch(new V2(0.1f, 0f)), Is.False);
            Assert.That(run.BeginTick().IsEmpty, Is.True);
            Assert.That(run.ShotsLeft, Is.EqualTo(2));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Aiming));
        }

        [Test]
        public void ShotsAreUsedInTheStageOrder()
        {
            var run = Run("heavy bomb normal");
            var kinds = new ShotKind[3];
            for (var i = 0; i < 3; i++)
            {
                Assert.That(run.NextShot, Is.EqualTo(new[] { ShotKind.Heavy, ShotKind.Bomb, ShotKind.Normal }[i]));
                run.Launch(Pull);
                kinds[i] = run.BeginTick().Kind;
                run.EndTick(Quiet());
                Steps(run, _config.RestTicks);
            }

            Assert.That(kinds, Is.EqualTo(new[] { ShotKind.Heavy, ShotKind.Bomb, ShotKind.Normal }));
        }

        // ---- 壊れた部材が二重得点しない ----

        [Test]
        public void ImpactBelowThresholdDoesNothingAndAboveDamagesBothPieces()
        {
            var run = Run();
            Shoot(run);
            Step(run, Hit(0, 3, _config.Wood.Threshold));
            Assert.That(run.Health(0), Is.EqualTo(_config.Wood.Health));
            Step(run, Hit(0, 3, _config.Stone.Threshold + 2f));
            Assert.That(run.Health(0), Is.EqualTo(_config.Wood.Health - SiegeRun.DamageOf(_config.Wood, _config.Stone.Threshold + 2f)));
            Assert.That(run.Health(3), Is.EqualTo(_config.Stone.Health - 18));
            Assert.That(run.Score, Is.EqualTo(0));
        }

        [Test]
        public void DamageFormulaFloorsAndRejectsNonFiniteImpulses()
        {
            var spec = new MaterialSpec(100, 10, 1f, 2f, 3f);
            Assert.That(SiegeRun.DamageOf(spec, 2f), Is.EqualTo(0));
            Assert.That(SiegeRun.DamageOf(spec, 2.4f), Is.EqualTo(1));
            Assert.That(SiegeRun.DamageOf(spec, 5f), Is.EqualTo(9));
            Assert.That(SiegeRun.DamageOf(spec, float.NaN), Is.EqualTo(0));
            Assert.That(SiegeRun.DamageOf(spec, float.PositiveInfinity), Is.EqualTo(0));
            Assert.That(SiegeRun.DamageOf(spec, -5f), Is.EqualTo(0));
            Assert.That(SiegeRun.DamageOf(new MaterialSpec(1, 1, 1f, 0f, 1000f), 1e9f), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void BrokenPieceScoresOnceEvenWhenHitTwiceInTheSameTick()
        {
            var run = Run();
            Shoot(run);
            var report = Hit(1, -1, 1000f);
            report.Impacts.Add(new Impact(-1, 1, 1000f));
            report.Blasts.Add(new BlastHit(1, 0f));
            report.FellOut.Add(1);
            Step(run, report);
            Assert.That(run.IsBroken(1), Is.True);
            Assert.That(run.Score, Is.EqualTo(_config.Lantern.Score));
            Assert.That(run.BrokenCount, Is.EqualTo(1));
            Assert.That(run.BrokenThisTick.Count, Is.EqualTo(1));
            Assert.That(run.BrokenThisTick[0], Is.EqualTo(1));
            Assert.That(run.TargetsLeft, Is.EqualTo(1));
        }

        [Test]
        public void ReportsAfterBreakingChangeNothing()
        {
            var run = Run();
            Shoot(run);
            Step(run, Fell(1));
            var score = run.Score;
            var hash = run.Hash();
            foreach (var report in new[] { Hit(1, 0, 0f), Fell(1), Hit(-1, 1, 500f) })
            {
                var before = run.Hash();
                run.BeginTick();
                run.EndTick(report);
                Assert.That(run.Score, Is.EqualTo(score));
                Assert.That(run.BrokenCount, Is.EqualTo(1));
                Assert.That(run.BrokenThisTick, Is.Empty);
                Assert.That(run.Health(1), Is.EqualTo(0));
                Assert.That(run.Hash(), Is.Not.EqualTo(before), "Tick は進む");
            }

            Assert.That(hash, Is.Not.EqualTo(0u));
        }

        [Test]
        public void PiecesAndJointsToRemoveAreInIndexOrderWhateverTheReportOrder()
        {
            // 報告の順番（衝突のコールバックの順番）に依らず、消す順番を番号順にする。
            var run = Run();
            Shoot(run);
            var report = Hit(3, -1, 1000f);
            report.Impacts.Add(new Impact(1, -1, 1000f));
            report.FellOut.Add(0);
            Step(run, report);
            Assert.That(run.BrokenThisTick.Count, Is.EqualTo(3));
            Assert.That(run.BrokenThisTick[0], Is.EqualTo(0));
            Assert.That(run.BrokenThisTick[1], Is.EqualTo(1));
            Assert.That(run.BrokenThisTick[2], Is.EqualTo(3));
        }

        [Test]
        public void BreakingAPieceRemovesItsJointsOnce()
        {
            var run = Run();
            Shoot(run);
            Step(run, Fell(0));
            Assert.That(run.JointsToRemove.Count, Is.EqualTo(1));
            Assert.That(run.JointsToRemove[0], Is.EqualTo(0));
            Assert.That(run.IsJointBroken(0), Is.True);
            Step(run, Fell(1));
            Assert.That(run.JointsToRemove, Is.Empty, "外れた接合は 2 度外さない");
        }

        [Test]
        public void JointBrokenByPhysicsIsNotRemovedAgain()
        {
            var run = Run();
            Shoot(run);
            var report = Quiet(5f);
            report.BrokenJoints.Add(0);
            report.BrokenJoints.Add(0);
            Step(run, report);
            Assert.That(run.IsJointBroken(0), Is.True);
            Assert.That(run.JointsToRemove, Is.Empty, "Physics2D が外した接合は Unity が既に消している");
            Step(run, Fell(0));
            Assert.That(run.JointsToRemove, Is.Empty);
        }

        [Test]
        public void BlastDamageFallsOffLinearlyToZeroAtTheRadius()
        {
            Assert.That(SiegeRun.BlastDamageOf(_config, 0f), Is.EqualTo(_config.BlastDamage));
            Assert.That(SiegeRun.BlastDamageOf(_config, _config.BlastRadius / 2), Is.EqualTo(_config.BlastDamage / 2));
            Assert.That(SiegeRun.BlastDamageOf(_config, _config.BlastRadius), Is.EqualTo(0));
            Assert.That(SiegeRun.BlastDamageOf(_config, -1f), Is.EqualTo(_config.BlastDamage));
            Assert.That(SiegeRun.BlastDamageOf(_config, float.NaN), Is.EqualTo(0));
        }

        [Test]
        public void SettlingImpactsAtTheStartAreIgnoredUntilTheGraceEndsOrTheFirstShot()
        {
            var run = Run();
            Step(run, Hit(0, -1, 50f));
            Assert.That(run.Health(0), Is.EqualTo(_config.Wood.Health));
            Steps(run, _config.GraceTicks - 1);
            Assert.That(run.Ticks, Is.EqualTo(_config.GraceTicks));
            Step(run, Hit(0, -1, 50f));
            Assert.That(run.Health(0), Is.LessThan(_config.Wood.Health), "猶予の後は数える");

            var early = Run();
            Shoot(early);
            Step(early, Hit(0, -1, 50f));
            Assert.That(early.Health(0), Is.LessThan(_config.Wood.Health), "撃った後は猶予の間でも数える");
        }

        [Test]
        public void OutOfRangeIndicesAreRejected()
        {
            var run = Run();
            run.BeginTick();
            Assert.Throws<ArgumentOutOfRangeException>(() => run.EndTick(Hit(4, -1, 10f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => run.EndTick(Hit(-2, -1, 10f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => run.EndTick(Fell(9)));
            var joints = Quiet();
            joints.BrokenJoints.Add(1);
            Assert.Throws<ArgumentOutOfRangeException>(() => run.EndTick(joints));
            Assert.That(run.Ticks, Is.EqualTo(0), "拒否した報告では進まない");
        }

        // ---- 停止後のみ結果確定 ----

        [Test]
        public void AllTargetsDownDoesNotWinWhileThingsMove()
        {
            var run = Run();
            Shoot(run);
            Step(run, Fell(1, 2));
            Assert.That(run.TargetsLeft, Is.EqualTo(0));
            Steps(run, 200, speed: 3f);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Flying));
            Steps(run, _config.RestTicks - 1);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Flying), "1 Tick 足りない");
            Step(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Won));
            Assert.That(run.TakeEvents() & RunEvents.Won, Is.EqualTo(RunEvents.Won));
        }

        [Test]
        public void QuietCountRestartsWhenSomethingMovesAgain()
        {
            var run = Run();
            Shoot(run);
            Step(run, Fell(1, 2));
            Steps(run, _config.RestTicks - 1);
            Step(run, Quiet(_config.RestSpeed));
            Steps(run, _config.RestTicks - 1);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Flying));
            Step(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Won));
        }

        [Test]
        public void SpinningAloneKeepsTheFieldUnsettled()
        {
            var run = Run();
            Shoot(run);
            for (var i = 0; i < _config.RestTicks * 2; i++) { Step(run, new TickReport { MaxAngular = _config.RestAngular }); }
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Flying));
        }

        [Test]
        public void TargetBrokenDuringTheLastShotsAftermathStillWins()
        {
            var run = Run("normal");
            Shoot(run);
            Step(run, Fell(2));
            Steps(run, 50, speed: 1f);
            Step(run, Fell(1));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Flying));
            Steps(run, _config.RestTicks);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Won));
            Assert.That(run.Stars, Is.EqualTo(1));
            Assert.That(run.Score, Is.EqualTo(_config.Lantern.Score * 2));
        }

        [Test]
        public void OutOfShotsWithATargetLeftLosesOnlyAfterSettling()
        {
            var run = Run("normal");
            Shoot(run);
            Step(run, Fell(2));
            Steps(run, _config.RestTicks - 1);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Flying));
            Step(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Lost));
            Assert.That(run.Stars, Is.EqualTo(0));
            Assert.That(run.Score, Is.EqualTo(_config.Lantern.Score), "負けでも壊した得点は残る（残弾の得点はない）");
        }

        [Test]
        public void SettlingWithShotsLeftReturnsToAiming()
        {
            var run = Run();
            Shoot(run);
            Steps(run, _config.RestTicks);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Aiming));
            Assert.That(run.TakeEvents() & RunEvents.Ready, Is.EqualTo(RunEvents.Ready));
            Assert.That(run.ShotRemovedThisTick, Is.True, "止まった弾は静止した Tick に消す");
            Assert.That(run.ShotAlive, Is.False);
            Assert.That(run.BeginTick().RemoveShot, Is.False, "次の Tick の命令では消さない");
            run.EndTick(Quiet());
            Assert.That(run.ShotRemovedThisTick, Is.False);
        }

        [Test]
        public void SettlingIntoAWinOrLossAlsoRemovesTheShotInThatTick()
        {
            // 静止で勝敗が決まると次の Tick がないので、その Tick の結果で弾を消す（レビュー P3-1）。
            var lost = Run("normal");
            Shoot(lost);
            Steps(lost, _config.RestTicks - 1);
            Assert.That(lost.ShotRemovedThisTick, Is.False);
            Step(lost);
            Assert.That(lost.Phase, Is.EqualTo(RunPhase.Lost));
            Assert.That(lost.ShotRemovedThisTick, Is.True);

            var won = Run("normal normal");
            Shoot(won);
            Step(won, Fell(1, 2));
            Steps(won, _config.RestTicks);
            Assert.That(won.Phase, Is.EqualTo(RunPhase.Won));
            Assert.That(won.ShotRemovedThisTick, Is.True);
        }

        [Test]
        public void GoneShotIsNotRemovedAgainOnSettling()
        {
            var run = Run();
            Shoot(run);
            var gone = Quiet();
            gone.ShotGone = true;
            Step(run, gone);
            Assert.That(run.ShotAlive, Is.False);
            Steps(run, _config.RestTicks);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Aiming));
            Assert.That(run.ShotRemovedThisTick, Is.False, "盤の外へ出た弾は Unity が既に消している");
        }

        [Test]
        public void EndlessWobbleSettlesAtTheTimeout()
        {
            var run = Run();
            Shoot(run);
            Steps(run, _config.SettleTimeoutTicks - 2, speed: 2f);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Flying));
            Step(run, Quiet(2f));
            Assert.That(run.TicksSinceLaunch, Is.EqualTo(_config.SettleTimeoutTicks));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Aiming));
        }

        [Test]
        public void ShotIsRemovedAfterItsLifetime()
        {
            var run = Run();
            Shoot(run);
            Steps(run, _config.ShotLifeTicks - 1, speed: 2f);
            Assert.That(run.TicksSinceLaunch, Is.EqualTo(_config.ShotLifeTicks));
            Assert.That(run.BeginTick().RemoveShot, Is.True);
            Assert.That(run.ShotAlive, Is.False);
        }

        [Test]
        public void WinIsFinalAndLaterCommandsAndReportsAreIgnored()
        {
            var run = Run("normal normal");
            Shoot(run);
            Step(run, Fell(1, 2));
            Steps(run, _config.RestTicks);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Won));
            Assert.That(run.Score, Is.EqualTo(_config.Lantern.Score * 2 + _config.UnusedShotBonus));
            Assert.That(run.Stars, Is.EqualTo(2));
            var hash = run.Hash();
            Assert.That(run.Launch(Pull), Is.False);
            Assert.That(run.Detonate(), Is.False);
            Assert.That(run.BeginTick().IsEmpty, Is.True);
            run.EndTick(Fell(0, 3));
            Assert.That(run.Hash(), Is.EqualTo(hash));
            Assert.That(run.BrokenThisTick, Is.Empty);
        }

        [Test]
        public void TargetsFallingWhileAimingWinOnceSettled()
        {
            var run = Run();
            Steps(run, _config.GraceTicks);
            Step(run, Fell(1, 2));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Aiming));
            Steps(run, _config.RestTicks);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Won));
            Assert.That(run.Stars, Is.EqualTo(3));
        }

        // ---- 爆発の弾 ----

        [Test]
        public void BombDetonatesOnceOnRequest()
        {
            var run = Run("bomb normal");
            Assert.That(run.Detonate(), Is.False, "構えている間は起爆できない");
            Shoot(run);
            Assert.That(run.CanDetonate, Is.True);
            Assert.That(run.Detonate(), Is.True);
            Assert.That(run.Detonate(), Is.False, "同じ Tick の 2 回目は無視");
            var commands = Step(run, Quiet(5f));
            Assert.That(commands.Detonate, Is.True);
            Assert.That(run.Detonated, Is.True);
            Assert.That(run.ShotAlive, Is.False);
            Assert.That(run.Detonate(), Is.False, "1 発で 1 回だけ");
            Assert.That(Step(run, Quiet(5f)).Detonate, Is.False);
        }

        [Test]
        public void NonBombShotsCannotDetonate()
        {
            foreach (var shot in new[] { "normal", "heavy" })
            {
                var run = Run(shot);
                Shoot(run);
                Assert.That(run.Detonate(), Is.False, shot);
            }
        }

        [Test]
        public void BombFusesAfterItsFirstTouch()
        {
            var run = Run("bomb");
            Shoot(run);
            Steps(run, 10, speed: 5f);
            var touch = Quiet(5f);
            touch.ShotTouched = true;
            Step(run, touch);
            Assert.That(run.FuseElapsed, Is.EqualTo(0));
            for (var i = 0; i < _config.FuseTicks; i++)
            {
                Assert.That(Step(run, touch).Detonate, Is.False, $"{i} Tick");
            }

            Assert.That(run.FuseElapsed, Is.EqualTo(_config.FuseTicks));
            Assert.That(Step(run, Quiet(5f)).Detonate, Is.True);
        }

        [Test]
        public void BombWithABurningFuseWaitsForTheBlastInsteadOfSettling()
        {
            // 触れた直後に盤全体が止まっても、静止で弾を消さずに導火線の満了で起爆し、その後に静止で決着する（レビュー P2）。
            var run = Run("bomb");
            Shoot(run);
            var touch = Quiet();
            touch.ShotTouched = true;
            Step(run, touch);
            Assert.That(run.FuseBurning, Is.True);
            for (var i = 0; i < _config.FuseTicks; i++)
            {
                Assert.That(Step(run).Detonate, Is.False, $"{i} Tick");
                Assert.That(run.Phase, Is.EqualTo(RunPhase.Flying), $"{i} Tick：止まっていても決着しない");
                Assert.That(run.ShotRemovedThisTick, Is.False);
            }

            Assert.That(run.QuietTicks, Is.GreaterThan(_config.RestTicks));
            var blast = Step(run, Fell(1, 2));
            Assert.That(blast.Detonate, Is.True);
            Assert.That(run.FuseBurning, Is.False);
            Steps(run, _config.RestTicks);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Won), "起爆の後は静止で決着する");
        }

        [Test]
        public void SettleTimeoutStillAppliesWhileTheFuseBurns()
        {
            var config = new SiegeConfig { FuseTicks = 600, SettleTimeoutTicks = 300, ShotLifeTicks = 300 };
            var run = new SiegeRun(config, Small("bomb normal"));
            Shoot(run);
            var touch = Quiet();
            touch.ShotTouched = true;
            Step(run, touch);
            Steps(run, config.SettleTimeoutTicks - 3);
            Assert.That(run.TicksSinceLaunch, Is.EqualTo(config.SettleTimeoutTicks - 1));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Flying));
            Step(run);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Aiming), "揺れが続く・導火線が長くても上限で決着する");
            Assert.That(run.ShotRemovedThisTick, Is.True);
        }

        [Test]
        public void BlastHitsDamageByDistanceAndCanBreakTargets()
        {
            var run = Run("bomb");
            Shoot(run);
            run.Detonate();
            run.BeginTick();
            var report = Quiet(5f);
            report.Blasts.Add(new BlastHit(1, 0.1f));
            report.Blasts.Add(new BlastHit(3, _config.BlastRadius + 1f));
            run.EndTick(report);
            Assert.That(run.IsBroken(1), Is.True);
            Assert.That(run.Health(3), Is.EqualTo(_config.Stone.Health));
        }

        // ---- 再現性 ----

        [Test]
        public void SameCommandsAndReportsGiveTheSameHash()
        {
            static uint Play()
            {
                var run = Run("normal bomb");
                Steps(run, 70);
                Shoot(run);
                Step(run, Hit(0, -1, 40f));
                Steps(run, _config.RestTicks);
                Shoot(run);
                run.Detonate();
                var blast = Quiet(3f);
                blast.Blasts.Add(new BlastHit(1, 1f));
                Step(run, blast);
                Steps(run, _config.RestTicks);
                return run.Hash();
            }

            Assert.That(Play(), Is.EqualTo(Play()));
        }

        [Test]
        public void HashSeparatesStatesWhoseFutureDiffers()
        {
            // 今は同じ Health でも、静止の数え・起爆の命令・弾の残りが違えば以後が分かれる。
            var a = Run("bomb bomb");
            var b = Run("bomb bomb");
            Shoot(a);
            Shoot(b);
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            b.Detonate();
            Assert.That(a.Hash(), Is.Not.EqualTo(b.Hash()), "起爆の命令");

            var c = Run();
            var d = Run();
            Shoot(c);
            Shoot(d);
            Step(c, Quiet(0f));
            Step(d, Quiet(5f));
            Assert.That(c.Hash(), Is.Not.EqualTo(d.Hash()), "静止の数え");

            var config = new SiegeConfig { Gravity = -9f };
            Assert.That(new SiegeRun(config, Small()).Hash(), Is.Not.EqualTo(Run().Hash()), "設定の指紋");
            var renamed = StageBook.Parse("stage 別名\nhint 別の案内\nlauncher 4 2.5\nshots normal normal\nblock wood 20 1 0.4 2\nblock lantern 20 2.3 0.6 0.6\nblock lantern 24 0.3 0.6 0.6\nblock stone 27 0.5 1 1\njoint 0 1 500\nend\n").Stages[0];
            Assert.That(new SiegeRun(_config, renamed).Hash(), Is.EqualTo(Run().Hash()), "名前と案内は含まない");
        }

        [Test]
        public void ConstructorCopiesTheConfig()
        {
            var config = new SiegeConfig();
            var run = new SiegeRun(config, Small());
            config.UnusedShotBonus = 7;
            config.Lantern.Score = 1;
            Shoot(run);
            Step(run, Fell(1, 2));
            Steps(run, _config.RestTicks);
            Assert.That(run.Score, Is.EqualTo(_config.Lantern.Score * 2 + _config.UnusedShotBonus));
        }

        [Test]
        public void InvalidConfigIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SiegeRun(new SiegeConfig { MaxSpeed = float.NaN }, Small()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SiegeRun(new SiegeConfig { MinPull = 3f }, Small()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SiegeRun(new SiegeConfig { Gravity = 1f }, Small()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SiegeRun(new SiegeConfig { ShotLifeTicks = 10000 }, Small()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SiegeRun(new SiegeConfig { ShotAngularDamping = float.NaN }, Small()));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SiegeRun(new SiegeConfig { ShotAngularDamping = -1f }, Small()));
            var config = new SiegeConfig();
            config.Ice.Density = float.PositiveInfinity;
            Assert.Throws<ArgumentOutOfRangeException>(() => new SiegeRun(config, Small()));
            config = new SiegeConfig();
            config.Wood = null!;
            Assert.Throws<ArgumentNullException>(() => new SiegeRun(config, Small()));
        }
    }
}
