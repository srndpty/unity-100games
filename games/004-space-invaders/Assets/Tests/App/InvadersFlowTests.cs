using System;
using Game.App;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class InvadersFlowTests
    {
        [Test]
        public void StartPauseReplayAndBack_RespectTransitions()
        {
            var flow = new InvadersFlow(new InvadersConfig(), 10);
            flow.StepOneTick(); Assert.That(flow.Game, Is.Null);
            flow.Submit(); flow.SetInput(false, true, true); flow.StepOneTick();
            flow.TogglePause(); var tick = flow.Game!.TickCount;
            flow.Advance(1); flow.SetInput(true, false, true); flow.StepOneTick();
            Assert.That(flow.Game.TickCount, Is.EqualTo(tick)); Assert.That(flow.Input.Move, Is.Zero);
            flow.TogglePause(); Assert.That(flow.Input.Fire, Is.False);
            flow.Back(); flow.Submit(); Assert.That(flow.GameSeed, Is.EqualTo(11)); Assert.That(flow.Game!.Score, Is.Zero);
        }
        [TestCase(true)]
        [TestCase(false)]
        public void FocusAndPauseOrder_StaysPaused(bool pauseFirst)
        {
            var flow = new InvadersFlow(new InvadersConfig(), 1); flow.Submit(); flow.SetInput(true, false, true);
            if (pauseFirst) { flow.TogglePause(); }
            flow.SetFocus(false);
            flow.TogglePause(); flow.Submit(); flow.SetInput(false, true, true);
            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused)); Assert.That(flow.Input.Fire, Is.False);
            flow.TogglePause(); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }
        [Test]
        public void FractionalTimeAndCatchup_AreConsistent()
        {
            var flow = new InvadersFlow(new InvadersConfig(), 1); flow.Submit();
            flow.Advance(1d / 120); Assert.That(flow.Game!.TickCount, Is.Zero);
            flow.Advance(1d / 120); Assert.That(flow.Game.TickCount, Is.EqualTo(1));
            flow.Advance(.1); Assert.That(flow.Game.TickCount, Is.EqualTo(6));
            flow.Advance(1d / 60); Assert.That(flow.Game.TickCount, Is.EqualTo(7));
            flow.Advance(double.MaxValue); Assert.That(flow.Game.TickCount, Is.EqualTo(12));
            flow.Advance(double.NaN); flow.Advance(double.PositiveInfinity); flow.Advance(-1);
            Assert.That(flow.Game.TickCount, Is.EqualTo(12));
        }
        [Test]
        public void BothDirectionsCancel_AndPauseDiscardsFraction()
        {
            var flow = new InvadersFlow(new InvadersConfig(), 1); flow.Submit();
            flow.SetInput(true, true, true); Assert.That(flow.Input.Move, Is.Zero);
            flow.Advance(1d / 120); flow.TogglePause(); flow.TogglePause(); flow.Advance(1d / 120);
            Assert.That(flow.Game!.TickCount, Is.Zero);
        }
        [Test]
        public void ResultAndThirdMatch_AreIndependent()
        {
            var flow = new InvadersFlow(new InvadersConfig(), 20);
            for (var match = 0; match < 3; match++)
            {
                flow.Submit();
                Assert.That(flow.GameSeed, Is.EqualTo(20 + match));
                var replay = new InvadersState(new InvadersConfig(), flow.GameSeed);
                for (var i = 0; i < 60000 && flow.Screen == AppScreen.Playing; i++) { flow.StepOneTick(); replay.Step(default); }
                Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
                Assert.That(flow.Game!.TickCount, Is.EqualTo(replay.TickCount));
                Assert.That(flow.Game.Lives, Is.EqualTo(replay.Lives));
                flow.SetInput(true, false, true); var tick = flow.Game.TickCount; flow.Advance(1); Assert.That(flow.Game.TickCount, Is.EqualTo(tick));
            }
        }
        [TestCase(1)]
        [TestCase(123)]
        [TestCase(2026)]
        public void Autopilot_CompletesThreeWaves(int seed)
        {
            var config = new InvadersConfig(); var game = new InvadersState(config, seed);
            for (var i = 0; i < 60000 && game.Phase != GamePhase.Won && game.Phase != GamePhase.Lost; i++) { game.Step(Autopilot.Next(game, config)); }
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Won), "Wave " + game.Wave + " score " + game.Score + " tick " + game.TickCount);
            Assert.That(game.Score, Is.EqualTo(9600));
        }
    }
}
