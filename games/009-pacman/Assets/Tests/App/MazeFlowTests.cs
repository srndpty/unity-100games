using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class MazeFlowTests
    {
        private static MazeFlow Create(MazeGameConfig? config = null)
        {
            var seed = 100;
            return new MazeFlow(config ?? new MazeGameConfig(), () => seed++);
        }

        private static MazeFlow Playing(MazeGameConfig? config = null)
        {
            var flow = Create(config);
            flow.Submit();
            return flow;
        }

        [Test]
        public void Starts_OnTitleWithoutGame()
        {
            var flow = Create();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
        }

        [Test]
        public void Submit_StartsOnlyFromTitleOrResult()
        {
            var flow = Playing();
            var game = flow.Game;
            flow.Submit();
            Assert.That(flow.Game, Is.SameAs(game));
            flow.TogglePause();
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
        }

        [Test]
        public void Replay_UsesNewSeed()
        {
            var config = new MazeGameConfig { ReadyTicks = 1, DyingTicks = 1, Lives = 1, CatSpeed = 200 };
            var flow = Playing(config);
            var first = flow.Game!.Seed;
            for (var i = 0; i < 20000 && flow.Screen == AppScreen.Playing; i++) { flow.StepOneTick(); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game!.Seed, Is.Not.EqualTo(first));
        }

        [Test]
        public void Result_IgnoresDirectionsAndTicks()
        {
            var config = new MazeGameConfig { ReadyTicks = 1, DyingTicks = 1, Lives = 1, CatSpeed = 200 };
            var flow = Playing(config);
            for (var i = 0; i < 20000 && flow.Screen == AppScreen.Playing; i++) { flow.StepOneTick(); }
            var tick = flow.Game!.TickCount;
            flow.RequestDirection(Direction.Up);
            flow.StepOneTick();
            flow.Advance(1);
            Assert.That(flow.PendingDirection, Is.EqualTo(Direction.None));
            Assert.That(flow.Game.TickCount, Is.EqualTo(tick));
        }

        [Test]
        public void RequestDirection_PrefersFixedOrderWithinTick()
        {
            var flow = Playing();
            flow.RequestDirection(Direction.Right);
            flow.RequestDirection(Direction.Left);
            flow.RequestDirection(Direction.Down);
            Assert.That(flow.PendingDirection, Is.EqualTo(Direction.Left));
            flow.StepOneTick();
            Assert.That(flow.PendingDirection, Is.EqualTo(Direction.None));
        }

        [Test]
        public void RequestDirection_RejectsOutOfRange()
        {
            var flow = Playing();
            Assert.Throws<ArgumentOutOfRangeException>(() => flow.RequestDirection((Direction)7));
        }

        [Test]
        public void Pause_StopsTicksAndClearsPendingInput()
        {
            var flow = Playing();
            flow.RequestDirection(Direction.Left);
            flow.TogglePause();
            var tick = flow.Game!.TickCount;
            flow.Advance(1);
            Assert.That(flow.Game.TickCount, Is.EqualTo(tick));
            Assert.That(flow.PendingDirection, Is.EqualTo(Direction.None));
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void FocusLoss_PausesAndRequiresManualResume()
        {
            var flow = Playing();
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間は再開しない");
            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "復帰しても自動で再開しない");
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void PauseThenFocusLoss_StaysPaused()
        {
            var flow = Playing();
            flow.TogglePause();
            flow.SetFocus(false);
            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
        }

        [Test]
        public void Advance_LimitsTicksPerFrame()
        {
            var flow = Playing();
            flow.Advance(10);
            Assert.That(flow.Game!.TickCount, Is.EqualTo(MazeFlow.MaxTicksPerFrame));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(-1d)]
        [TestCase(0d)]
        public void Advance_IgnoresInvalidTime(double seconds)
        {
            var flow = Playing();
            flow.Advance(seconds);
            Assert.That(flow.Game!.TickCount, Is.Zero);
        }

        [Test]
        public void Advance_SmallStepsAccumulateWithoutDrift()
        {
            var flow = Playing();
            for (var i = 0; i < 6000; i++) { flow.Advance(0.001); }
            Assert.That(flow.Game!.TickCount, Is.EqualTo(360));
        }

        [Test]
        public void TakeEvents_AccumulatesAcrossTicksAndClears()
        {
            var config = new MazeGameConfig { ReadyTicks = 1 };
            var flow = Playing(config);
            for (var i = 0; i < 30; i++)
            {
                flow.RequestDirection(Direction.Left);
                flow.StepOneTick();
            }

            Assert.That(flow.TakeEvents() & GameEvents.DotEaten, Is.EqualTo(GameEvents.DotEaten));
            Assert.That(flow.TakeEvents(), Is.EqualTo(GameEvents.None));
        }

        [Test]
        public void Back_ReturnsToTitleAndDropsGame()
        {
            var flow = Playing();
            flow.Back();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void Autopilot_AlwaysReachesAnEnding(int seed)
        {
            var game = new MazeGameState(new MazeGameConfig(), seed);
            var pilot = new Autopilot(seed);
            for (var i = 0; i < 200000 && !game.IsOver; i++) { game.Step(pilot.Next(game)); }
            Assert.That(game.IsOver, Is.True);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Autopilot_ClearsMazeWhenCatsStayHome(int seed)
        {
            var config = new MazeGameConfig
            {
                CatSpeed = 1,
                ReleaseTicks = new[] { 0, int.MaxValue, int.MaxValue, int.MaxValue },
            };
            var game = new MazeGameState(config, seed);
            var pilot = new Autopilot(seed);
            for (var i = 0; i < 200000 && !game.IsOver; i++) { game.Step(pilot.Next(game)); }
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(game.PelletsRemaining, Is.Zero);
        }
    }
}
