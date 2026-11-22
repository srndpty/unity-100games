using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有：届かない球は打てない。早打ちと遅打ちで球筋が変わる。</summary>
    public sealed class HitWindowTests
    {
        private static Vec3 Me => new(0, -110000, 0);

        /// <summary>理想の打点（届く範囲の中心）。</summary>
        private static Vec3 Ideal => HitWindow.IdealContact(Side.Near, Me, TestKit.Config());

        private static HitCheck Check(Vec3 ball, Vec3? velocity = null, bool incoming = true, bool unbouncedServe = false)
        {
            var athlete = new Athlete(Side.Near, Me);
            var b = new Ball(ball, velocity ?? new Vec3(0, -3000, 0));
            return HitWindow.Check(athlete, b, incoming, unbouncedServe, TestKit.Config());
        }

        [Test]
        public void BallAtTheEdgeOfReachCanBeHit()
        {
            var reach = TestKit.Config().Reach;
            Assert.That(Check(new Vec3(reach, Ideal.Y, 5000)), Is.EqualTo(HitCheck.Ok));
            Assert.That(Check(new Vec3(0, Ideal.Y + reach, 5000)), Is.EqualTo(HitCheck.Ok), "早打ち側の端");
            Assert.That(Check(new Vec3(0, Ideal.Y - reach, 5000)), Is.EqualTo(HitCheck.Ok), "遅打ち側の端");
        }

        [Test]
        public void BallJustOutOfReachCannotBeHit()
        {
            var reach = TestKit.Config().Reach;
            Assert.That(Check(new Vec3(reach + 1, Ideal.Y, 5000)), Is.EqualTo(HitCheck.TooFar));
            Assert.That(Check(new Vec3(-reach - 1, Ideal.Y, 5000)), Is.EqualTo(HitCheck.TooFar));
            Assert.That(Check(new Vec3(0, Ideal.Y - reach - 1, 5000)), Is.EqualTo(HitCheck.TooFar));
            Assert.That(Check(new Vec3(0, Ideal.Y + reach + 1, 5000)), Is.EqualTo(HitCheck.TooFar));
        }

        [Test]
        public void HeightLimitsAreInclusive()
        {
            var config = TestKit.Config();
            Assert.That(Check(new Vec3(0, Me.Y, config.MinHitZ)), Is.EqualTo(HitCheck.Ok));
            Assert.That(Check(new Vec3(0, Me.Y, config.MaxHitZ)), Is.EqualTo(HitCheck.Ok));
            Assert.That(Check(new Vec3(0, Me.Y, config.MinHitZ - 1)), Is.EqualTo(HitCheck.TooLow));
            Assert.That(Check(new Vec3(0, Me.Y, config.MaxHitZ + 1)), Is.EqualTo(HitCheck.TooHigh));
        }

        [Test]
        public void BallMovingAwayOrOnTheOtherSideCannotBeHit()
        {
            Assert.That(Check(new Vec3(0, Me.Y, 5000), new Vec3(0, 3000, 0)), Is.EqualTo(HitCheck.NotIncoming), "離れていく球");
            Assert.That(Check(new Vec3(0, 5000, 5000)), Is.EqualTo(HitCheck.NotIncoming), "ネットの向こう");
        }

        [Test]
        public void OwnShotAndUnbouncedServeCannotBeHit()
        {
            Assert.That(Check(new Vec3(0, Me.Y, 5000), incoming: false), Is.EqualTo(HitCheck.NotInPlay));
            Assert.That(Check(new Vec3(0, Me.Y, 5000), unbouncedServe: true), Is.EqualTo(HitCheck.UnbouncedServe));
        }

        [Test]
        public void TimingIsPositiveWhenTheBallIsAheadOfTheIdealContact()
        {
            var config = TestKit.Config();
            var athlete = new Athlete(Side.Near, Me);
            var ideal = HitWindow.IdealContact(Side.Near, Me, config);
            Assert.That(HitWindow.Timing(athlete, ideal, config), Is.Zero);
            Assert.That(HitWindow.Timing(athlete, ideal + new Vec3(0, config.TimingSpan / 2, 0), config), Is.EqualTo(500));
            Assert.That(HitWindow.Timing(athlete, ideal - new Vec3(0, config.TimingSpan * 3, 0), config), Is.EqualTo(-HitWindow.MaxTiming), "最大で打ち切る");

            var far = new Athlete(Side.Far, new Vec3(0, 110000, 0));
            var farIdeal = HitWindow.IdealContact(Side.Far, far.Position, config);
            Assert.That(farIdeal.Y, Is.LessThan(far.Position.Y), "奥の選手の前はネットの向き（y が小さい）");
            Assert.That(HitWindow.Timing(far, farIdeal - new Vec3(0, config.TimingSpan / 2, 0), config), Is.EqualTo(500));
        }

        // ---- 試合の中で：振っても届かない球には当たらない ----

        [Test]
        public void SwingAtAnUnreachableBallWhiffs()
        {
            var config = TestKit.Config();
            var match = Incoming(config, nearX: config.Reach * 3);
            match.TakeEvents();
            SwingAndWaitForImpact(match);
            var events = match.TakeEvents();
            Assert.That(events & MatchEvents.Whiff, Is.Not.EqualTo(MatchEvents.None));
            Assert.That(events & MatchEvents.Stroke, Is.EqualTo(MatchEvents.None));
            Assert.That(match.Rally.LastHitter, Is.EqualTo(Side.Far), "打っていない");
        }

        [Test]
        public void SwingAtAReachableBallReturnsIt()
        {
            var config = TestKit.Config();
            var match = Incoming(config, nearX: 0);
            match.TakeEvents();
            SwingAndWaitForImpact(match);
            Assert.That(match.TakeEvents() & MatchEvents.Stroke, Is.Not.EqualTo(MatchEvents.None));
            Assert.That(match.Rally.LastHitter, Is.EqualTo(Side.Near));
            Assert.That(match.Ball.Velocity.Y, Is.GreaterThan(0), "奥へ向かう");
        }

        [Test]
        public void SwingStartedWhileSwingingIsIgnored()
        {
            var config = TestKit.Config();
            var match = Incoming(config, nearX: 0);
            match.Step(TestKit.Press(), AthleteCommand.Idle);
            match.Step(AthleteCommand.Idle, AthleteCommand.Idle);
            var tick = match.Near.SwingTick;
            match.Step(TestKit.Press(ShotKind.Lob), AthleteCommand.Idle);
            Assert.That(match.Near.SwingTick, Is.EqualTo(tick + 1), "振り直さない");
            Assert.That(match.Near.SwingKind, Is.EqualTo(ShotKind.Stroke));
        }

        // ---- 早打ち・遅打ち ----

        [Test]
        public void EarlyAndLateHitsSendTheBallToDifferentSides()
        {
            var config = TestKit.Config();
            var onTime = HitAt(config, 0, out var onTimeTiming);
            var early = HitAt(config, -2, out var earlyTiming);
            var late = HitAt(config, 2, out var lateTiming);

            Assert.That(earlyTiming, Is.GreaterThan(onTimeTiming));
            Assert.That(lateTiming, Is.LessThan(onTimeTiming));

            // 手前の選手の左は x が負。早打ちは左（引っ張り）、遅打ちは右へ。
            Assert.That(early.X, Is.LessThan(onTime.X));
            Assert.That(late.X, Is.GreaterThan(onTime.X));
            Assert.That(early.Y, Is.GreaterThan(onTime.Y), "早打ちは長くなる");
        }

        [Test]
        public void ZeroTimingLandsOnTheAimedSpot()
        {
            var config = TestKit.Config();
            var contact = new Vec3(5000, -110000, 8000);
            foreach (var aim in new[] { -1, 0, 1 })
            {
                var target = Shots.Target(Side.Near, ShotKind.Stroke, aim, 0, ServeCourt.Deuce, config);
                var landing = TestKit.FlyToFirstEvent(new Ball(contact, Shots.Plan(Side.Near, ShotKind.Stroke, aim, 0, contact, ServeCourt.Deuce, config)), config);
                Assert.That(landing.Kind, Is.EqualTo(BallEventKind.Bounce));
                Assert.That(System.Math.Abs(landing.Point.X - target.X), Is.LessThan(1000), $"狙い {aim}");
                Assert.That(System.Math.Abs(landing.Point.Y - target.Y), Is.LessThan(1000), $"狙い {aim}");
            }
        }

        [Test]
        public void LobFliesHigherThanStroke()
        {
            var config = TestKit.Config();
            var contact = new Vec3(0, -110000, 8000);
            Assert.That(Apex(contact, ShotKind.Lob, config), Is.GreaterThan(Apex(contact, ShotKind.Stroke, config) + 20000));
        }

        [Test]
        public void MaxLateTimingNetsAStroke()
        {
            var config = TestKit.Config();
            var contact = new Vec3(0, -110000, 8000);
            var ball = new Ball(contact, Shots.Plan(Side.Near, ShotKind.Stroke, 0, -HitWindow.MaxTiming, contact, ServeCourt.Deuce, config));
            Assert.That(TestKit.FlyToFirstEvent(ball, config).Kind, Is.EqualTo(BallEventKind.Net));
        }

        [Test]
        public void MaxEarlyTimingSendsAStrokeLong()
        {
            var config = TestKit.Config();
            var contact = new Vec3(0, -110000, 8000);
            var ball = new Ball(contact, Shots.Plan(Side.Near, ShotKind.Stroke, 0, HitWindow.MaxTiming, contact, ServeCourt.Deuce, config));
            var e = TestKit.FlyToFirstEvent(ball, config);
            Assert.That(e.Kind, Is.EqualTo(BallEventKind.Bounce));
            Assert.That(Court.InSingles(e.Point.X, e.Point.Y, Side.Far), Is.False);
        }

        private static int Apex(Vec3 contact, ShotKind kind, TennisConfig config)
        {
            var ball = new Ball(contact, Shots.Plan(Side.Near, kind, 0, 0, contact, ServeCourt.Deuce, config));
            var apex = contact.Z;
            while (ball.Step(config.Physics).Kind == BallEventKind.None) { apex = System.Math.Max(apex, ball.Position.Z); }
            return apex;
        }

        /// <summary>奥から手前の選手へ向かう打ち合いの場面。手前の選手は x = nearX、球は選手の正面に来る。</summary>
        private static TennisMatch Incoming(TennisConfig config, int nearX)
        {
            var match = new TennisMatch(config, seed: 1);
            var near = new Vec3(nearX, -112000, 0);
            var from = new Vec3(0, 100000, 8000);
            var velocity = TestKit.VelocityTo(from, 0, -80000, config);
            match.StageRally(Side.Far, from, velocity, near, from.WithZ(0));
            return match;
        }

        /// <summary>理想の打点の Tick に打点が来るよう振り、打点の Tick まで進める。</summary>
        private static void SwingAndWaitForImpact(TennisMatch match, int shift = 0)
        {
            var config = match.Config;
            var contactTick = IdealContactTick(match);
            var pressAt = contactTick - config.ImpactTicks + shift;
            TestKit.Idle(match, pressAt - 1);
            match.Step(TestKit.Press(), AthleteCommand.Idle);
            TestKit.Idle(match, config.ImpactTicks);
        }

        /// <summary>球が手前の選手の理想の打点（の y）を通り過ぎる Tick（今から何 Tick 後か）。</summary>
        private static int IdealContactTick(TennisMatch match)
        {
            var config = match.Config;
            var idealY = HitWindow.IdealContact(Side.Near, match.Near.Position, config).Y;
            var copy = match.Ball.Clone();
            for (var tick = 1; tick < 600; tick++)
            {
                copy.Step(config.Physics);
                if (copy.Position.Y <= idealY) { return tick; }
            }

            throw new System.InvalidOperationException("球が来ません。");
        }

        /// <summary>理想の Tick から shift ずらして打ち、着地点と打点のずれを返す。</summary>
        private static Vec3 HitAt(TennisConfig config, int shift, out int timing)
        {
            var match = Incoming(config, nearX: 0);
            SwingAndWaitForImpact(match, shift);
            Assert.That(match.Rally.LastHitter, Is.EqualTo(Side.Near), $"ずらし {shift} で打てる");
            timing = match.LastTiming;
            TestKit.IdleUntil(match, m => m.LastBounce.HasValue && m.LastBounce.Value.Y > 0);
            return match.LastBounce!.Value;
        }
    }
}
