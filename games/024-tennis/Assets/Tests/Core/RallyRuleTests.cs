using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有：ネット・アウト・ツーバウンドで正しく失点する。</summary>
    public sealed class RallyRuleTests
    {
        private static Rally InPlayFromNear() => Rally.Staged(Side.Near, ServeCourt.Deuce, lastHitter: Side.Near);

        private static BallEvent Bounce(int x, int y) => new(BallEventKind.Bounce, new Vec3(x, y, 0));

        [Test]
        public void NetLosesThePointForTheHitter()
        {
            var rally = InPlayFromNear();
            Assert.That(rally.OnBall(new BallEvent(BallEventKind.Net, new Vec3(0, 0, 5000))), Is.EqualTo(Verdict.Net));
            Assert.That(rally.Winner, Is.EqualTo(Side.Far));
            Assert.That(rally.Phase, Is.EqualTo(RallyPhase.Over));
        }

        [TestCase(Court.HalfWidth, Court.HalfLength)]
        [TestCase(-Court.HalfWidth, Court.HalfLength)]
        [TestCase(0, 1)]
        public void LinesAreIn(int x, int y)
        {
            var rally = InPlayFromNear();
            Assert.That(rally.OnBall(Bounce(x, y)), Is.EqualTo(Verdict.None));
            Assert.That(rally.Bounces, Is.EqualTo(1));
        }

        [TestCase(Court.HalfWidth + 1, 50000)]
        [TestCase(-Court.HalfWidth - 1, 50000)]
        [TestCase(0, Court.HalfLength + 1)]
        [TestCase(0, -1)]
        public void BounceOutsideTheOpponentsCourtLosesForTheHitter(int x, int y)
        {
            var rally = InPlayFromNear();
            Assert.That(rally.OnBall(Bounce(x, y)), Is.EqualTo(Verdict.Out));
            Assert.That(rally.Winner, Is.EqualTo(Side.Far));
        }

        [Test]
        public void SecondBounceLosesForTheReceiver()
        {
            var rally = InPlayFromNear();
            rally.OnBall(Bounce(0, 80000));
            Assert.That(rally.OnBall(Bounce(0, 110000)), Is.EqualTo(Verdict.DoubleBounce));
            Assert.That(rally.Winner, Is.EqualTo(Side.Near));
        }

        [Test]
        public void SecondBounceOutsideTheCourtStillLosesForTheReceiver()
        {
            var rally = InPlayFromNear();
            rally.OnBall(Bounce(0, 80000));
            Assert.That(rally.OnBall(Bounce(0, Court.HalfLength + 30000)), Is.EqualTo(Verdict.DoubleBounce));
            Assert.That(rally.Winner, Is.EqualTo(Side.Near));
        }

        [Test]
        public void BouncedBallPassingTheFenceLosesForTheReceiver()
        {
            var rally = InPlayFromNear();
            Assert.That(rally.OnPosition(new Vec3(0, Court.FenceY + 1, 5000)), Is.EqualTo(Verdict.None), "弾む前は壁の線を越えても判定しない");
            rally.OnBall(Bounce(0, 80000));
            Assert.That(rally.OnPosition(new Vec3(0, Court.FenceY, 5000)), Is.EqualTo(Verdict.None));
            Assert.That(rally.OnPosition(new Vec3(0, Court.FenceY + 1, 5000)), Is.EqualTo(Verdict.Unreturned));
            Assert.That(rally.Winner, Is.EqualTo(Side.Near));
        }

        [Test]
        public void HittingResetsTheBounceCountAndSwapsTheTarget()
        {
            var rally = InPlayFromNear();
            rally.OnBall(Bounce(0, 80000));
            rally.Hit(Side.Far);
            Assert.That(rally.Bounces, Is.Zero);
            Assert.That(rally.Incoming, Is.EqualTo(Side.Near));
            Assert.That(rally.OnBall(Bounce(0, 80000)), Is.EqualTo(Verdict.Out), "打った側（奥）のコートに落ちたら打った側の失点");
            Assert.That(rally.Winner, Is.EqualTo(Side.Near));
        }

        [Test]
        public void SameHitterTwiceIsRejected()
        {
            var rally = InPlayFromNear();
            Assert.Throws<System.InvalidOperationException>(() => rally.Hit(Side.Near));
        }

        [Test]
        public void ServeMustLandInTheDiagonalBox()
        {
            // 手前のサーバーがデュースサイド（右、x > 0）から打つ → 奥のボックスの x ≤ 0 側。
            var rally = Served(ServeCourt.Deuce);
            Assert.That(rally.OnBall(Bounce(-Court.HalfWidth, Court.ServiceLine)), Is.EqualTo(Verdict.None), "ボックスの角（ライン上）は入り");
            Assert.That(rally.ServeInFlight, Is.False);

            Assert.That(Served(ServeCourt.Deuce).OnBall(Bounce(1, 30000)), Is.EqualTo(Verdict.Fault), "対角でない側");
            Assert.That(Served(ServeCourt.Deuce).OnBall(Bounce(-20000, Court.ServiceLine + 1)), Is.EqualTo(Verdict.Fault), "サービスラインの外");
            Assert.That(Served(ServeCourt.Ad).OnBall(Bounce(20000, 30000)), Is.EqualTo(Verdict.None), "アドサイドは x ≥ 0 側");
            Assert.That(Served(ServeCourt.Ad).OnBall(new BallEvent(BallEventKind.Net, new Vec3(0, 0, 1000))), Is.EqualTo(Verdict.Fault), "サーブのネットはフォルト");
        }

        [Test]
        public void SecondFaultIsADoubleFault()
        {
            var rally = Served(ServeCourt.Deuce);
            Assert.That(rally.OnBall(Bounce(20000, 30000)), Is.EqualTo(Verdict.Fault));
            Assert.That(rally.Phase, Is.EqualTo(RallyPhase.Ready));
            Assert.That(rally.ServeNumber, Is.EqualTo(2));
            Assert.That(rally.Winner, Is.Null);

            rally.Toss();
            rally.SwingAtToss(hit: true);
            Assert.That(rally.OnBall(Bounce(20000, 30000)), Is.EqualTo(Verdict.DoubleFault));
            Assert.That(rally.Winner, Is.EqualTo(Side.Far));
        }

        [Test]
        public void DroppedTossIsNotAFault()
        {
            var rally = new Rally(Side.Near, ServeCourt.Deuce);
            rally.Toss();
            rally.DropToss();
            Assert.That(rally.Phase, Is.EqualTo(RallyPhase.Ready));
            Assert.That(rally.ServeNumber, Is.EqualTo(1));
        }

        [Test]
        public void SwingingTwiceAtOneTossIsRejected()
        {
            var rally = new Rally(Side.Near, ServeCourt.Deuce);
            rally.Toss();
            rally.SwingAtToss(hit: false);
            Assert.Throws<System.InvalidOperationException>(() => rally.SwingAtToss(hit: true));
        }

        [Test]
        public void EventsAfterThePointAreRejected()
        {
            var rally = InPlayFromNear();
            rally.OnBall(new BallEvent(BallEventKind.Net, Vec3.Zero));
            Assert.Throws<System.InvalidOperationException>(() => rally.OnBall(Bounce(0, 80000)));
            Assert.Throws<System.InvalidOperationException>(() => rally.Hit(Side.Far));
        }

        // ---- 試合の中で（球を実際に飛ばして）----

        [Test]
        public void MatchAwardsNetToTheReceiver()
        {
            var config = TestKit.Config();
            var match = new TennisMatch(config, seed: 1);
            var from = new Vec3(0, -100000, 3000);
            match.StageRally(Side.Near, from, TestKit.VelocityTo(from, 0, 80000, config, clearance: -4000), from.WithZ(0), new Vec3(0, 120000, 0));
            TestKit.IdleUntil(match, m => m.Phase == MatchPhase.Gap);
            Assert.That(match.LastVerdict, Is.EqualTo(Verdict.Net));
            Assert.That(match.LastPointWinner, Is.EqualTo(Side.Far));
            Assert.That(match.Score.Points(Side.Far), Is.EqualTo(1));
        }

        [Test]
        public void MatchAwardsLongBallToTheReceiver()
        {
            var config = TestKit.Config();
            var match = new TennisMatch(config, seed: 1);
            var from = new Vec3(0, -100000, 10000);
            match.StageRally(Side.Near, from, TestKit.VelocityTo(from, 0, Court.HalfLength + 20000, config), from.WithZ(0), new Vec3(0, 120000, 0));
            TestKit.IdleUntil(match, m => m.Phase == MatchPhase.Gap);
            Assert.That(match.LastVerdict, Is.EqualTo(Verdict.Out));
            Assert.That(match.LastPointWinner, Is.EqualTo(Side.Far));
        }

        [Test]
        public void MatchAwardsUnreturnedBallToTheHitter()
        {
            var config = TestKit.Config();
            var match = new TennisMatch(config, seed: 1);
            var from = new Vec3(0, -100000, 10000);
            match.StageRally(Side.Near, from, TestKit.VelocityTo(from, 0, 90000, config), from.WithZ(0), new Vec3(30000, 120000, 0));
            TestKit.IdleUntil(match, m => m.Phase == MatchPhase.Gap);
            Assert.That(match.LastVerdict, Is.EqualTo(Verdict.DoubleBounce).Or.EqualTo(Verdict.Unreturned));
            Assert.That(match.LastPointWinner, Is.EqualTo(Side.Near));
            Assert.That(match.Score.Points(Side.Near), Is.EqualTo(1));
        }

        [Test]
        public void OnTimeServeLandsInTheBoxAndUnansweredServeIsAnAce()
        {
            var match = new TennisMatch(TestKit.Config(), seed: 1);
            TestKit.NearServe(match, offset: 0, aim: 0);
            Assert.That(match.Rally.ServeInFlight, Is.True);
            TestKit.IdleUntil(match, m => m.Phase == MatchPhase.Gap);
            Assert.That(match.LastPointWinner, Is.EqualTo(Side.Near));
            Assert.That(match.LastBounce.HasValue, Is.True);
        }

        [Test]
        public void LateServesNetTwiceForADoubleFault()
        {
            var config = TestKit.Config();
            var match = new TennisMatch(config, seed: 1);
            TestKit.NearServe(match, offset: config.ServeTimingSpan);
            TestKit.IdleUntil(match, m => m.Rally.Phase == RallyPhase.Ready);
            Assert.That(match.LastVerdict, Is.EqualTo(Verdict.Fault));
            Assert.That(match.Rally.ServeNumber, Is.EqualTo(2));
            Assert.That(match.Score.Points(Side.Far), Is.Zero, "1 本目のフォルトは失点にならない");

            TestKit.NearServe(match, offset: config.ServeTimingSpan);
            TestKit.IdleUntil(match, m => m.Phase == MatchPhase.Gap);
            Assert.That(match.LastVerdict, Is.EqualTo(Verdict.DoubleFault));
            Assert.That(match.LastPointWinner, Is.EqualTo(Side.Far));
        }

        [Test]
        public void EarlyServeGoesLongForAFault()
        {
            var config = TestKit.Config();
            var match = new TennisMatch(config, seed: 1);
            TestKit.NearServe(match, offset: -config.ServeTimingSpan);
            TestKit.IdleUntil(match, m => m.Rally.Phase == RallyPhase.Ready);
            Assert.That(match.LastVerdict, Is.EqualTo(Verdict.Fault));
            Assert.That(match.LastBounce.HasValue, Is.True, "ネットではなく、ボックスの外に落ちた");
            Assert.That(match.LastBounce!.Value.Y, Is.GreaterThan(Court.ServiceLine));
        }

        [Test]
        public void DroppedTossInMatchIsRetossed()
        {
            var config = TestKit.Config();
            var match = new TennisMatch(config, seed: 1);
            match.Step(TestKit.Press(), AthleteCommand.Idle);
            Assert.That(match.Rally.Phase, Is.EqualTo(RallyPhase.Toss));
            TestKit.IdleUntil(match, m => m.Rally.Phase == RallyPhase.Ready);
            Assert.That(match.Rally.ServeNumber, Is.EqualTo(1));
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Point));
        }

        private static Rally Served(ServeCourt court)
        {
            var rally = new Rally(Side.Near, court);
            rally.Toss();
            rally.SwingAtToss(hit: true);
            return rally;
        }
    }
}
