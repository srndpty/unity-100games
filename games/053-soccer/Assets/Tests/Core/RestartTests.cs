using System.Linq;
using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>ライン外と再開（スローイン・ゴールキック・コーナー・キックオフ）。</summary>
    public sealed class RestartTests
    {
        /// <summary>再開の準備ができるまで進める。</summary>
        private static void ToSetPiece(Match m)
        {
            for (int i = 0; i < 600 && m.Phase != MatchPhase.SetPiece; i++) { m.Step(Lab.Idle()); }
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.SetPiece));
        }

        [TestCase(Team.Blue, Team.Red)]
        [TestCase(Team.Red, Team.Blue)]
        public void Touchline_GivesAThrowInToTheOtherTeam(Team lastTouch, Team restart)
        {
            var m = Lab.Open();
            m.PlaceBall(new Vec2i(2000, Pitch.HalfWidth - 1000), new Vec2i(0, 200));
            m.SetLastTouch(lastTouch);
            var events = Lab.Run(m, 20);
            var outEvent = events.Single(e => e.Kind == MatchEventKind.Out);
            Assert.That(outEvent.Value, Is.EqualTo((int)RestartKind.ThrowIn));
            Assert.That(outEvent.Team, Is.EqualTo(restart));
            Assert.That(m.RestartSpot.Y, Is.EqualTo(Pitch.HalfWidth));
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.Stopped));
        }

        [Test]
        public void SetPiece_TakerHoldsTheBall_CannotMove_AndOpponentsKeepTheirDistance()
        {
            var m = Lab.Open();
            m.PlaceBall(new Vec2i(2000, Pitch.HalfWidth - 1000), new Vec2i(0, 200));
            m.SetLastTouch(Team.Red);
            Lab.Run(m, 20);

            // 相手（赤）を再開の位置へ走らせても、球から離される。
            var events = Lab.Run(m, 200, (x, i) => Match.TeamOf(i) == Team.Red ? new PlayerCommand(x.RestartSpot - x.Players[i].Position) : PlayerCommand.Idle);
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.SetPiece));
            Assert.That(m.Owner, Is.EqualTo(m.Taker));
            Assert.That(Match.TeamOf(m.Taker), Is.EqualTo(Team.Blue));
            foreach (var p in m.Players.Where(p => p.Team == Team.Red && !p.IsKeeper))
            {
                Assert.That(Vec2i.DistanceSquared(p.Position, m.RestartSpot), Is.GreaterThanOrEqualTo((long)(Pitch.ExclusionRadius - 2) * (Pitch.ExclusionRadius - 2)), "丸めの誤差だけ");
            }

            var at = m.Players[m.Taker].Position;
            Lab.Run(m, 5, (x, i) => i == x.Taker ? new PlayerCommand(new Vec2i(1000, 0)) : PlayerCommand.Idle);
            Assert.That(m.Players[m.Taker].Position, Is.EqualTo(at), "蹴る人は動けない");
            Assert.That(Lab.Count(events, MatchEventKind.Tackle), Is.EqualTo(0));
        }

        [Test]
        public void ThrowIn_CannotBeAShot_ButCanBeAPass()
        {
            var m = Lab.Open();
            m.PlaceBall(new Vec2i(2000, Pitch.HalfWidth - 1000), new Vec2i(0, 200));
            m.SetLastTouch(Team.Red);
            Lab.Run(m, 20);
            ToSetPiece(m);
            var taker = m.Taker;
            var hash = m.Hash();
            var shot = Lab.Run(m, 1, (x, i) => i == taker ? new PlayerCommand(Vec2i.Zero, PlayerAction.Shoot, new Vec2i(Pitch.HalfLength, 0)) : PlayerCommand.Idle);
            Assert.That(Lab.Count(shot, MatchEventKind.Kick), Is.EqualTo(0), "スローインでシュートはできない");
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.SetPiece));
            Assert.That(m.Hash(), Is.Not.EqualTo(hash), "時計は進む");

            var pass = Lab.Run(m, 1, (x, i) => i == taker ? new PlayerCommand(Vec2i.Zero, PlayerAction.Pass, new Vec2i(0, 0)) : PlayerCommand.Idle);
            Assert.That(pass.Single(e => e.Kind == MatchEventKind.Kick).Player, Is.EqualTo(taker));
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.Play));
        }

        [Test]
        public void OnlyTheTakerCanKick_DuringASetPiece()
        {
            var m = new Match(Lab.Exact, 3);
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.SetPiece), "最初はキックオフ");
            var taker = m.Taker;

            // 全員がシュートを命じても、蹴るのは蹴る人だけ。
            var events = Lab.Run(m, 1, (x, i) => new PlayerCommand(Vec2i.Zero, PlayerAction.Shoot, new Vec2i(Pitch.HalfLength, 0)));
            Assert.That(events.Single(e => e.Kind == MatchEventKind.Kick).Player, Is.EqualTo(taker));
        }

        [Test]
        public void SetPieceTimeout_PassesAutomatically()
        {
            var config = Lab.Exact;
            var m = new Match(config, 4);
            var events = Lab.Run(m, config.SetPieceTimeoutTicks + 2);
            var kick = events.Single(e => e.Kind == MatchEventKind.Kick);
            Assert.That(kick.Value, Is.EqualTo((int)PlayerAction.Pass));
            Assert.That(m.Phase, Is.EqualTo(MatchPhase.Play));
        }

        [Test]
        public void GoalKick_IsTakenByTheKeeper_Corner_ByAFieldPlayer()
        {
            var g = Lab.Open();
            g.PlaceBall(new Vec2i(Pitch.HalfLength - 1000, 8000), new Vec2i(300, 0));
            g.SetLastTouch(Team.Blue);
            Lab.Run(g, 20);
            ToSetPiece(g);
            Assert.That(g.Restart, Is.EqualTo(RestartKind.GoalKick));
            Assert.That(g.Taker, Is.EqualTo(3), "赤の GK");

            var c = Lab.Open();
            c.PlaceBall(new Vec2i(Pitch.HalfLength - 1000, -5000), new Vec2i(300, 0));
            c.SetLastTouch(Team.Red);
            Lab.Run(c, 20);
            ToSetPiece(c);
            Assert.That(c.Restart, Is.EqualTo(RestartKind.Corner));
            Assert.That(c.RestartSpot, Is.EqualTo(new Vec2i(Pitch.HalfLength, -Pitch.HalfWidth)));
            Assert.That(Match.TeamOf(c.Taker), Is.EqualTo(Team.Blue));
            Assert.That(c.Players[c.Taker].IsKeeper, Is.False);
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
