using System;
using Game.App;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class FlowTests
    {
        [Test]
        public void ThirdGame_IsReproducibleFromItsOwnSeed_AndConfigIsCopied()
        {
            SnakeConfig config = new();
            SnakeFlow flow = new(config, int.MaxValue);
            config.StepsPerSecond = 1;
            Assert.That(flow.TickDuration, Is.EqualTo(.125));
            for (var match = 0; match < 3; match++)
            {
                flow.Submit();
                SnakeState direct = new(new SnakeConfig(), flow.GameSeed);
                while (flow.Screen == AppScreen.Playing)
                {
                    flow.StepOneTick();
                    direct.Step(Direction.Right);
                    Assert.That(flow.Game!.Head, Is.EqualTo(direct.Head));
                    Assert.That(flow.Game.Food, Is.EqualTo(direct.Food));
                    Assert.That(flow.Game.Phase, Is.EqualTo(direct.Phase));
                }
            }
            Assert.That(flow.GameSeed, Is.EqualTo(int.MinValue + 1));
        }

        [Test]
        public void MaximumFiniteTime_DoesNotOverflow_AndInvalidDirectionIsIgnored()
        {
            SnakeFlow flow = new(new SnakeConfig { Width = SnakeConfig.MaxWidth }, 1);
            flow.Submit();
            Assert.That(flow.RequestDirection((Direction)99), Is.False);
            flow.Advance(double.MaxValue);
            Assert.That(flow.Game!.TickCount, Is.EqualTo(5));
        }

        [Test]
        public void NonBinaryTickDuration_DoesNotDoubleCountRemainder()
        {
            SnakeFlow flow = new(new SnakeConfig { Width = SnakeConfig.MaxWidth, StepsPerSecond = 10f }, 1);
            flow.Submit();
            flow.Advance(1d);
            flow.Advance(.001d);
            Assert.That(flow.Game!.TickCount, Is.EqualTo(5));
            flow.Advance(.099d);
            Assert.That(flow.Game.TickCount, Is.EqualTo(6));
        }

        [Test]
        public void TwoTurns_AreConsumedInOrder_AndIllegalTurnsAreIgnored()
        {
            SnakeFlow flow = new(new SnakeConfig(), 1);
            flow.Submit();
            Assert.That(flow.RequestDirection(Direction.Left), Is.False);
            Assert.That(flow.RequestDirection(Direction.Up), Is.True);
            Assert.That(flow.RequestDirection(Direction.Down), Is.False);
            Assert.That(flow.RequestDirection(Direction.Up), Is.False);
            Assert.That(flow.RequestDirection(Direction.Left), Is.True);
            Assert.That(flow.RequestDirection(Direction.Down), Is.False);
            flow.Advance(flow.TickDuration / 2);
            Assert.That(flow.PendingTurns, Is.EqualTo(2));
            flow.Advance(flow.TickDuration / 2);
            Assert.That(flow.Game!.Direction, Is.EqualTo(Direction.Up));
            flow.StepOneTick();
            Assert.That(flow.Game.Direction, Is.EqualTo(Direction.Left));
            Assert.That(flow.PendingTurns, Is.Zero);
        }

        [Test]
        public void Pause_ClearsInputAndPartialTime_AndBackResetsEverything()
        {
            SnakeFlow flow = new(new SnakeConfig(), 3);
            Assert.That(flow.RequestDirection(Direction.Up), Is.False);
            flow.Submit();
            flow.RequestDirection(Direction.Up);
            flow.Advance(flow.TickDuration / 2);
            flow.TogglePause();
            Assert.That(flow.PendingTurns, Is.Zero);
            flow.Advance(100);
            Assert.That(flow.RequestDirection(Direction.Up), Is.False);
            Assert.That(flow.Game!.TickCount, Is.Zero);
            flow.TogglePause();
            flow.Advance(flow.TickDuration / 2);
            Assert.That(flow.Game.TickCount, Is.Zero);
            flow.Advance(flow.TickDuration / 2);
            Assert.That(flow.Game.TickCount, Is.EqualTo(1));
            flow.Back();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
            flow.Advance(1);
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [TestCase(0d)]
        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void InvalidTime_IsIgnored(double time)
        {
            SnakeFlow flow = new(new SnakeConfig(), 4);
            flow.Submit();
            flow.Advance(time);
            Assert.That(flow.Game!.TickCount, Is.Zero);
        }

        [Test]
        public void LargeFrame_IsCapped_AndDoesNotCarryWholeTicks()
        {
            SnakeFlow flow = new(new SnakeConfig { Width = SnakeConfig.MaxWidth }, 4);
            flow.Submit();
            flow.Advance(100);
            Assert.That(flow.Game!.TickCount, Is.EqualTo(5));
            flow.Advance(flow.TickDuration / 2);
            Assert.That(flow.Game.TickCount, Is.EqualTo(5));
        }

        [Test]
        public void FrameSplitting_ProducesSameState()
        {
            SnakeFlow a = new(new SnakeConfig(), 10);
            SnakeFlow b = new(new SnakeConfig(), 10);
            a.Submit(); b.Submit();
            a.Advance(.5);
            for (var i = 0; i < 16; i++) { b.Advance(.03125); }
            Assert.That(a.Game!.Head, Is.EqualTo(b.Game!.Head));
            Assert.That(a.Game.TickCount, Is.EqualTo(b.Game.TickCount));
        }

        [Test]
        public void ResultStopsUpdates_AndReplayUsesIndependentSeed()
        {
            SnakeFlow flow = new(new SnakeConfig(), 10);
            flow.Submit();
            var original = flow.Game;
            flow.Submit();
            Assert.That(flow.Game, Is.SameAs(original));
            while (flow.Screen == AppScreen.Playing) { flow.StepOneTick(); }
            var tick = flow.Game!.TickCount;
            flow.StepOneTick(); flow.Advance(10); flow.TogglePause();
            Assert.That(flow.Game.TickCount, Is.EqualTo(tick));
            Assert.That(flow.RequestDirection(Direction.Up), Is.False);
            flow.Submit();
            Assert.That(flow.GameSeed, Is.EqualTo(11));
            SnakeState direct = new(new SnakeConfig(), flow.GameSeed);
            Assert.That(flow.Game!.Food, Is.EqualTo(direct.Food));
            Assert.That(flow.Game.Score, Is.Zero);
            Assert.That(flow.Game.TickCount, Is.Zero);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(42)]
        public void Autopilot_FillsDefaultBoard_WithNoMissingSegments(int seed)
        {
            SnakeFlow flow = new(new SnakeConfig(), seed);
            flow.Submit();
            SnakeState replay = new(new SnakeConfig(), seed);
            var game = flow.Game!;
            for (var i = 0; i < 110000 && flow.Screen == AppScreen.Playing; i++)
            {
                var direction = Autopilot.Next(game);
                flow.RequestDirection(direction);
                flow.StepOneTick();
                replay.Step(direction);
                Assert.That(game.Head, Is.EqualTo(replay.Head));
                Assert.That(game.Food, Is.EqualTo(replay.Food));
                Assert.That(game.Length, Is.EqualTo(replay.Length));
                if (i % 100 == 0 || game.Phase != GamePhase.Playing) { AssertBody(game); }
            }
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(game.Score, Is.EqualTo(317));
            Assert.That(game.Food, Is.Null);
            var tick = game.TickCount;
            flow.StepOneTick();
            Assert.That(game.TickCount, Is.EqualTo(tick));
        }

        private static void AssertBody(SnakeState game)
        {
            System.Collections.Generic.HashSet<Cell> cells = new();
            for (var i = 0; i < game.Length; i++)
            {
                var cell = game.SegmentAt(i);
                Assert.That(cells.Add(cell), Is.True);
                Assert.That(game.Occupies(cell), Is.True);
                if (i > 0)
                {
                    var previous = game.SegmentAt(i - 1);
                    Assert.That(Math.Abs(cell.X - previous.X) + Math.Abs(cell.Y - previous.Y), Is.EqualTo(1));
                }
            }
            Assert.That(game.Length, Is.EqualTo(3 + game.Score));
            if (game.Food.HasValue) { Assert.That(cells.Contains(game.Food.Value), Is.False); }
        }
    }
}
