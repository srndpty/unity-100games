using System;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有：得点後にサーブ権が更新される。得点・ゲーム・タイブレークの状態機械。</summary>
    public sealed class ScoreTests
    {
        private const Side A = Side.Near;
        private const Side B = Side.Far;

        private static void Award(MatchScore score, Side side, int times)
        {
            for (var i = 0; i < times; i++) { score.Award(side); }
        }

        [Test]
        public void DefaultIsASingleSevenPointTiebreak()
        {
            var score = new MatchScore(0, 7, A);
            Assert.That(score.InTiebreak, Is.True);
            Award(score, A, 6);
            Assert.That(score.IsOver, Is.False);
            Assert.That(score.Award(A), Is.EqualTo(ScoreChange.Game | ScoreChange.Set));
            Assert.That(score.Winner, Is.EqualTo(A));
            Assert.That(score.Games(A), Is.EqualTo(1));
            Assert.That(score.Points(A), Is.EqualTo(7), "最後のゲームの得点は残る");
        }

        [Test]
        public void TiebreakNeedsATwoPointLead()
        {
            var score = new MatchScore(0, 7, A);
            Award(score, A, 6);
            Award(score, B, 6);
            score.Award(A);
            Assert.That(score.IsOver, Is.False, "7-6 では決まらない");
            score.Award(B);
            score.Award(B);
            Assert.That(score.IsOver, Is.False, "7-8");
            score.Award(B);
            Assert.That(score.Winner, Is.EqualTo(B), "7-9");
        }

        [Test]
        public void TiebreakServeOrderIsABBAABB()
        {
            var score = new MatchScore(0, 15, A);
            var servers = new Side[9];
            for (var i = 0; i < servers.Length; i++)
            {
                servers[i] = score.Server;
                score.Award(i % 2 == 0 ? A : B);
            }

            Assert.That(servers, Is.EqualTo(new[] { A, B, B, A, A, B, B, A, A }));
        }

        [Test]
        public void ServeCourtAlternatesWithEveryPoint()
        {
            var score = new MatchScore(0, 7, B);
            var courts = Enumerable.Range(0, 5).Select(i =>
            {
                var court = score.ServeCourt;
                score.Award(A);
                return court;
            }).ToArray();
            Assert.That(courts, Is.EqualTo(new[] { ServeCourt.Deuce, ServeCourt.Ad, ServeCourt.Deuce, ServeCourt.Ad, ServeCourt.Deuce }));
        }

        [Test]
        public void RegularGameGoesThroughDeuceAndAdvantage()
        {
            var score = new MatchScore(6, 7, A);
            Assert.That(score.InTiebreak, Is.False);
            Award(score, A, 3);
            Award(score, B, 3);
            Assert.That(score.Award(A), Is.EqualTo(ScoreChange.None), "アドバンテージ");
            Assert.That(score.Award(B), Is.EqualTo(ScoreChange.None), "デュースに戻る");
            score.Award(B);
            Assert.That(score.Award(B), Is.EqualTo(ScoreChange.Game));
            Assert.That(score.Games(B), Is.EqualTo(1));
            Assert.That(score.Points(A), Is.Zero);
            Assert.That(score.PointsPlayed, Is.Zero);
        }

        [Test]
        public void LoveGameNeedsFourPoints()
        {
            var score = new MatchScore(6, 7, A);
            Award(score, A, 3);
            Assert.That(score.Games(A), Is.Zero);
            Assert.That(score.Award(A), Is.EqualTo(ScoreChange.Game));
        }

        [Test]
        public void ServerAlternatesEveryRegularGame()
        {
            var score = new MatchScore(6, 7, A);
            var servers = new Side[4];
            for (var game = 0; game < servers.Length; game++)
            {
                servers[game] = score.Server;
                Assert.That(score.Server, Is.EqualTo(servers[game]));
                for (var p = 0; p < 4; p++)
                {
                    Assert.That(score.Server, Is.EqualTo(servers[game]), "ゲームの途中では変わらない");
                    score.Award(A);
                }
            }

            Assert.That(servers, Is.EqualTo(new[] { A, B, A, B }));
        }

        [TestCase(6, 6, 4, false)]
        [TestCase(6, 7, 5, false)]
        [TestCase(4, 4, 2, false)]
        [TestCase(4, 5, 3, false)]
        public void SetIsWonByTwoGamesAtOrAboveTheTarget(int target, int winnerGames, int loserGames, bool tiebreak)
        {
            var score = PlayGames(target, winnerGames, loserGames);
            Assert.That(score.Winner, Is.EqualTo(A));
            Assert.That(score.Games(A), Is.EqualTo(winnerGames));
            Assert.That(score.Games(B), Is.EqualTo(loserGames));
            Assert.That(score.InTiebreak, Is.EqualTo(tiebreak));
        }

        [Test]
        public void SixAllGoesToATiebreakThatDecidesTheSet()
        {
            var score = new MatchScore(6, 7, A);
            for (var game = 0; game < 12; game++) { Award(score, game % 2 == 0 ? A : B, 4); }
            Assert.That(score.InTiebreak, Is.True);
            Assert.That(score.IsOver, Is.False);
            var first = score.Server;
            Assert.That(first, Is.EqualTo(A), "12 ゲームの後は最初のサーバーの番");
            Award(score, B, 7);
            Assert.That(score.Winner, Is.EqualTo(B));
            Assert.That(score.Games(B), Is.EqualTo(7));
            Assert.That(score.Games(A), Is.EqualTo(6));
        }

        [Test]
        public void SixFiveIsNotYetASet()
        {
            var score = new MatchScore(6, 7, A);
            for (var game = 0; game < 10; game++) { Award(score, game % 2 == 0 ? A : B, 4); }
            Award(score, A, 4);
            Assert.That(score.Games(A), Is.EqualTo(6));
            Assert.That(score.IsOver, Is.False);
            Assert.That(score.InTiebreak, Is.False);
        }

        [Test]
        public void AwardAfterTheMatchIsRejected()
        {
            var score = new MatchScore(0, 1, A);
            score.Award(A);
            Assert.That(score.IsOver, Is.False, "1 点先取でも 2 点差が要る");
            score.Award(A);
            Assert.That(score.IsOver, Is.True);
            Assert.Throws<InvalidOperationException>(() => score.Award(B));
        }

        [TestCase(-1, 7)]
        [TestCase(MatchScore.MaxTiebreakAtGames + 1, 7)]
        [TestCase(0, 0)]
        [TestCase(0, MatchScore.MaxTiebreakPoints + 1)]
        public void OutOfRangeSettingsAreRejected(int at, int points)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new MatchScore(at, points, A));
        }

        [Test]
        public void UndefinedSideIsRejected()
        {
            var score = new MatchScore(0, 7, A);
            Assert.Throws<ArgumentOutOfRangeException>(() => score.Award((Side)7));
        }

        // ---- 試合の中で：ポイントの後にサーバー・コート・立ち位置が更新される ----

        [Test]
        public void MatchMovesTheServeAfterEachPoint()
        {
            var config = TestKit.Config();
            var match = new TennisMatch(config, seed: 3);
            var expected = new[] { (A, ServeCourt.Deuce), (B, ServeCourt.Ad), (B, ServeCourt.Deuce), (A, ServeCourt.Ad) };
            foreach (var (server, court) in expected)
            {
                Assert.That(match.Rally.Server, Is.EqualTo(server));
                Assert.That(match.Rally.ServeCourt, Is.EqualTo(court));
                Assert.That(match.AthleteOf(server).Position, Is.EqualTo(Positions.Serve(server, court, config)));
                Assert.That(match.AthleteOf(Court.Other(server)).Position, Is.EqualTo(Positions.Receive(Court.Other(server), court, config)));
                Assert.That(match.BallInHand, Is.True);

                // サーバーがトスを上げずに待っていても点は動かないので、場面を作ってポイントを終わらせる（サーバーのネット）。
                var from = match.AthleteOf(server).Position.WithZ(3000);
                var target = Court.Forward(server) * 80000;
                match.StageRally(server, from, TestKit.VelocityTo(from, 0, target, config, clearance: -6000), match.Near.Position, match.Far.Position);
                TestKit.IdleUntil(match, m => m.Phase == MatchPhase.Gap);
                TestKit.IdleUntil(match, m => m.Phase == MatchPhase.Point);
            }
        }

        private static MatchScore PlayGames(int target, int winnerGames, int loserGames)
        {
            var score = new MatchScore(target, 7, A);

            // 負ける側が先に取り、最後に勝つ側が続けて取る（途中で決着しない順）。
            var lead = Math.Min(winnerGames, loserGames);
            for (var i = 0; i < lead; i++)
            {
                Award(score, A, 4);
                Award(score, B, 4);
            }

            for (var i = lead; i < loserGames; i++) { Award(score, B, 4); }
            for (var i = lead; i < winnerGames; i++) { Award(score, A, 4); }
            return score;
        }
    }
}
