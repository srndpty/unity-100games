using System;
using System.Collections.Generic;
using System.Reflection;
using Game.App;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class AsteroidsFlowTests
    {
        [Test]
        public void Transitions_RespectScreenAndFocus()
        {
            var flow = new AsteroidsFlow(new AsteroidsConfig(), 1);
            flow.SetInput(true, false, true, true); Assert.That(flow.Input.Fire, Is.False);
            flow.Submit(); flow.Submit(); Assert.That(flow.GameSeed, Is.EqualTo(1));
            flow.SetInput(true, true, true, true); Assert.That(flow.Input.Turn, Is.Zero); Assert.That(flow.Input.Thrust, Is.True);
            flow.TogglePause(); flow.StepOneTick(); Assert.That(flow.Game!.TickCount, Is.Zero); Assert.That(flow.Input.Fire, Is.False);
            flow.SetFocus(false); flow.TogglePause(); flow.Submit(); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.SetFocus(true); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused)); flow.TogglePause();
            flow.SetInput(false, true, false, true); Assert.That(flow.Input.Turn, Is.EqualTo(-1));
            flow.Back(); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title)); Assert.That(flow.Game, Is.Null);
        }
        [TestCase(true)]
        [TestCase(false)]
        public void FocusAndPauseOrder_AlwaysStops(bool focusFirst)
        {
            var flow = new AsteroidsFlow(new AsteroidsConfig(), 1); flow.Submit();
            if (focusFirst) { flow.SetFocus(false); flow.TogglePause(); } else { flow.TogglePause(); flow.SetFocus(false); }
            flow.SetFocus(true); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused)); Assert.That(flow.Input.Fire, Is.False);
        }
        [Test]
        public void TimeAccounting_UsesOneQuotientAndRemainder()
        {
            var flow = new AsteroidsFlow(new AsteroidsConfig(), 1); flow.Submit();
            for (var i = 0; i < 10; i++) { flow.Advance(.01); }
            Assert.That(flow.Game!.TickCount, Is.EqualTo(6));
            flow.Advance(0); flow.Advance(-1); flow.Advance(double.NaN); flow.Advance(double.PositiveInfinity);
            Assert.That(flow.Game.TickCount, Is.EqualTo(6));
            flow.Advance(double.MaxValue); Assert.That(flow.Game.TickCount, Is.EqualTo(11));
        }
        [Test]
        public void Pause_DiscardsFractionAndHeldIntent()
        {
            var flow = new AsteroidsFlow(new AsteroidsConfig(), 1); flow.Submit(); flow.Advance(.01);
            flow.SetInput(true, false, true, true); flow.TogglePause(); flow.TogglePause(); flow.Advance(.01);
            Assert.That(flow.Game!.TickCount, Is.Zero); Assert.That(flow.Input.Thrust, Is.False);
        }
        private static void Clear(AsteroidsFlow flow)
        {
            var rocks = (List<Asteroid>)typeof(AsteroidsState).GetField("_asteroids", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flow.Game)!;
            rocks.Clear(); flow.StepOneTick();
        }
        [Test]
        public void ThirdMatch_IsReproducibleIndependently()
        {
            var config = new AsteroidsConfig { WaveCount = 1 }; var flow = new AsteroidsFlow(config, 10);
            for (var i = 0; i < 2; i++) { flow.Submit(); Clear(flow); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result)); }
            flow.Submit(); var independent = new AsteroidsFlow(config, 12); independent.Submit();
            for (var i = 0; i < 100; i++)
            {
                flow.SetInput(true, false, true, true); independent.SetInput(true, false, true, true); flow.StepOneTick(); independent.StepOneTick();
            }
            Assert.That(flow.GameSeed, Is.EqualTo(12)); Assert.That(flow.Game!.ShipPosition.X, Is.EqualTo(independent.Game!.ShipPosition.X)); Assert.That(flow.Game.Score, Is.EqualTo(independent.Game.Score));
        }
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(123)]
        public void Autopilot_CompletesThreeWaves(int seed)
        {
            var config = new AsteroidsConfig(); var flow = new AsteroidsFlow(config, seed); flow.Submit();
            for (var tick = 0; tick < 60000 && flow.Screen == AppScreen.Playing; tick++)
            {
                var command = Autopilot.Next(flow.Game!, config);
                flow.SetInput(command.Turn > 0, command.Turn < 0, command.Thrust, command.Fire); flow.StepOneTick();
            }
            TestContext.WriteLine($"seed={seed}, tick={flow.Game!.TickCount}, phase={flow.Game.Phase}, wave={flow.Game.Wave}, score={flow.Game.Score}, lives={flow.Game.Lives}");
            Assert.That(flow.Game.Phase, Is.EqualTo(GamePhase.Won)); Assert.That(flow.Game.Score, Is.EqualTo(6240));
        }
    }
}
