using System.Linq;
using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有：得点はゴール通過時に一度だけ。</summary>
    public sealed class GoalTests
    {
        private static int R => Lab.Exact.BallRadius;

        [Test]
        public void RollingIntoTheGoal_ScoresOnce_AndTheBallStaysInTheNet()
        {
            var m = Lab.Open();
            m.PlaceBall(new Vec2i(Pitch.HalfLength - 3000, 300), new Vec2i(300, 0));
            m.SetLastTouch(Team.Blue);
            var events = Lab.Run(m, 60);
            Assert.That(Lab.Count(events, MatchEventKind.Goal), Is.EqualTo(1));
            Assert.That(m.Score(Team.Blue), Is.EqualTo(1));
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.Goal));
            Assert.That(m.Ball.Position.X, Is.LessThanOrEqualTo(Pitch.HalfLength + Pitch.GoalDepth), "ネットの奥で止まる");
            Assert.That(Lab.Count(events, MatchEventKind.Out), Is.EqualTo(0), "得点の Tick にライン外は重ならない");
        }

        [Test]
        public void LineExactly_IsNotAGoal_OneUnitBeyond_Is()
        {
            // 球の全体が越えたとき（中心 - 半径 > ライン）だけ得点。
            var m = Lab.Open();
            // 空中の球（転がる球は遅いと止まる）。高さはクロスバーより十分低い。
            m.PlaceBall(new Vec2i(Pitch.HalfLength + R - 1, 0), new Vec2i(1, 0), 500, 0);
            var first = Lab.Run(m, 1);
            Assert.That(Lab.Count(first, MatchEventKind.Goal), Is.EqualTo(0), "ちょうどラインの上");
            Assert.That(m.Ball.Position.X - R, Is.EqualTo(Pitch.HalfLength));
            var next = Lab.Run(m, 1);
            Assert.That(Lab.Count(next, MatchEventKind.Goal), Is.EqualTo(1), "1 単位越えた");
        }

        [Test]
        public void OverTheBar_OrOutsideThePosts_IsNotAGoal()
        {
            var high = Lab.Open();
            high.PlaceBall(new Vec2i(Pitch.HalfLength - 1000, 0), new Vec2i(300, 0), Pitch.CrossbarHeight + 300, 0);
            high.SetLastTouch(Team.Blue);
            var e1 = Lab.Run(high, 20);
            Assert.That(Lab.Count(e1, MatchEventKind.Goal), Is.EqualTo(0));
            Assert.That(e1.Single(e => e.Kind == MatchEventKind.Out).Value, Is.EqualTo((int)RestartKind.GoalKick), "攻めた側が最後に触れた");

            var wide = Lab.Open();
            wide.PlaceBall(new Vec2i(Pitch.HalfLength - 1000, Pitch.GoalHalfWidth + 400), new Vec2i(300, 0));
            wide.SetLastTouch(Team.Red);
            var e2 = Lab.Run(wide, 20);
            Assert.That(Lab.Count(e2, MatchEventKind.Goal), Is.EqualTo(0));
            var corner = e2.Single(e => e.Kind == MatchEventKind.Out);
            Assert.That(corner.Value, Is.EqualTo((int)RestartKind.Corner), "守った側が最後に触れた");
            Assert.That(corner.Team, Is.EqualTo(Team.Blue));
        }

        [Test]
        public void PostAndBackOut_IsNotAGoal()
        {
            var m = Lab.Open();
            var post = Pitch.Posts[3];
            m.PlaceBall(post - new Vec2i(2000, 0), new Vec2i(300, 0));
            var events = Lab.Run(m, 30);
            Assert.That(Lab.Count(events, MatchEventKind.Post), Is.EqualTo(1));
            Assert.That(Lab.Count(events, MatchEventKind.Goal), Is.EqualTo(0));
        }

        [Test]
        public void DuringTheGoalPause_NothingIsJudged_ThenTheConcedingTeamKicksOff()
        {
            var config = Lab.Exact;
            var m = Lab.Open(config);
            m.PlaceBall(new Vec2i(Pitch.HalfLength - 2000, 0), new Vec2i(300, 0));
            Lab.Run(m, 20);
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.Goal));

            // 得点の間に球を動かしても（ラインを戻っても）判定しない。
            m.PlaceBall(new Vec2i(Pitch.HalfLength - 500, 0), new Vec2i(400, 0));
            var pause = Lab.Run(m, config.GoalCelebrationTicks - 2);
            Assert.That(Lab.Count(pause, MatchEventKind.Goal), Is.EqualTo(0));
            Assert.That(m.Score(Team.Blue), Is.EqualTo(1));

            var after = Lab.Run(m, config.RestartDelayTicks + 3);
            var setPiece = after.Single(e => e.Kind == MatchEventKind.SetPiece);
            Assert.That(setPiece.Value, Is.EqualTo((int)RestartKind.Kickoff));
            Assert.That(setPiece.Team, Is.EqualTo(Team.Red), "失点した側のキックオフ");
            Assert.That(m.Ball.Position, Is.EqualTo(Vec2i.Zero));
        }

        [Test]
        public void OwnGoal_CountsForTheOpponent()
        {
            var m = Lab.Open();
            m.PlaceBall(new Vec2i(-Pitch.HalfLength + 2500, -200), new Vec2i(-300, 0));
            m.SetLastTouch(Team.Blue);
            Lab.Run(m, 30);
            Assert.That(m.Score(Team.Red), Is.EqualTo(1));
            Assert.That(m.Score(Team.Blue), Is.EqualTo(0));
        }

        [Test]
        public void LongRandomMatches_GoalEventsEqualTheScore()
        {
            for (int seed = 0; seed < 12; seed++)
            {
                var m = new Match(new SoccerConfig { MatchTicks = 3 * 60 * Pitch.TickRate }, seed);
                var events = Lab.Run(m, 3 * 60 * Pitch.TickRate + 10, Lab.Random(seed + 100));
                Assert.That(m.Phase, Is.EqualTo(MatchPhase.Ended));
                Assert.That(Lab.Count(events, MatchEventKind.Goal), Is.EqualTo(m.Score(Team.Blue) + m.Score(Team.Red)), $"seed {seed}");
                Assert.That(Lab.Count(events, MatchEventKind.Whistle), Is.EqualTo(1));
            }
        }
    }
}
