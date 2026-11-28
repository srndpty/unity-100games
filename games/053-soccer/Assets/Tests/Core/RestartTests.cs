using System.Linq;
using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>再開はキックオフだけ。ライン外はなく、壁で跳ね返る。</summary>
    public sealed class RestartTests
    {
        [TestCase(1)]
        [TestCase(-1)]
        public void SideWall_ReflectsTheBall_AndPlayGoesOn(int side)
        {
            var m = Lab.Open();
            m.PlaceBall(new Vec2i(2000, side * (Pitch.HalfWidth - 1000)), new Vec2i(0, side * 200));
            var events = Lab.Run(m, 20);
            Assert.That(Lab.Count(events, MatchEventKind.Wall), Is.EqualTo(1));
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.Play));
            Assert.That(m.Ball.Velocity.Y * side, Is.LessThan(0), "向きが反転する");
            Assert.That(System.Math.Abs(m.Ball.Position.Y) + Lab.Exact.BallRadius, Is.LessThanOrEqualTo(Pitch.HalfWidth));
        }

        [Test]
        public void Wall_LosesSpeedByTheRestitution()
        {
            var config = new SoccerConfig { ShotSpread = 0, PassSpread = 0, WallRestitutionPercent = 50, RollFrictionPerMille = 1 };
            var m = Lab.Open(config);
            var r = config.BallRadius;
            m.PlaceBall(new Vec2i(0, Pitch.HalfWidth - r - 99), new Vec2i(0, 300));

            // 転がりの摩擦で 299 になり、壁を 200 越える。
            Lab.Run(m, 1);
            Assert.That(m.Ball.Velocity.Y, Is.EqualTo(-299 * 50 / 100));
            Assert.That(m.Ball.Position.Y, Is.EqualTo(Pitch.HalfWidth - r - 200), "越えた分だけ折り返す");
        }

        [Test]
        public void LongRandomMatches_TheBallNeverLeavesThePitch_ExceptIntoTheGoal()
        {
            for (int seed = 0; seed < 6; seed++)
            {
                var m = new Match(new SoccerConfig { MatchTicks = 60 * Pitch.TickRate }, seed);
                var cmd = Lab.Random(seed + 7);
                var c = Lab.Idle();
                while (m.Phase != MatchPhase.Ended)
                {
                    for (int i = 0; i < Match.PlayerCount; i++) { c[i] = cmd(m, i); }
                    m.Step(c);
                    var b = m.Ball.Position;
                    Assert.That(System.Math.Abs(b.Y), Is.LessThanOrEqualTo(Pitch.HalfWidth), $"seed {seed} tick {m.Clock}");
                    if (m.Phase != MatchPhase.Goal)
                    {
                        Assert.That(System.Math.Abs(b.X), Is.LessThanOrEqualTo(Pitch.HalfLength + Lab.Exact.BallRadius), $"seed {seed} tick {m.Clock}");
                    }
                }
            }
        }

        [Test]
        public void Kickoff_OpponentsKeepTheirDistance_AndTheTakerCannotMove()
        {
            var m = new Match(Lab.Exact, 2);
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.Kickoff));
            Assert.That(Match.TeamOf(m.Taker), Is.EqualTo(Team.Blue));

            // 相手（赤）をセンターへ走らせても、円の外に留まる。
            var events = Lab.Run(m, 200, (x, i) => Match.TeamOf(i) == Team.Red ? new PlayerCommand(Vec2i.Zero - x.Players[i].Position) : PlayerCommand.Idle);
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.Kickoff));
            Assert.That(m.Owner, Is.EqualTo(m.Taker));
            foreach (var p in m.Players.Where(p => p.Team == Team.Red))
            {
                Assert.That(Vec2i.DistanceSquared(p.Position, Vec2i.Zero), Is.GreaterThanOrEqualTo((long)(Pitch.ExclusionRadius - 2) * (Pitch.ExclusionRadius - 2)), "丸めの誤差だけ");
            }

            Assert.That(Lab.Count(events, MatchEventKind.Tackle), Is.EqualTo(0));
            var at = m.Players[m.Taker].Position;
            Lab.Run(m, 5, (x, i) => i == x.Taker ? new PlayerCommand(new Vec2i(1000, 0)) : PlayerCommand.Idle);
            Assert.That(m.Players[m.Taker].Position, Is.EqualTo(at), "蹴る人は動けない");
        }

        [Test]
        public void OnlyTheTakerCanKick_DuringTheKickoff()
        {
            var m = new Match(Lab.Exact, 3);
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.Kickoff), "最初はキックオフ");
            var taker = m.Taker;

            // 全員がシュートを命じても、蹴るのは蹴る人だけ。
            var events = Lab.Run(m, 1, (x, i) => new PlayerCommand(Vec2i.Zero, PlayerAction.Shoot, new Vec2i(Pitch.HalfLength, 0)));
            Assert.That(events.Single(e => e.Kind == MatchEventKind.Kick).Player, Is.EqualTo(taker));
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.Play));
        }

        [Test]
        public void KickoffTimeout_PassesAutomatically()
        {
            var config = Lab.Exact;
            var m = new Match(config, 4);
            var events = Lab.Run(m, config.KickoffTimeoutTicks + 2);
            var kick = events.Single(e => e.Kind == MatchEventKind.Kick);
            Assert.That(kick.Value, Is.EqualTo((int)PlayerAction.Pass));
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.Play));
        }

        [Test]
        public void Kickoff_PlacesEveryoneInTheirOwnHalf()
        {
            var m = new Match(Lab.Exact, 5);
            foreach (var p in m.Players)
            {
                if (p.Index == m.Taker) { continue; }
                Assert.That(p.Position.X * Pitch.Direction(p.Team), Is.LessThan(0), $"p{p.Index}");
            }

            Assert.That(m.Owner, Is.EqualTo(m.Taker));
            Assert.That(m.Ball.Position, Is.EqualTo(Vec2i.Zero));
        }
    }
}
