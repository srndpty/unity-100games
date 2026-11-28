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
            var events = Lab.Run(m, 60);
            Assert.That(Lab.Count(events, MatchEventKind.Goal), Is.EqualTo(1));
            Assert.That(m.Score(Team.Blue), Is.EqualTo(1));
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.Goal));
            Assert.That(m.Ball.Position.X, Is.LessThanOrEqualTo(Pitch.HalfLength + Pitch.GoalDepth), "ネットの奥で止まる");
            Assert.That(Lab.Count(events, MatchEventKind.Wall), Is.EqualTo(0), "ゴールの口では跳ね返らない");
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
        public void OverTheBar_OrOutsideThePosts_IsNotAGoal_TheWallBouncesItBack()
        {
            var high = Lab.Open();
            high.PlaceBall(new Vec2i(Pitch.HalfLength - 1000, 0), new Vec2i(300, 0), Pitch.CrossbarHeight + 300, 0);
            var e1 = Lab.Run(high, 20);
            Assert.That(Lab.Count(e1, MatchEventKind.Goal), Is.EqualTo(0));
            Assert.That(Lab.Count(e1, MatchEventKind.Wall), Is.EqualTo(1), "クロスバーの上は壁");
            Assert.That(high.Ball.Velocity.X, Is.LessThan(0), "跳ね返る");
            Assert.That(high.Phase, Is.EqualTo(MatchPhase.Play), "試合は止まらない");

            var wide = Lab.Open();
            wide.PlaceBall(new Vec2i(Pitch.HalfLength - 1000, Pitch.GoalHalfWidth + 400), new Vec2i(300, 0));
            var e2 = Lab.Run(wide, 20);
            Assert.That(Lab.Count(e2, MatchEventKind.Goal), Is.EqualTo(0));
            Assert.That(Lab.Count(e2, MatchEventKind.Wall), Is.EqualTo(1));
            Assert.That(wide.Ball.Velocity.X, Is.LessThan(0));
            Assert.That(wide.Ball.Position.X + Lab.Exact.BallRadius, Is.LessThanOrEqualTo(Pitch.HalfLength), "コートの中に残る");
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
            var kickoff = after.Single(e => e.Kind == MatchEventKind.Kickoff);
            Assert.That(kickoff.Team, Is.EqualTo(Team.Red), "失点した側のキックオフ");
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.Kickoff));
            Assert.That(m.Ball.Position, Is.EqualTo(Vec2i.Zero));
        }

        [Test]
        public void OwnGoal_CountsForTheOpponent()
        {
            var m = Lab.Open();
            m.PlaceBall(new Vec2i(-Pitch.HalfLength + 2500, -200), new Vec2i(-300, 0));
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
                var random = Lab.Random(seed + 100);
                var events = Lab.Run(m, 3 * 60 * Pitch.TickRate + 10, (x, i) =>
                {
                    if (i == 0 && x.Events.Any(e => e.Kind == MatchEventKind.Goal))
                    {
                        Assert.That(System.Math.Abs(x.Ball.Position.Y), Is.LessThan(Pitch.GoalHalfWidth), $"seed {seed}：得点の球はポストの間");
                    }

                    return random(x, i);
                });
                Assert.That(m.Phase, Is.EqualTo(MatchPhase.Ended));
                Assert.That(Lab.Count(events, MatchEventKind.Goal), Is.EqualTo(m.Score(Team.Blue) + m.Score(Team.Red)), $"seed {seed}");
                Assert.That(Lab.Count(events, MatchEventKind.Whistle), Is.EqualTo(1));
            }
        }
    }
}
