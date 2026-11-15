using System;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class MatchStateTests
    {
        [Test]
        public void NewMatch_StartsServingWithZeroScore()
        {
            var match = TestCourt.NewMatch();

            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Serving));
            Assert.That(match.LeftScore, Is.Zero);
            Assert.That(match.RightScore, Is.Zero);
            Assert.That(match.Winner, Is.Null);
            Assert.That(match.Ball.InPlay, Is.False);
        }

        [Test]
        public void Serve_LaunchesBallAfterDelay()
        {
            var match = TestCourt.NewMatch();

            for (var i = 0; i < match.Config.ServeDelayTicks - 1; i++)
            {
                match.Tick(0f, 0f);
            }

            Assert.That(match.Ball.InPlay, Is.False);
            match.Tick(0f, 0f);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Playing));
            Assert.That(match.Ball.InPlay, Is.True);
            Assert.That(match.Ball.Velocity.Length, Is.EqualTo(match.Config.ServeSpeed).Within(1e-4f));
        }

        [Test]
        public void Scoring_HappensOnceAndResetsBallToCenter()
        {
            var match = TestCourt.NewMatch();
            var receiver = PlayUntilFirstPoint(match, out var scorer);

            Assert.That(match.ScoreOf(scorer), Is.EqualTo(1));
            Assert.That(match.ScoreOf(receiver), Is.Zero);
            Assert.That(match.Events.Count(e => e.Kind == MatchEventKind.Scored), Is.EqualTo(1));
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Serving));
            Assert.That(match.Ball.InPlay, Is.False);
            Assert.That(match.Ball.Position, Is.EqualTo(Vec2.Zero));
        }

        [Test]
        public void AfterScoring_NoSecondBallOrPointUntilNextServe()
        {
            var match = TestCourt.NewMatch();
            var receiver = PlayUntilFirstPoint(match, out var scorer);

            // サーブ待ちの間は何 Tick 進めてもボールは動かず、得点も増えない
            for (var i = 0; i < match.Config.ServeDelayTicks - 1; i++)
            {
                match.Tick(0f, 0f);
                Assert.That(match.Events, Is.Empty);
                Assert.That(match.Ball.InPlay, Is.False);
            }

            match.Tick(0f, 0f);
            Assert.That(match.Ball.InPlay, Is.True);
            Assert.That(match.ScoreOf(scorer) + match.ScoreOf(receiver), Is.EqualTo(1));
            Assert.That(match.Ball.Velocity.X * receiver.Direction(), Is.GreaterThan(0f), "失点した側へサーブする");
        }

        [Test]
        public void ReachingWinScore_FinishesMatch()
        {
            var match = TestCourt.NewMatch();

            TestCourt.PlayToEnd(match, _ => 0f, _ => 0f);

            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Finished));
            Assert.That(match.Winner, Is.Not.Null);
            Assert.That(match.ScoreOf(match.Winner!.Value), Is.EqualTo(match.Config.WinScore));
            Assert.That(match.ScoreOf(match.Winner.Value.Opponent()), Is.LessThan(match.Config.WinScore));
            Assert.That(match.Events, Has.Member(new MatchEvent(MatchEventKind.MatchWon, match.Winner.Value)));
            Assert.That(match.Ball.InPlay, Is.False);
        }

        [Test]
        public void AfterWinner_TicksAreIgnored()
        {
            var match = TestCourt.NewMatch();
            TestCourt.PlayToEnd(match, _ => 0f, _ => 0f);
            var left = match.LeftScore;
            var right = match.RightScore;
            var ticks = match.Ticks;
            var paddleY = match.Left.Y;

            for (var i = 0; i < 1000; i++)
            {
                match.Tick(1f, -1f);
            }

            Assert.That(match.LeftScore, Is.EqualTo(left));
            Assert.That(match.RightScore, Is.EqualTo(right));
            Assert.That(match.Ticks, Is.EqualTo(ticks));
            Assert.That(match.Left.Y, Is.EqualTo(paddleY));
            Assert.That(match.Events, Is.Empty);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void NonFiniteInput_IsIgnored(float input)
        {
            var match = TestCourt.NewMatch();

            match.Tick(input, input);

            Assert.That(match.Left.Y, Is.Zero);
            Assert.That(match.Right.Y, Is.Zero);
        }

        [Test]
        public void OutOfRangeInput_IsClampedToPaddleSpeed()
        {
            var match = TestCourt.NewMatch();

            match.Tick(100f, -100f);

            var step = match.Config.PaddleSpeed * match.Config.TickDuration;
            Assert.That(match.Left.Y, Is.EqualTo(step).Within(1e-5f));
            Assert.That(match.Right.Y, Is.EqualTo(-step).Within(1e-5f));
        }

        [Test]
        public void Paddle_StaysInsideCourt()
        {
            var match = TestCourt.NewMatch();

            for (var i = 0; i < 1000; i++)
            {
                match.Tick(1f, -1f);
            }

            var limit = (match.Config.FieldHeight / 2f) - match.Left.HalfHeight;
            Assert.That(match.Left.Y, Is.EqualTo(limit));
            Assert.That(match.Right.Y, Is.EqualTo(-limit));
        }

        [Test]
        public void InvalidConfig_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => new MatchState(new PongConfig { WinScore = 0 }, TestCourt.Seed));
            Assert.Throws<ArgumentException>(() => new MatchState(new PongConfig { TicksPerSecond = 0 }, TestCourt.Seed));
            Assert.Throws<ArgumentException>(() => new MatchState(new PongConfig { PaddleHeight = 20f }, TestCourt.Seed));
            Assert.Throws<ArgumentException>(() => new MatchState(new PongConfig { MaxBallSpeed = 1f }, TestCourt.Seed));
        }

        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NaN)]
        public void NonFiniteConfig_IsRejected(float value)
        {
            Assert.Throws<ArgumentException>(() => new MatchState(new PongConfig { MaxBallSpeed = value }, TestCourt.Seed));
            Assert.Throws<ArgumentException>(() => new MatchState(new PongConfig { SpeedIncreasePerHit = value }, TestCourt.Seed));
            Assert.Throws<ArgumentException>(() => new MatchState(new PongConfig { PaddleSpeed = value }, TestCourt.Seed));
        }

        [Test]
        public void BallFasterThanPaddleGapPerTick_IsRejected()
        {
            // コート幅 4 だと左右のパドル面の間隔は 2.3。1 Tick に約 11.7 進むと、中央で水平に往復して 1 Tick に 5 回当たりうる。
            var config = new PongConfig
            {
                FieldWidth = 4f,
                FieldHeight = 100f,
                PaddleHeight = 30f,
                ServeSpeed = 700f,
                MaxBallSpeed = 700f,
                MaxServeAngleDegrees = 0f,
                SpeedIncreasePerHit = 0f,
            };

            Assert.That(config.MaxTravelPerTick, Is.EqualTo(2.3f).Within(1e-4f));
            Assert.Throws<ArgumentException>(() => new MatchState(config, TestCourt.Seed));
        }

        [Test]
        public void MaxTravelPerTick_IsExclusiveUpperBound()
        {
            var config = new PongConfig();
            var limitSpeed = config.MaxTravelPerTick * config.TicksPerSecond;

            config.MaxBallSpeed = limitSpeed * 0.999f;
            Assert.DoesNotThrow(() => config.Validate(), "上限のわずかに下は受け入れる");

            config.MaxBallSpeed = limitSpeed;
            Assert.Throws<ArgumentException>(() => config.Validate(), "上限ちょうどは拒否する");
        }

        // 誰も動かずに最初の得点が入るまで進め、失点した側を返す。
        private static Side PlayUntilFirstPoint(MatchState match, out Side scorer)
        {
            TestCourt.SkipServe(match);
            for (var i = 0; i < TestCourt.MaxTicksPerMatch; i++)
            {
                match.Tick(0f, 0f);
                var scored = match.Events.Where(e => e.Kind == MatchEventKind.Scored).ToList();
                if (scored.Count > 0)
                {
                    scorer = scored[0].Side;
                    return scorer.Opponent();
                }
            }

            throw new InvalidOperationException("得点が入りませんでした");
        }
    }
}
