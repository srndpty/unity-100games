using System;
using System.Reflection;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class FlappyFlowTests
    {
        [Test]
        public void CompleteLoop_ReadyFlapLossReplayTitle()
        {
            var flow = new FlappyFlow(new FlappyConfig(), 100); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            flow.RequestFlap(); flow.StepOneTick(); Assert.That(flow.Game, Is.Null);
            flow.Submit(); flow.Advance(1); Assert.That(flow.Game!.TickCount, Is.Zero);
            flow.RequestFlap(); flow.StepOneTick(); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            flow.Submit(); Assert.That(flow.GameSeed, Is.EqualTo(100));
            for (var i = 0; i < 200; i++) { flow.StepOneTick(); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            flow.RequestFlap(); Assert.That(flow.PendingFlap, Is.False);
            flow.Submit(); Assert.That(flow.GameSeed, Is.EqualTo(101)); Assert.That(flow.Game!.Score, Is.Zero);
            flow.Back(); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title)); Assert.That(flow.Game, Is.Null);
        }
        [Test]
        public void PendingFlap_SurvivesFractionalFrame_AndIsConsumedOnce()
        {
            var flow = new FlappyFlow(new FlappyConfig(), 1); flow.Submit(); flow.RequestFlap(); flow.RequestFlap();
            flow.Advance(.001); Assert.That(flow.PendingFlap, Is.True);
            flow.Advance(.05); Assert.That(flow.Game!.TickCount, Is.EqualTo(3)); Assert.That(flow.Game.Velocity, Is.EqualTo(5.1f).Within(.0001));
            Assert.That(flow.PendingFlap, Is.False);
        }
        [TestCase(true)]
        [TestCase(false)]
        public void FocusAndPause_BothOrdersDiscardInput(bool pauseFirst)
        {
            var flow = new FlappyFlow(new FlappyConfig(), 1); flow.Submit(); flow.RequestFlap(); flow.StepOneTick();
            flow.RequestFlap(); if (pauseFirst) { flow.TogglePause(); }
            flow.SetFocus(false); if (!pauseFirst) { flow.TogglePause(); }
            flow.RequestFlap(); flow.Advance(1); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused)); Assert.That(flow.PendingFlap, Is.False);
            flow.SetFocus(true); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused)); flow.TogglePause();
            flow.Advance(1.0 / 60); Assert.That(flow.Game!.Velocity, Is.EqualTo(5.4f).Within(.0001));
        }
        [Test]
        public void ReadyPause_RestoresReady_AndClearsPendingAndTime()
        {
            var flow = new FlappyFlow(new FlappyConfig(), 1); flow.Submit(); flow.RequestFlap(); flow.Advance(.001);
            flow.TogglePause(); flow.TogglePause(); flow.Advance(1); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Ready));
            Assert.That(flow.Game!.TickCount, Is.Zero); Assert.That(flow.PendingFlap, Is.False);
        }
        [Test]
        public void TimeAccumulation_HandlesDecimalFractionsAndFrameCap()
        {
            var flow = new FlappyFlow(new FlappyConfig(), 1); flow.Submit(); flow.RequestFlap(); flow.StepOneTick();
            for (var i = 0; i < 10; i++) { flow.Advance(.01); }
            Assert.That(flow.Game!.TickCount, Is.EqualTo(7));
            flow.Advance(.1); Assert.That(flow.Game.TickCount, Is.EqualTo(12));
            flow.Advance(0); flow.Advance(-1); flow.Advance(double.NaN); flow.Advance(double.PositiveInfinity);
            Assert.That(flow.Game.TickCount, Is.EqualTo(12));
        }
        [Test]
        public void ReplaySeed_IsIndependentlyReproducible()
        {
            var flow = new FlappyFlow(new FlappyConfig(), 6); flow.Submit(); flow.Back(); flow.Submit();
            var separate = new FlappyState(new FlappyConfig(), flow.GameSeed);
            flow.RequestFlap(); flow.StepOneTick(); separate.Step(new FlappyCommand(true));
            Assert.That(flow.Game!.BirdY, Is.EqualTo(separate.BirdY));
            Assert.That(flow.Game.PipeAt(0), Is.EqualTo(separate.PipeAt(0)));
        }
        [TestCase(1, false)]
        [TestCase(42, false)]
        [TestCase(12345, false)]
        [TestCase(0, true)]
        [TestCase(71, true)]
        public void Autopilot_LongRun_ReusesBoundedPipesAndReachesDifficultyCap(int seed, bool maximum)
        {
            var c = maximum ? new FlappyConfig { BirdRadius = .6f, Gravity = -30, FlapSpeed = 9, TerminalSpeed = 16, PipeWidth = 2, Spacing = 5, Speed = 5, SpeedIncrease = .4f, Gap = 4.75f, GapDecrease = .25f, WallMargin = 1.5f } : new FlappyConfig();
            var state = new FlappyState(c, seed); var centers = new System.Collections.Generic.Dictionary<long, float>();
            for (var tick = 0; tick < 10000 && !state.Lost; tick++)
            {
                state.Step(Autopilot.Next(state, c)); Assert.That(state.PipeCount, Is.EqualTo(c.Capacity));
                for (var i = 0; i < state.PipeCount; i++)
                {
                    var p = state.PipeAt(i); Assert.That(Math.Abs(p.Center) + p.Gap / 2 + c.WallMargin, Is.LessThanOrEqualTo(7.00001f));
                    if (!centers.ContainsKey(p.Generation))
                    {
                        if (centers.TryGetValue(p.Generation - 1, out var previous)) { Assert.That(Math.Abs(p.Center - previous), Is.LessThanOrEqualTo(c.MaxCenterChange + .00001f)); }
                        centers.Add(p.Generation, p.Center);
                    }
                    Assert.That(p.Gap, Is.GreaterThanOrEqualTo(3.5f));
                }
            }
            Assert.That(state.Lost, Is.False, "seed=" + seed + " tick=" + state.TickCount + " score=" + state.Score);
            Assert.That(state.Score, Is.GreaterThan(100)); Assert.That(state.Level, Is.EqualTo(5));
            Assert.That(centers.Count, Is.GreaterThan(c.Capacity * 10));
        }
        [Test]
        public void NewPipesUseNewGap_ExistingPipesKeepTheirGap()
        {
            var c = new FlappyConfig(); var state = new FlappyState(c, 42);
            typeof(FlappyState).GetProperty("Score")!.SetValue(state, 25L);
            var old = state.PipeAt(1); var regenerated = false;
            for (var i = 0; i < 1000 && !state.Lost; i++)
            {
                state.Step(Autopilot.Next(state, c));
                for (var j = 0; j < state.PipeCount; j++)
                { if (state.PipeAt(j).Generation >= c.Capacity) { Assert.That(state.PipeAt(j).Gap, Is.EqualTo(3.5f)); regenerated = true; } }
                if (regenerated) { break; }
            }
            Assert.That(old.Gap, Is.EqualTo(4.5f)); Assert.That(regenerated, Is.True);
        }
    }
}
