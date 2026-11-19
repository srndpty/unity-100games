using NUnit.Framework;
using static Game.Core.Tests.OthelloTestKit;

namespace Game.Core.Tests
{
    public sealed class OthelloGameTests
    {
        /// <summary>黒の手番だが黒は打てず、白は c1 に打てる局面。白が c1 に打つと黒の石がなくなり終局する。</summary>
        private static Position BlackMustPass() => Parse(Side.Black,
            "WB......",
            "........",
            "........",
            "........",
            "........",
            "........",
            "........",
            "........");

        /// <summary>h8 だけが空き、黒が h8 に打つと盤が埋まる局面。</summary>
        private static Position OneEmptySquare() => Parse(Side.Black,
            "BBBBBBBB",
            "BBBBBBBB",
            "BBBBBBBB",
            "BBBBBBBB",
            "BBBBBBBB",
            "BBBBBBBB",
            "BBBBBBBB",
            "BBBBBBW.");

        [Test]
        public void StartsAwaitingBlack()
        {
            var game = new OthelloGame(new OthelloConfig());
            Assert.That(game.Phase, Is.EqualTo(GamePhase.AwaitingMove));
            Assert.That(game.ToMove, Is.EqualTo(Side.Black));
            Assert.That(game.LegalMoves, Is.EqualTo(Bits("d3", "c4", "f5", "e6")));
            Assert.That(game.Turn, Is.EqualTo(1));
            Assert.That(game.LastMove, Is.Null);
        }

        // ---- 挟めない手を拒否 ----

        [TestCase("a1", PlayVerdict.NoFlip)]
        [TestCase("c3", PlayVerdict.NoFlip)]
        [TestCase("d4", PlayVerdict.Occupied)]
        [TestCase("e4", PlayVerdict.Occupied)]
        public void IllegalSquaresAreRejectedWithoutChangingTheGame(string square, PlayVerdict expected)
        {
            var game = new OthelloGame(new OthelloConfig());
            Assert.That(game.TryPlay(Side.Black, S(square)), Is.EqualTo(expected));
            AssertUntouched(game);
        }

        [TestCase(-1, 0)]
        [TestCase(8, 3)]
        [TestCase(3, 8)]
        [TestCase(0, -1)]
        public void OutOfBoardIsRejected(int x, int y)
        {
            var game = new OthelloGame(new OthelloConfig());
            Assert.That(game.TryPlay(Side.Black, new Square(x, y)), Is.EqualTo(PlayVerdict.OutOfBoard));
            AssertUntouched(game);
        }

        [Test]
        public void MoveByTheSideNotToMoveIsRejected()
        {
            var game = new OthelloGame(new OthelloConfig());
            Assert.That(game.TryPlay(Side.White, S("c5")), Is.EqualTo(PlayVerdict.NotYourTurn));
            AssertUntouched(game);
        }

        [Test]
        public void CheckDoesNotChangeTheGame()
        {
            var game = new OthelloGame(new OthelloConfig());
            Assert.That(game.Check(Side.Black, S("d3")), Is.EqualTo(PlayVerdict.Ok));
            AssertUntouched(game);
        }

        [Test]
        public void LegalMoveFlipsAndHandsTheTurnOver()
        {
            var game = new OthelloGame(InstantConfig());
            Assert.That(game.TryPlay(Side.Black, S("d3")), Is.EqualTo(PlayVerdict.Ok));
            Assert.That(game.ToMove, Is.EqualTo(Side.White));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.AwaitingMove));
            Assert.That(game.LastMove, Is.EqualTo(S("d3")));
            Assert.That(game.LastFlips, Is.EqualTo(Bits("d4")));
            Assert.That(game.Count(Side.Black), Is.EqualTo(4));
            Assert.That(game.MovesPlayed, Is.EqualTo(1));
            Assert.That(game.Turn, Is.EqualTo(2));
            Assert.That(game.TakeEvents(), Is.EqualTo(GameEvents.Placed));
        }

        [Test]
        public void MovesAreRejectedAsBusyDuringTheFlipAnimation()
        {
            var config = new OthelloConfig { FlipTicks = 5 };
            var game = new OthelloGame(config);
            game.TryPlay(Side.Black, S("d3"));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Placing));
            Assert.That(game.LegalMoves, Is.EqualTo(0));
            for (var i = 0; i < 4; i++)
            {
                Assert.That(game.TryPlay(Side.White, S("c3")), Is.EqualTo(PlayVerdict.Busy), $"tick {i}");
                game.Tick();
            }

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Placing));
            game.Tick();
            Assert.That(game.Phase, Is.EqualTo(GamePhase.AwaitingMove));
            Assert.That(game.TryPlay(Side.White, S("c3")), Is.EqualTo(PlayVerdict.Ok));
        }

        [Test]
        public void TheSameMoveSentTwiceIsAppliedOnce()
        {
            var game = new OthelloGame(InstantConfig());
            Assert.That(game.TryPlay(Side.Black, S("d3")), Is.EqualTo(PlayVerdict.Ok));
            Assert.That(game.TryPlay(Side.Black, S("d3")), Is.EqualTo(PlayVerdict.NotYourTurn));
            Assert.That(game.MovesPlayed, Is.EqualTo(1));
        }

        // ---- 合法手ゼロでパス ----

        [Test]
        public void SideWithoutLegalMovesPassesAutomatically()
        {
            var game = new OthelloGame(InstantConfig(), BlackMustPass());
            Assert.That(game.Phase, Is.EqualTo(GamePhase.AwaitingMove));
            Assert.That(game.ToMove, Is.EqualTo(Side.White));
            Assert.That(game.Passes, Is.EqualTo(1));
            Assert.That(game.TakeEvents(), Is.EqualTo(GameEvents.Passed));
            Assert.That(game.Position.Black, Is.EqualTo(BlackMustPass().Black), "パスでは石が変わらない");
        }

        [Test]
        public void PassIsShownForPassTicksAndRejectsMoves()
        {
            var game = new OthelloGame(new OthelloConfig { PassTicks = 3 }, BlackMustPass());
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Passing));
            Assert.That(game.ToMove, Is.EqualTo(Side.Black), "パスの演出中はパスする側の手番のまま");
            Assert.That(game.TryPlay(Side.White, S("c1")), Is.EqualTo(PlayVerdict.Busy));
            game.Tick();
            game.Tick();
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Passing));
            game.Tick();
            Assert.That(game.Phase, Is.EqualTo(GamePhase.AwaitingMove));
            Assert.That(game.ToMove, Is.EqualTo(Side.White));
            Assert.That(game.TryPlay(Side.White, S("c1")), Is.EqualTo(PlayVerdict.Ok));
        }

        [Test]
        public void PassAfterAMoveHandsTheTurnBackToTheMover()
        {
            // 白が c1 に打つと黒は打てないが、白はまだ打てる（g1 の黒を f1 で挟める）。
            var position = Parse(Side.White,
                "WB....BW",
                "........",
                "........",
                "........",
                "........",
                "........",
                "........",
                "........");
            var game = new OthelloGame(InstantConfig(), position);
            Assert.That(game.TryPlay(Side.White, S("c1")), Is.EqualTo(PlayVerdict.Ok));
            Assert.That(game.TakeEvents(), Is.EqualTo(GameEvents.Placed | GameEvents.Passed));
            Assert.That(game.ToMove, Is.EqualTo(Side.White), "黒がパスして白の手番に戻る");
            Assert.That(game.Passes, Is.EqualTo(1));
        }

        // ---- 両者打てないと終局 ----

        [Test]
        public void GameEndsWhenNeitherSideCanMoveWithEmptySquaresLeft()
        {
            var game = new OthelloGame(InstantConfig(), BlackMustPass());
            game.TakeEvents();
            Assert.That(game.TryPlay(Side.White, S("c1")), Is.EqualTo(PlayVerdict.Ok));
            Assert.That(game.TakeEvents(), Is.EqualTo(GameEvents.Placed | GameEvents.GameOver));
            Assert.That(game.IsOver, Is.True);
            Assert.That(game.Position.EmptyCount, Is.EqualTo(61));
            Assert.That(game.Winner, Is.EqualTo(Side.White));
            Assert.That(game.Count(Side.White), Is.EqualTo(3));
            Assert.That(game.Count(Side.Black), Is.EqualTo(0));
        }

        [Test]
        public void GameEndsWhenTheBoardIsFull()
        {
            var game = new OthelloGame(InstantConfig(), OneEmptySquare());
            Assert.That(game.TryPlay(Side.Black, S("h8")), Is.EqualTo(PlayVerdict.Ok));
            Assert.That(game.IsOver, Is.True);
            Assert.That(game.Position.EmptyCount, Is.EqualTo(0));
            Assert.That(game.Winner, Is.EqualTo(Side.Black));
            Assert.That(game.Count(Side.Black), Is.EqualTo(64));
        }

        [Test]
        public void GameOverIsDecidedAfterTheFlipAnimation()
        {
            var game = new OthelloGame(new OthelloConfig { FlipTicks = 2 }, OneEmptySquare());
            game.TryPlay(Side.Black, S("h8"));
            Assert.That(game.IsOver, Is.False);
            Assert.That(game.Winner, Is.Null);
            game.Tick();
            game.Tick();
            Assert.That(game.IsOver, Is.True);
            Assert.That(game.TakeEvents(), Is.EqualTo(GameEvents.Placed | GameEvents.GameOver));
        }

        [Test]
        public void EqualCountsAreADraw()
        {
            var game = new OthelloGame(InstantConfig(), Parse(Side.Black,
                "BBBBBBBB",
                "BBBBBBBB",
                "BBBBBBBB",
                "BBBBBBBB",
                "WWWWWWWW",
                "WWWWWWWW",
                "WWWWWWWW",
                "WWWWWWWW"));
            Assert.That(game.IsOver, Is.True);
            Assert.That(game.IsDraw, Is.True);
            Assert.That(game.Winner, Is.Null);
        }

        [Test]
        public void EmptySquaresAreNotAwardedToTheWinner()
        {
            var game = new OthelloGame(InstantConfig(), BlackMustPass());
            game.TryPlay(Side.White, S("c1"));
            Assert.That(game.Count(Side.White) + game.Count(Side.Black), Is.EqualTo(3));
        }

        [Test]
        public void MovesAfterTheEndAreRejectedAndTicksStop()
        {
            var game = new OthelloGame(InstantConfig(), OneEmptySquare());
            game.TryPlay(Side.Black, S("h8"));
            game.TakeEvents();
            var ticks = game.TickCount;
            Assert.That(game.TryPlay(Side.Black, S("a1")), Is.EqualTo(PlayVerdict.GameOver));
            Assert.That(game.TryPlay(Side.White, S("a1")), Is.EqualTo(PlayVerdict.GameOver));
            game.Tick();
            Assert.That(game.TickCount, Is.EqualTo(ticks));
            Assert.That(game.TakeEvents(), Is.EqualTo(GameEvents.None), "終局の通知は1回だけ");
        }

        [Test]
        public void SearchGameFinishesWithBothSidesBlocked()
        {
            var game = PlaySearchGame(InstantConfig(), 2, 1, seed: 3);
            Assert.That(game.IsOver, Is.True);
            Assert.That(game.Position.IsTerminal, Is.True);
            Assert.That(game.Count(Side.Black) + game.Count(Side.White) + game.Position.EmptyCount, Is.EqualTo(64));
        }

        [Test]
        public void TimedAndInstantGamesReachTheSamePosition()
        {
            var instant = PlaySearchGame(InstantConfig(), 2, 2, seed: 11);
            var timed = PlaySearchGame(new OthelloConfig(), 2, 2, seed: 11);
            Assert.That(timed.Position, Is.EqualTo(instant.Position));
            Assert.That(timed.TickCount, Is.GreaterThan(0));
        }

        [Test]
        public void SearchGameReproducesAFixedFinalPosition()
        {
            // 整数だけで動くので、Mono（Unity）と .NET（dotnet test）で同じ最終局面になる。
            var game = PlaySearchGame(InstantConfig(), 3, 3, seed: 1);
            Assert.That(BoardText.Format(game.Position), Is.EqualTo(_fixedFinalPosition));
            Assert.That(game.Winner, Is.EqualTo(Side.White));
        }

        private static readonly string[] _fixedFinalPosition =
        {
            "WWWWWWWW",
            "WBBWWWWW",
            "WWBBBBWW",
            "WWWBBWWW",
            "WWWWWBWW",
            "WWBWWWWW",
            "WWWWWWWW",
            "BBBBBBWW",
        };

        [Test]
        public void ConfigIsCopiedWhenTheGameStarts()
        {
            var config = new OthelloConfig { FlipTicks = 4 };
            var game = new OthelloGame(config);
            config.FlipTicks = 0;
            game.TryPlay(Side.Black, S("d3"));
            Assert.That(game.PhaseDuration, Is.EqualTo(4));
        }

        private static void AssertUntouched(OthelloGame game)
        {
            Assert.That(game.Position, Is.EqualTo(Position.Initial));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.AwaitingMove));
            Assert.That(game.MovesPlayed, Is.EqualTo(0));
            Assert.That(game.LastMove, Is.Null);
            Assert.That(game.TakeEvents(), Is.EqualTo(GameEvents.None));
        }
    }
}
