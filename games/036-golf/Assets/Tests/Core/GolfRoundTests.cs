using System;
using IntGeometry;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>ラウンドの進行・打数（題材固有テストの「OB 後の打数加算」）・予測と実際の一致。</summary>
    public sealed class GolfRoundTests
    {
        private static readonly GolfConfig _config = new();

        /// <summary>クラブと向きを決め、メーターの <paramref name="powerTick"/> でパワー、理想 + <paramref name="error"/> でインパクト。打った後の出来事を返す。</summary>
        internal static RoundEvents Swing(GolfRound round, int club, int powerTick, int error = 0, int? aim = null)
        {
            Assert.That(round.Phase, Is.EqualTo(RoundPhase.Aiming));
            Assert.That(round.SelectClub(club), Is.True);
            if (aim != null) { round.SetAim(aim.Value); }
            var events = RoundEvents.None;
            Assert.That(round.Press(), Is.True);
            var ideal = round.Config.IdealImpactTick;
            while (round.Phase == RoundPhase.Swinging)
            {
                var meter = round.Meter!;
                if (!meter.PowerSet && meter.T == powerTick) { Assert.That(round.Press(), Is.True); }
                else if (meter.PowerSet && meter.T == ideal + error) { round.Press(); }
                if (round.Phase == RoundPhase.Swinging) { events |= round.Step(); }
            }

            return events | round.TakeEvents();
        }

        /// <summary>次の構え（または終わり）まで進める。</summary>
        internal static RoundEvents Settle(GolfRound round)
        {
            var events = RoundEvents.None;
            while (round.Phase != RoundPhase.Aiming && round.Phase != RoundPhase.Finished) { events |= round.Step(); }
            return events;
        }

        /// <summary>風のない検証用のラウンド。</summary>
        private static GolfRound LabRound(string extra) => new(new GolfConfig { MaxWindSteps = 0 }, LabCourse(extra), 1);

        [Test]
        public void TeeShotIntoOutOfBoundsAddsAPenaltyAndReplaysFromTheTee()
        {
            // ウェッジは約 98 m で着地する。100〜120 m は OB。
            var round = LabRound("paint ob rect 100 0 120 60\n");
            Swing(round, Clubs.Wedge, _config.MeterHalfTicks, aim: 0);
            var events = Settle(round);
            Assert.That(events & RoundEvents.Penalty, Is.EqualTo(RoundEvents.Penalty));
            Assert.That(round.Strokes, Is.EqualTo(2), "打った 1 打＋罰打 1");
            Assert.That(round.Penalties, Is.EqualTo(1));
            Assert.That(round.Lie, Is.EqualTo(round.Hole.Tee));
            Assert.That(round.LastOutcome, Is.EqualTo(ShotOutcome.OutOfBounds));
        }

        [Test]
        public void WaterIsHandledLikeOutOfBounds()
        {
            var round = LabRound("paint water rect 100 0 120 60\n");
            Swing(round, Clubs.Wedge, _config.MeterHalfTicks, aim: 0);
            Settle(round);
            Assert.That(round.Strokes, Is.EqualTo(2));
            Assert.That(round.Lie, Is.EqualTo(round.Hole.Tee));
            Assert.That(round.LastOutcome, Is.EqualTo(ShotOutcome.Water));
        }

        [Test]
        public void FlyingOverOutOfBoundsAndLandingInPlayHasNoPenalty()
        {
            // ウッドの 9 割弱のパワーは約 140 m 飛ぶので、100〜120 m の OB を越え、200 m のホールの中で止まる。
            var round = LabRound("paint ob rect 100 0 120 60\n");
            Swing(round, Clubs.Wood, 40, aim: 0);
            var events = Settle(round);
            Assert.That(events & RoundEvents.Penalty, Is.EqualTo(RoundEvents.None));
            Assert.That(round.Strokes, Is.EqualTo(1));
            Assert.That(round.Lie.X, Is.GreaterThan(120 * M));
        }

        [Test]
        public void PenaltyIsCountedOncePerShot()
        {
            var round = LabRound("paint ob rect 100 0 120 60\n");
            Swing(round, Clubs.Wedge, _config.MeterHalfTicks, aim: 0);
            var penalties = 0;
            while (round.Phase != RoundPhase.Aiming)
            {
                if ((round.Step() & RoundEvents.Penalty) != 0) { penalties++; }
            }

            Assert.That(penalties, Is.EqualTo(1));
            Assert.That(round.Shots.Count, Is.EqualTo(1));
        }

        [Test]
        public void ConsecutiveOutOfBoundsReachTheStrokeCapAndEndTheHole()
        {
            var round = LabRound("paint ob rect 100 0 120 60\n");
            var cap = _config.StrokeCap(round.Hole.Par);
            for (var shot = 1; shot <= 4; shot++)
            {
                Swing(round, Clubs.Wedge, _config.MeterHalfTicks, aim: 0);
                Settle(round);
                Assert.That(round.Strokes, Is.EqualTo(shot * 2));
                Assert.That(round.HoleIndex, Is.EqualTo(0));
            }

            // 5 打目は 9 打目で、罰打を足すと上限を超える → 上限で打ち切り。
            Swing(round, Clubs.Wedge, _config.MeterHalfTicks, aim: 0);
            var events = RoundEvents.None;
            while (round.Phase != RoundPhase.HoleOut) { events |= round.Step(); }
            Assert.That(events & RoundEvents.GaveUp, Is.EqualTo(RoundEvents.GaveUp));
            Assert.That(round.ScoreOf(0), Is.EqualTo(cap));
            Assert.That(round.GaveUp, Is.True);
        }

        [Test]
        public void IdealSwingMatchesThePredictionEveryTick()
        {
            // 背の高い木を打球の線上に置き、木に当たった後の計算し直しまで予測と照合する。
            var round = LabRound("tree 60 30 2 25\n");
            var points = new Vec2i[_config.MaxShotTicks + 1];
            var heights = new int[points.Length];
            Assert.That(round.SelectClub(Clubs.Iron), Is.True);
            round.SetAim(0);
            var preview = round.Preview(points, heights);
            Assert.That(preview.Landed, Is.True);

            var events = Swing(round, Clubs.Iron, _config.MeterHalfTicks, aim: 0);
            Assert.That(events & RoundEvents.Launched, Is.EqualTo(RoundEvents.Launched));
            var tick = 0;
            Assert.That(round.Ball.Position, Is.EqualTo(points[tick]));
            while (round.Phase == RoundPhase.Moving && tick + 1 < preview.Count)
            {
                events |= round.Step();
                tick++;
                Assert.That(round.Ball.Position, Is.EqualTo(points[tick]), $"Tick {tick}");
                Assert.That(round.Ball.Z, Is.EqualTo(heights[tick]), $"Tick {tick}");
            }

            Assert.That(events & RoundEvents.TreeHit, Is.EqualTo(RoundEvents.TreeHit));
            Settle(round);
            var shot = round.Shots[0];
            Assert.That(shot.Actual.Landing, Is.EqualTo(preview.Landing));
            Assert.That(shot.Predicted, Is.EqualTo(shot.Actual));
            Assert.That(shot.Difference, Is.EqualTo(0));
        }

        [TestCase(3)]
        [TestCase(-3)]
        public void ImpactErrorBendsTheShotAwayFromThePrediction(int error)
        {
            var round = LabRound("");
            Swing(round, Clubs.Iron, _config.MeterHalfTicks, error, aim: 0);
            Settle(round);
            var shot = round.Shots[0];
            var side = shot.Actual.Landing.Y - shot.Predicted.Landing.Y;
            Assert.That(Math.Sign(side), Is.EqualTo(-Math.Sign(error)), "遅い（正）と右（−y）、早い（負）と左（+y）");
            Assert.That(shot.Difference, Is.EqualTo(IntMath.Sqrt(Vec2i.DistanceSquared(shot.Actual.Landing, shot.Predicted.Landing))));
            Assert.That(shot.Difference, Is.GreaterThan(M));
        }

        [Test]
        public void CanceledSwingDoesNotCountAStroke()
        {
            var round = Round();
            Assert.That(round.Press(), Is.True);
            var events = RoundEvents.None;
            while (round.Phase == RoundPhase.Swinging) { events |= round.Step(); }
            Assert.That(events & RoundEvents.SwingCanceled, Is.EqualTo(RoundEvents.SwingCanceled));
            Assert.That(round.Phase, Is.EqualTo(RoundPhase.Aiming));
            Assert.That(round.Strokes, Is.EqualTo(0));

            Assert.That(round.Press(), Is.True);
            Assert.That(round.CancelSwing(), Is.True);
            Assert.That(round.Strokes, Is.EqualTo(0));
            Assert.That(round.CancelSwing(), Is.False);
        }

        [Test]
        public void MishitHalvesThePowerAndBendsByTheFullWindow()
        {
            var round = LabRound("");
            var events = Swing(round, Clubs.Iron, _config.MeterHalfTicks, _config.ImpactWindow + 5, aim: 0);
            Assert.That(events & RoundEvents.Mishit, Is.EqualTo(RoundEvents.Mishit));
            Settle(round);
            var shot = round.Shots[0];
            Assert.That(shot.Setup.Mishit, Is.True);
            Assert.That(shot.Setup.Error, Is.EqualTo(_config.ImpactWindow));
            Assert.That(shot.Actual.Landing.X - round.Hole.Tee.X, Is.LessThan(round.CarryOf(Clubs.Iron) / 2));
        }

        [Test]
        public void OperationsOutsideTheirPhaseAreRejected()
        {
            var round = Round();
            Assert.That(round.SetImpact(3, 0), Is.False);
            Assert.That(round.SelectClub(-1), Is.False);
            Assert.That(round.SelectClub(Clubs.Count), Is.False);
            Assert.That(round.CancelSwing(), Is.False);
            Swing(round, Clubs.Wood, _config.MeterHalfTicks);
            Assert.That(round.Phase, Is.EqualTo(RoundPhase.Moving));
            Assert.That(round.TurnAim(1), Is.False);
            Assert.That(round.SelectClub(Clubs.Iron), Is.False);
            Assert.That(round.SetImpact(0, 1), Is.False);
            Assert.That(round.Press(), Is.False);
        }

        [Test]
        public void OnTheGreenOnlyThePutterCanBeUsed()
        {
            // アイアンの最大は約 160 m 先で止まる（グリーンは 155〜185 m）。
            var round = LabRound("");
            Swing(round, Clubs.Iron, _config.MeterHalfTicks, aim: 0);
            Settle(round);
            Assert.That(round.LieSurface, Is.EqualTo(Surface.Green));

            Assert.That(round.Club, Is.EqualTo(Clubs.Putter));
            Assert.That(round.SelectClub(Clubs.Wedge), Is.False);
            Assert.That(round.ShiftClub(-1), Is.False);
        }

        [Test]
        public void AimTurnsByTheClubStep()
        {
            var round = Round();
            var start = round.Aim;
            Assert.That(round.TurnAim(2), Is.True);
            Assert.That(round.Aim, Is.EqualTo(Trig.Normalize(start + (2 * _config.AimStep))));
            Assert.That(round.SelectClub(Clubs.Putter), Is.True);
            Assert.That(round.TurnAim(-1), Is.True);
            Assert.That(round.Aim, Is.EqualTo(Trig.Normalize(start + (2 * _config.AimStep) - _config.PuttAimStep)));
        }

        [Test]
        public void ReadyAimsAtTheCupWithTheShortestClubThatReaches()
        {
            var round = Round();
            var toCup = round.Hole.Cup - round.Lie;
            Assert.That(round.Aim, Is.EqualTo(Trig.Atan2(toCup.Y, toCup.X, 0)));
            Assert.That(round.Club, Is.EqualTo(Clubs.Wood), "1 番（約 156 m）はアイアン（約 144 m）では届かない");
            Assert.That(round.DistanceToCup, Is.GreaterThan(round.CarryOf(Clubs.Iron)));
            Assert.That(round.CarryOf(Clubs.Wood), Is.GreaterThan(round.CarryOf(Clubs.Iron)));
            Assert.That(round.CarryOf(Clubs.Iron), Is.GreaterThan(round.CarryOf(Clubs.Wedge)));
        }

        [Test]
        public void WindIsFixedPerHoleBySeed()
        {
            var a = Round(5);
            var b = Round(5);
            for (var i = 0; i < Course.HoleCount; i++) { Assert.That(a.WindOf(i), Is.EqualTo(b.WindOf(i))); }
            var differs = false;
            for (var seed = 1; seed < 30 && !differs; seed++)
            {
                differs = GolfRound.WindFor(_config, seed, 0) != GolfRound.WindFor(_config, seed + 1, 0);
            }

            Assert.That(differs, Is.True);
            foreach (var seed in new[] { 1, 2, 3, int.MinValue, int.MaxValue })
            {
                for (var hole = 0; hole < 3; hole++)
                {
                    var wind = GolfRound.WindFor(_config, seed, hole);
                    Assert.That(wind.LengthSquared, Is.LessThanOrEqualTo((long)_config.MaxWindSteps * _config.WindStep * _config.MaxWindSteps * _config.WindStep));
                }
            }
        }

        [Test]
        public void SameSeedAndOperationsGiveTheSameHash()
        {
            var a = Round(3);
            var b = Round(3);
            for (var i = 0; i < 2; i++)
            {
                Swing(a, Clubs.Iron, 40, 1);
                Swing(b, Clubs.Iron, 40, 1);
                Settle(a);
                Settle(b);
                Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            }

            Assert.That(Round(3).Hash(), Is.Not.EqualTo(Round(4).Hash()));
        }

        [Test]
        public void HashSeparatesStatesThatLookTheSameNowButDivergeLater()
        {
            // 同じ位置で同じ Tick 数でも、メーターのパワーを決めたかで以後が分かれる。
            var a = Round();
            var b = Round();
            a.Press();
            b.Press();
            for (var i = 0; i < 10; i++)
            {
                a.Step();
                b.Step();
            }

            b.Press();
            Assert.That(a.Ball, Is.EqualTo(b.Ball));
            Assert.That(a.Hash(), Is.Not.EqualTo(b.Hash()));

            // 打点だけが違う。
            var c = Round();
            var d = Round();
            d.SetImpact(0, -1);
            Assert.That(c.Hash(), Is.Not.EqualTo(d.Hash()));

            // 設定が違う。
            var e = new GolfRound(new GolfConfig { Drag = 151 }, Production(), 1);
            Assert.That(e.Hash(), Is.Not.EqualTo(Round().Hash()));
        }

        [Test]
        public void CloneIsIndependent()
        {
            var round = Round();
            Swing(round, Clubs.Iron, 40);
            var copy = round.Clone();
            var hash = round.Hash();
            Settle(copy);
            Assert.That(round.Hash(), Is.EqualTo(hash));
            Assert.That(round.Shots.Count, Is.EqualTo(0));
            Assert.That(copy.Shots.Count, Is.EqualTo(1));
        }

        [Test]
        public void FinishedRoundIgnoresEverything()
        {
            var round = new GolfRound(_config, LabCourse("paint ob rect 100 0 120 60\n"), 1);
            while (round.Phase != RoundPhase.Finished)
            {
                if (round.Phase == RoundPhase.Aiming) { Swing(round, Clubs.Wedge, _config.MeterHalfTicks, aim: 0); }
                round.Step();
            }

            Assert.That(round.Total, Is.EqualTo(3 * _config.StrokeCap(4)));
            var hash = round.Hash();
            Assert.That(round.Press(), Is.False);
            Assert.That(round.TurnAim(1), Is.False);
            Assert.That(round.SelectClub(Clubs.Wood), Is.False);
            Assert.That(round.SetImpact(1, 1), Is.False);
            Assert.That(round.CancelSwing(), Is.False);
            Assert.That(round.Step(), Is.EqualTo(RoundEvents.None));
            Assert.That(round.Hash(), Is.EqualTo(hash));
        }

        [Test]
        public void ConfigChangedAfterConstructionDoesNotAffectTheRound()
        {
            var config = new GolfConfig();
            var round = new GolfRound(config, Production(), 1);
            var hash = round.Hash();
            config.Drag = 1000;
            config.ClubSpeed[0] = 1000;
            Assert.That(round.Hash(), Is.EqualTo(hash));
            Assert.That(round.Config.ClubSpeed[0], Is.EqualTo(new GolfConfig().ClubSpeed[0]));
        }
    }
}
