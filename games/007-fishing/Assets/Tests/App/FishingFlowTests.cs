using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class FishingFlowTests
    {
        private static FishingFlow Start(int seed = 1)
        { var flow = new FishingFlow(new FishingConfig(), seed); flow.Submit(); flow.Cast(); return flow; }
        private static void Catch(FishingFlow flow)
        {
            for (var i = 0; i < 3600 && flow.Screen == AppScreen.Playing; i++) { Autopilot.Step(flow); }
            Assert.That(flow.Game!.Phase, Is.EqualTo(FishingPhase.Caught));
        }
        [TestCase(FishKind.Crucian)]
        [TestCase(FishKind.Bass)]
        [TestCase(FishKind.Carp)]
        public void AllSpecies_TwentySeeds_AreCatchableWithinMinute(FishKind kind)
        {
            for (var seed = 1; seed <= 20; seed++)
            {
                var flow = new FishingFlow(new FishingConfig(), seed); flow.Submit(); flow.Select(kind); flow.Cast(); Catch(flow);
            }
        }
        [Test]
        public void ThreeDistinctCatches_CompleteSession_AndReplayResets()
        {
            var flow = Start(); Catch(flow); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            flow.Submit(); flow.Cast(); Catch(flow); Assert.That(flow.SpeciesCount, Is.EqualTo(1));
            flow.Submit(); flow.Select(FishKind.Bass); flow.Cast(); Catch(flow);
            flow.Submit(); flow.Select(FishKind.Carp); flow.Cast(); Catch(flow);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Complete)); Assert.That(flow.Throws, Is.EqualTo(4));
            flow.StepOneTick(); Assert.That(flow.CatchCount(FishKind.Carp), Is.EqualTo(1));
            flow.Submit(); Assert.That(flow.SpeciesCount, Is.Zero); Assert.That(flow.Throws, Is.Zero);
        }
        [Test]
        public void Failure_AllowsRetryWithNewSeed_AndBackResets()
        {
            var flow = Start(); var seed = flow.Game!.Seed;
            flow.SetInput(false, false); flow.SetInput(true, true); flow.StepOneTick();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result)); flow.Submit(); flow.Cast();
            Assert.That(flow.Game!.Seed, Is.Not.EqualTo(seed)); Assert.That(flow.SpeciesCount, Is.Zero);
            flow.Back(); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title)); Assert.That(flow.Throws, Is.Zero);
        }
        [TestCase(true)]
        [TestCase(false)]
        public void FocusLossAndPause_BothOrdersStayPaused(bool pauseFirst)
        {
            var flow = Start(); flow.SetInput(false, false); flow.SetInput(true, true);
            if (pauseFirst) { flow.TogglePause(); flow.SetFocus(false); } else { flow.SetFocus(false); flow.TogglePause(); }
            flow.Advance(1); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused)); Assert.That(flow.Game!.TickCount, Is.Zero);
            flow.SetFocus(true); flow.TogglePause(); flow.StepOneTick(); Assert.That(flow.Game!.Phase, Is.EqualTo(FishingPhase.Casting));
        }
        [Test]
        public void HeldInput_IsBlockedAcrossCast_ButContinuesAfterHook()
        {
            var flow = Start(); flow.SetInput(true, true); flow.StepOneTick(); Assert.That(flow.Game!.Finished, Is.False);
            while (flow.Game.Phase != FishingPhase.Biting) { flow.StepOneTick(); }
            flow.SetInput(false, false); flow.SetInput(true, true); flow.StepOneTick();
            var tension = flow.Game.Tension; flow.SetInput(false, true); flow.StepOneTick();
            Assert.That(flow.Game.Tension, Is.GreaterThan(tension));
        }
        [Test]
        public void Time_SmallFractionsAndLargeDelta_RespectTickBudget()
        {
            var flow = Start(); for (var i = 0; i < 10; i++) { flow.Advance(.01); }
            Assert.That(flow.Game!.TickCount, Is.EqualTo(6)); flow.Advance(10); Assert.That(flow.Game.TickCount, Is.EqualTo(11));
            flow.Advance(double.NaN); flow.Advance(-1); Assert.That(flow.Game.TickCount, Is.EqualTo(11));
            flow.Advance(1e100); Assert.That(flow.Game.TickCount, Is.EqualTo(16));
            flow.Advance(.005); flow.TogglePause(); flow.TogglePause(); flow.Advance(.012);
            Assert.That(flow.Game.TickCount, Is.EqualTo(16));
        }
        [Test]
        public void InvalidTransitions_DoNotCreateOrAdvanceGame()
        {
            var flow = new FishingFlow(new FishingConfig(), 1); flow.Cast(); flow.SetInput(true, true); flow.StepOneTick();
            Assert.That(flow.Game, Is.Null); flow.Submit(); flow.Select((FishKind)99); flow.Shift(-1);
            Assert.That(flow.Selected, Is.EqualTo(FishKind.Carp)); flow.SetFocus(false); flow.Cast(); Assert.That(flow.Game, Is.Null);
        }
    }
}
