using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class MinesweeperFlowTests
    {
        private static MinesweeperFlow Create(BoardConfig? config = null)
        {
            var seed = 100;
            return new MinesweeperFlow(config ?? new BoardConfig(), () => seed++);
        }

        private static MinesweeperFlow Playing(BoardConfig? config = null)
        {
            var flow = Create(config);
            flow.Submit();
            return flow;
        }

        private static void PlayToWin(MinesweeperFlow flow)
        {
            for (var i = 0; i < 1024 && flow.Screen == AppScreen.Playing; i++)
            {
                Assert.That(AnswerKeyPilot.TryNextSafe(flow.Game!, out var x, out var y), Is.True);
                Assert.That(flow.Reveal(x, y), Is.Not.EqualTo(MoveResult.Rejected));
            }
        }

        private static void PlayToLose(MinesweeperFlow flow)
        {
            AnswerKeyPilot.TryNextSafe(flow.Game!, out var x, out var y);
            flow.Reveal(x, y);
            Assert.That(AnswerKeyPilot.TryNextMine(flow.Game!, out x, out y), Is.True);
            Assert.That(flow.Reveal(x, y), Is.EqualTo(MoveResult.Exploded));
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
            Assert.Throws<ArgumentOutOfRangeException>(() => Create(new BoardConfig { MineCount = 0 }));
            Assert.Throws<ArgumentNullException>(() => new MinesweeperFlow(new BoardConfig(), null!));
        }

        [Test]
        public void Submit_DuringPlay_KeepsCurrentGame()
        {
            var flow = Playing();
            var game = flow.Game;
            flow.Submit();
            Assert.That(flow.Game, Is.SameAs(game));
        }

        [Test]
        public void Moves_OnTitle_AreRejected()
        {
            var flow = Create();
            Assert.That(flow.Reveal(0, 0), Is.EqualTo(MoveResult.Rejected));
            Assert.That(flow.ToggleFlag(0, 0), Is.EqualTo(MoveResult.Rejected));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None));
        }

        [Test]
        public void Win_GoesToResult_AndRetryUsesNewSeed()
        {
            var flow = Playing();
            var firstSeed = flow.Game!.Seed;
            PlayToWin(flow);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Game!.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(flow.TakeEvents() & FlowEvents.Won, Is.EqualTo(FlowEvents.Won));

            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game!.Seed, Is.Not.EqualTo(firstSeed));
            Assert.That(flow.Game.Phase, Is.EqualTo(GamePhase.Ready));
        }

        [Test]
        public void Lose_GoesToResult_AndRejectsFurtherMoves()
        {
            var flow = Playing();
            PlayToLose(flow);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Exploded | FlowEvents.Cascade).Or.EqualTo(FlowEvents.Exploded | FlowEvents.Opened));
            Assert.That(flow.Reveal(0, 0), Is.EqualTo(MoveResult.Rejected));
            Assert.That(flow.ToggleFlag(0, 0), Is.EqualTo(MoveResult.Rejected));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None));
        }

        [Test]
        public void Events_DistinguishSingleOpenCascadeAndFlags()
        {
            var flow = Playing();
            flow.Reveal(8, 8);
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Cascade));

            AnswerKeyPilot.TryNextMine(flow.Game!, out var x, out var y);
            flow.ToggleFlag(x, y);
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Flagged));
            flow.ToggleFlag(x, y);
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Unflagged));
        }

        [Test]
        public void RejectedMove_RaisesNoEvent()
        {
            var flow = Playing();
            flow.Reveal(8, 8);
            flow.TakeEvents();
            Assert.That(flow.Reveal(8, 8), Is.EqualTo(MoveResult.Rejected));
            Assert.That(flow.Reveal(-1, 0), Is.EqualTo(MoveResult.Rejected));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None));
        }

        [Test]
        public void FocusLost_RejectsEveryOperationUntilRestored()
        {
            var flow = Playing();
            var game = flow.Game;
            flow.SetFocus(false);
            Assert.That(flow.Reveal(8, 8), Is.EqualTo(MoveResult.Rejected));
            Assert.That(flow.ToggleFlag(8, 8), Is.EqualTo(MoveResult.Rejected));
            flow.Back();
            Assert.That(flow.Game, Is.SameAs(game));

            flow.SetFocus(true);
            Assert.That(flow.Reveal(8, 8), Is.EqualTo(MoveResult.Changed));
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
        public void Back_ReturnsToTitleAndDiscardsGame()
        {
            var flow = Playing();
            flow.Reveal(8, 8);
            flow.Back();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None));
        }

        [Test]
        public void Pilot_BeforeFirstReveal_PicksCenterAndKnowsNoMine()
        {
            var flow = Playing();
            Assert.That(AnswerKeyPilot.TryNextSafe(flow.Game!, out var x, out var y), Is.True);
            Assert.That((x, y), Is.EqualTo((8, 8)));
            Assert.That(AnswerKeyPilot.TryNextMine(flow.Game!, out _, out _), Is.False);
        }
    }
}
