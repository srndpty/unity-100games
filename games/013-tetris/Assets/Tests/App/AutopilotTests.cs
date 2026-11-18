using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class AutopilotTests
    {
        private const int PieceCount = 300;

        [TestCase(1)]
        [TestCase(2024)]
        public void AutopilotKeepsClearingLines(int seed)
        {
            var game = new StrataGame(new StrataConfig(), seed);
            for (var piece = 0; piece < PieceCount && game.Phase != GamePhase.GameOver; piece++)
            {
                foreach (var command in Autopilot.PlanPiece(game)) { game.Apply(command); }
                while (game.Phase == GamePhase.Clearing) { game.Tick(); }
            }

            Assert.That(game.Phase, Is.EqualTo(GamePhase.Falling));
            Assert.That(game.Lines, Is.GreaterThanOrEqualTo(PieceCount / 3));
        }

        [Test]
        public void PlanEndsWithAHardDrop()
        {
            var plan = Autopilot.PlanPiece(new StrataGame(new StrataConfig(), 3));
            Assert.That(plan[plan.Count - 1], Is.EqualTo(GameCommand.HardDrop));
        }

        [Test]
        public void NoPlanWithoutAnActivePiece()
        {
            var board = Board.Empty;
            for (var x = 0; x < Board.Width; x++) { board = board.With(x, Board.VisibleHeight, PieceKind.J); }
            var game = new StrataGame(new StrataConfig(), new SevenBag(1), board);

            Assert.That(game.Active, Is.Null);
            Assert.That(Autopilot.PlanPiece(game), Is.Empty);
        }

        [Test]
        public void MissingGameIsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => Autopilot.PlanPiece(null!));
        }
    }
}
