using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>試合の状態遷移・再現性・完走と、設定の検証。</summary>
    public sealed class MatchTests
    {
        /// <summary>試合の Tick の上限（2 時間）。決着しなければ失敗。</summary>
        private const int TickLimit = 60 * 60 * 120;

        [Test]
        public void MatchStartsWithTheNearPlayerServingFromTheDeuceCourt()
        {
            var match = new TennisMatch(TestKit.Config(), seed: 1);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Point));
            Assert.That(match.Rally.Server, Is.EqualTo(Side.Near));
            Assert.That(match.Rally.ServeCourt, Is.EqualTo(ServeCourt.Deuce));
            Assert.That(match.Near.Position.X, Is.GreaterThan(0), "手前の選手の右");
            Assert.That(match.BallInHand, Is.True);
        }

        [Test]
        public void ServerCannotMoveBeforeTheServeButTheReceiverCan()
        {
            var match = new TennisMatch(TestKit.Config(), seed: 1);
            var server = match.Near.Position;
            var receiver = match.Far.Position;
            match.Step(new AthleteCommand(1, 1, false, false), new AthleteCommand(1, 0, false, false));
            Assert.That(match.Near.Position, Is.EqualTo(server));
            Assert.That(match.Far.Position.X, Is.GreaterThan(receiver.X));
        }

        [Test]
        public void AthleteStaysOnItsOwnSide()
        {
            var config = TestKit.Config();
            var near = new Athlete(Side.Near, new Vec3(0, -100000, 0));
            var far = new Athlete(Side.Far, new Vec3(0, 100000, 0));
            for (var i = 0; i < 300; i++)
            {
                near.Move(-1, 1, config.MoveSpeed, config);
                far.Move(1, -1, config.MoveSpeed, config);
            }

            Assert.That(near.Position, Is.EqualTo(new Vec3(-(Court.FenceX - config.AthleteMargin), -config.NetGap, 0)));
            Assert.That(far.Position, Is.EqualTo(new Vec3(Court.FenceX - config.AthleteMargin, config.NetGap, 0)));
            for (var i = 0; i < 300; i++) { near.Move(0, -1, config.MoveSpeed, config); }
            Assert.That(near.Position.Y, Is.EqualTo(-(Court.FenceY - config.AthleteMargin)));
        }

        [Test]
        public void DiagonalStepIsShorterButNeverZero()
        {
            var config = TestKit.Config();
            var athlete = new Athlete(Side.Near, new Vec3(0, -100000, 0));
            athlete.Move(1, 1, config.MoveSpeed, config);
            Assert.That(athlete.Position, Is.EqualTo(new Vec3(config.MoveSpeed * 181 / 256, -100000 + (config.MoveSpeed * 181 / 256), 0)));
            athlete.Move(1, 0, 1, config);
            athlete.Move(1, 1, 1, config);
            Assert.That(athlete.Position.X, Is.EqualTo((config.MoveSpeed * 181 / 256) + 2), "速さ 1 の斜めでも 1 は動く");
        }

        [Test]
        public void HeldKeyDoesNotSwingAgain()
        {
            var match = new TennisMatch(TestKit.Config(), seed: 1);
            match.Step(TestKit.Press(), AthleteCommand.Idle);
            Assert.That(match.Rally.Phase, Is.EqualTo(RallyPhase.Toss));
            for (var i = 0; i < 40; i++) { match.Step(TestKit.Press(), AthleteCommand.Idle); }
            Assert.That(match.Rally.TossSwung, Is.False, "押したままではトスを打たない");
        }

        [Test]
        public void SameSeedAndInputsGiveTheSameHash()
        {
            var a = PlayCpuVsCpu(new TennisMatch(TestKit.Config(), seed: 42), 3000);
            var b = PlayCpuVsCpu(new TennisMatch(TestKit.Config(), seed: 42), 3000);
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            var c = PlayCpuVsCpu(new TennisMatch(TestKit.Config(), seed: 43), 3000);
            Assert.That(c.Hash(), Is.Not.EqualTo(a.Hash()), "シードが違えば CPU の判断が変わる");
        }

        [Test]
        public void CloneContinuesIdentically()
        {
            var match = PlayCpuVsCpu(new TennisMatch(TestKit.Config(), seed: 7), 1500);
            var copy = match.Clone();
            PlayCpuVsCpu(match, 1500);
            PlayCpuVsCpu(copy, 1500);
            Assert.That(copy.Hash(), Is.EqualTo(match.Hash()));
        }

        [Test]
        public void CpuVersusCpuFinishesTheDefaultMatch([Range(1, 50)] int seed)
        {
            var match = PlayToTheEnd(new TennisMatch(TestKit.Config(), seed));
            AssertValidFinalScore(match);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void CpuVersusCpuFinishesAFullSet(int seed)
        {
            var config = TestKit.Config();
            config.TiebreakAtGames = 6;
            var match = PlayToTheEnd(new TennisMatch(config, seed));
            AssertValidFinalScore(match);
            var winner = match.Winner!.Value;
            Assert.That(match.Score.Games(winner), Is.GreaterThanOrEqualTo(6));
        }

        [Test]
        public void RalliesActuallyHappen()
        {
            var longest = 0;
            for (var seed = 1; seed <= 10; seed++)
            {
                longest = Math.Max(longest, PlayToTheEnd(new TennisMatch(TestKit.Config(), seed)).LongestRally);
            }

            Assert.That(longest, Is.GreaterThanOrEqualTo(4), "CPU どうしで打ち合いが続く");
        }

        [Test]
        public void FinishedMatchIgnoresTicksAndInput()
        {
            var match = PlayToTheEnd(new TennisMatch(TestKit.Config(), seed: 5));
            var hash = match.Hash();
            var tick = match.Tick;
            match.Step(TestKit.Press());
            match.Step(new AthleteCommand(1, 1, true, true), AthleteCommand.Idle);
            Assert.That(match.Hash(), Is.EqualTo(hash));
            Assert.That(match.Tick, Is.EqualTo(tick));
            Assert.Throws<InvalidOperationException>(() => match.StageRally(Side.Near, Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero));
        }

        [Test]
        public void FixedSeedFinalHashIsStable()
        {
            // 整数だけで進むので .NET と Mono で同じ値になる（dev test と dev test 024 -Unity の両方で照合）。
            var match = PlayToTheEnd(new TennisMatch(TestKit.Config(), seed: 1));
            Assert.That(match.Hash(), Is.EqualTo(ExpectedFinalHash), $"Tick {match.Tick}");
        }

        private const uint ExpectedFinalHash = 0x2A9FD882u;

        // ---- 設定 ----

        [Test]
        public void DefaultConfigIsValid()
        {
            Assert.DoesNotThrow(() => TestKit.Config().Validate());
        }

        [Test]
        public void OutOfRangeValuesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => With(c => c.TiebreakAtGames = 7).Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => With(c => c.TiebreakPoints = 0).Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => With(c => c.Gravity = 0).Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => With(c => c.ImpactTicks = c.SwingTicks).Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => With(c => c.MaxHitZ = c.MinHitZ).Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => With(c => c.StrokeDepth = Court.HalfLength + 1).Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => With(c => c.MoveSpeed = -1).Validate());
        }

        [Test]
        public void BallFasterThanTheReachIsRejected()
        {
            Assert.Throws<ArgumentException>(() => With(c => c.Reach = c.ServeSpeed).Validate());
            Assert.DoesNotThrow(() => With(c => c.Reach = c.ServeSpeed + 1).Validate());
        }

        [Test]
        public void OnTimeShotsThatMissAreRejected()
        {
            // サーブがネットのすぐ先を狙うと、ネットの余裕を満たせずにかかる。
            var error = Assert.Throws<ArgumentException>(() => With(c =>
            {
                c.ServeDepth = 1;
                c.ServeShortError = 0;
            }).Validate());
            Assert.That(error!.Message, Does.Contain("サーブ"));
            Assert.That(ShotCheck.FirstFailure(TestKit.Config()), Is.Null);
        }

        [Test]
        public void MatchCopiesTheConfig()
        {
            var config = TestKit.Config();
            var match = new TennisMatch(config, seed: 1);
            config.MoveSpeed = 1;
            Assert.That(match.Config.MoveSpeed, Is.EqualTo(TestKit.Config().MoveSpeed));
        }

        private static TennisConfig With(Action<TennisConfig> change)
        {
            var config = TestKit.Config();
            change(config);
            return config;
        }

        private static TennisMatch PlayCpuVsCpu(TennisMatch match, int ticks)
        {
            var pilot = new CpuBrain(Side.Near, match.Seed + 1000);
            for (var i = 0; i < ticks && !match.IsFinished; i++) { match.Step(pilot.Decide(match)); }
            return match;
        }

        private static TennisMatch PlayToTheEnd(TennisMatch match)
        {
            PlayCpuVsCpu(match, TickLimit);
            Assert.That(match.IsFinished, Is.True, $"上限 {TickLimit} Tick で決着しない（{match.Score.Games(Side.Near)}-{match.Score.Games(Side.Far)}、{match.Score.Points(Side.Near)}-{match.Score.Points(Side.Far)}）");
            return match;
        }

        private static void AssertValidFinalScore(TennisMatch match)
        {
            var score = match.Score;
            var winner = match.Winner!.Value;
            var loser = Court.Other(winner);
            Assert.That(score.Games(winner), Is.GreaterThan(score.Games(loser)));
            if (score.TiebreakAtGames == 0)
            {
                Assert.That(score.Games(winner), Is.EqualTo(1));
                Assert.That(score.Games(loser), Is.Zero);
            }
        }
    }
}
