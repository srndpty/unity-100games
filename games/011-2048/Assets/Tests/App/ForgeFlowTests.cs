using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class ForgeFlowTests
    {
        private static readonly BoardState _winningBoard = BoardState.Empty.With(0, 1024).With(1, 1024);

        private static readonly BoardState _losingBoard = BoardState.FromValues(
            2, 2, 8, 16,
            8, 16, 32, 64,
            4, 8, 16, 32,
            8, 16, 32, 64);

        private static ForgeFlow Create(int fourChancePercent = 0)
        {
            var seed = 100;
            return new ForgeFlow(new ForgeRules { FourChancePercent = fourChancePercent }, () => seed++);
        }

        [Test]
        public void Starts_OnTitleWithoutGame()
        {
            var flow = Create();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
        }

        [Test]
        public void Constructor_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new ForgeFlow(null!, () => 1));
            Assert.Throws<ArgumentNullException>(() => new ForgeFlow(new ForgeRules(), null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ForgeFlow(new ForgeRules { FourChancePercent = 101 }, () => 1));
        }

        [Test]
        public void Move_OnTitle_IsRejected()
        {
            Assert.That(Create().Move(Direction.Left), Is.EqualTo(MoveResult.Rejected));
        }

        [Test]
        public void Submit_StartsGameAndIgnoresRepeatWhilePlaying()
        {
            var flow = Create();
            flow.Submit();
            var game = flow.Game;
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game, Is.SameAs(game));
        }

        [Test]
        public void Win_MovesToResultAndRaisesEvents()
        {
            var flow = Create();
            flow.StartFrom(_winningBoard);
            Assert.That(flow.Move(Direction.Left), Is.EqualTo(MoveResult.Won));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Moved | FlowEvents.Merged | FlowEvents.Won));
        }

        [Test]
        public void Lose_MovesToResultAndRaisesEvents()
        {
            var flow = Create();
            flow.StartFrom(_losingBoard);
            Assert.That(flow.Move(Direction.Left), Is.EqualTo(MoveResult.Lost));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Moved | FlowEvents.Merged | FlowEvents.Lost));
        }

        [Test]
        public void Result_RejectsMovesAndKeepsBoard()
        {
            var flow = Create();
            flow.StartFrom(_winningBoard);
            flow.Move(Direction.Left);
            flow.TakeEvents();
            var board = flow.Game!.Board;
            Assert.That(flow.Move(Direction.Right), Is.EqualTo(MoveResult.Rejected));
            Assert.That(flow.Game!.Board, Is.EqualTo(board));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None));
        }

        [Test]
        public void Rematch_UsesNewSeedAndNewGame()
        {
            var flow = Create();
            flow.StartFrom(_winningBoard);
            var first = flow.Game!;
            flow.Move(Direction.Left);
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game, Is.Not.SameAs(first));
            Assert.That(flow.Game!.Seed, Is.Not.EqualTo(first.Seed));
            Assert.That(flow.Game!.Score, Is.Zero);
        }

        [Test]
        public void RejectedMove_RaisesNoEvents()
        {
            var flow = Create();
            flow.StartFrom(BoardState.Empty.With(0, 2));
            Assert.That(flow.Move(Direction.Left), Is.EqualTo(MoveResult.Rejected));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None));
        }

        [Test]
        public void MoveWithoutMerge_RaisesOnlyMoved()
        {
            var flow = Create();
            flow.StartFrom(BoardState.Empty.With(0, 2));
            flow.Move(Direction.Right);
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Moved));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None), "取り出すと空になる");
        }

        [Test]
        public void FocusLost_RejectsAllOperationsUntilRestored()
        {
            var flow = Create();
            flow.StartFrom(BoardState.Empty.With(0, 2));
            var board = flow.Game!.Board;

            flow.SetFocus(false);
            Assert.That(flow.Move(Direction.Right), Is.EqualTo(MoveResult.Rejected));
            flow.Back();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game!.Board, Is.EqualTo(board));

            flow.SetFocus(true);
            Assert.That(flow.Move(Direction.Right), Is.EqualTo(MoveResult.Moved));
        }

        [Test]
        public void FocusLost_OnTitle_DoesNotStart()
        {
            var flow = Create();
            flow.SetFocus(false);
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [Test]
        public void Back_ClearsGameAndPendingEvents()
        {
            var flow = Create();
            flow.StartFrom(BoardState.Empty.With(0, 2));
            flow.Move(Direction.Right);
            flow.Back();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None));
        }
    }
}
