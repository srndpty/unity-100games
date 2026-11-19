using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class DemoGamesTests
    {
        private static OthelloConfig InstantConfig() => new()
        {
            FlipTicks = 0,
            PassTicks = 0,
            CpuThinkTicks = 0,
        };

        [Test]
        public void PassMoveMakesWhitePassAndReturnsTheTurnToBlack()
        {
            var game = new OthelloGame(new OthelloConfig { FlipTicks = 0 }, DemoGames.PassSoon());
            Assert.That(game.TryPlay(Side.Black, DemoGames.PassMove), Is.EqualTo(PlayVerdict.Ok));
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Passing));
            Assert.That(game.ToMove, Is.EqualTo(Side.White));
            Assert.That(game.TakeEvents(), Is.EqualTo(GameEvents.Placed | GameEvents.Passed));
        }

        [Test]
        public void EndgameIsWonByBlackWhateverTheOrder()
        {
            // 3マスのすべての打ち方を調べる。
            static void Walk(Position position)
            {
                if (position.IsTerminal)
                {
                    Assert.That(position.Count(Side.Black), Is.GreaterThan(position.Count(Side.White)));
                    return;
                }

                if (position.LegalMoves() == 0)
                {
                    Walk(position.Pass());
                    return;
                }

                foreach (var move in Bits.ToSquares(position.LegalMoves())) { Walk(position.Play(move)); }
            }

            Assert.That(DemoGames.Endgame().EmptyCount, Is.EqualTo(3));
            Walk(DemoGames.Endgame());
        }

        [TestCase(Side.Black, Outcome.Win)]
        [TestCase(Side.White, Outcome.Loss)]
        public void EndgameGivesTheExpectedResultForEachSide(Side playerSide, Outcome expected)
        {
            var flow = new OthelloFlow(InstantConfig(), 1);
            flow.SelectSide(playerSide);
            flow.GameFactory = _ => new OthelloGame(flow.Config, DemoGames.Endgame());
            flow.Submit();
            for (var i = 0; i < 1000 && flow.Screen == AppScreen.Playing; i++)
            {
                if (flow.IsPlayerTurn)
                {
                    var move = Searcher.Search(flow.Game!.Position, 1).Move;
                    flow.PointerDown(move.X + 0.5, move.Y + 0.5);
                }
                else
                {
                    flow.StepTick();
                }
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Outcome, Is.EqualTo(expected));
        }
    }
}
