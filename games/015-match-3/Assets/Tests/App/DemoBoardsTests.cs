using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class DemoBoardsTests
    {
        private static Match3Config InstantConfig() => new()
        {
            SwapTicks = 0,
            ClearTicks = 0,
            FallTicksPerRow = 0,
            ReshuffleTicks = 0,
        };

        [Test]
        public void PlayableBoardsAreValidStarts()
        {
            foreach (var board in new[] { DemoBoards.TwoChain(), DemoBoards.Cross() })
            {
                Assert.That(MatchFinder.Find(board).IsEmpty, Is.True);
                Assert.That(SwapRules.HasLegalMove(board), Is.True);
                Assert.That(new Match3Game(new Match3Config(), 1, board).Phase, Is.EqualTo(GamePhase.Idle));
            }
        }

        [Test]
        public void TwoChainSwapChainsTwice()
        {
            var game = new Match3Game(InstantConfig(), 1, DemoBoards.TwoChain());

            Assert.That(game.TrySwap(DemoBoards.TwoChainSwap.From, DemoBoards.TwoChainSwap.To), Is.EqualTo(SwapVerdict.Legal));
            game.Tick();
            Assert.That(game.MaxChain, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void CrossSwapClearsFiveCellsOnce()
        {
            var config = new Match3Config();
            var game = new Match3Game(config, 1, DemoBoards.Cross());
            game.TrySwap(DemoBoards.CrossSwap.From, DemoBoards.CrossSwap.To);
            for (var i = 0; i < config.SwapTicks; i++) { game.Tick(); }

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Clearing));
            Assert.That(game.ClearingCells.Count, Is.EqualTo(5));
            Assert.That(game.Score, Is.EqualTo(5 * config.ScorePerPiece));
        }

        [Test]
        public void NoMatchSwapIsNoMatch()
        {
            Assert.That(SwapRules.Check(DemoBoards.TwoChain(), DemoBoards.NoMatchSwap.From, DemoBoards.NoMatchSwap.To), Is.EqualTo(SwapVerdict.NoMatch));
        }

        [Test]
        public void DeadBoardIsReshuffledAtStart()
        {
            Assert.That(SwapRules.HasLegalMove(DemoBoards.Dead()), Is.False);
            Assert.That(new Match3Game(new Match3Config(), 1, DemoBoards.Dead()).Phase, Is.EqualTo(GamePhase.Reshuffling));
        }
    }
}
