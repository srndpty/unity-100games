using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class FroggerFlowTests
    {
        private static FroggerFlow Start()
        { var flow = new FroggerFlow(new FroggerConfig()); flow.Submit(); return flow; }
        [Test]
        public void StartWinReplay_ResetEverything()
        {
            var flow = Start();
            for (var i = 0; i < 200 && flow.Screen == AppScreen.Playing; i++)
            { flow.RequestMove(Autopilot.Next(flow.Game!)); flow.StepOneTick(); }
            Assert.That(flow.Game!.Result, Is.EqualTo(RoundResult.Won)); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            flow.RequestMove(MoveDirection.Down); flow.StepOneTick(); Assert.That(flow.Game.Row, Is.EqualTo(8));
            flow.Submit(); Assert.That(flow.Game.Row, Is.Zero); Assert.That(flow.Game.TickCount, Is.Zero);
        }
        [Test]
        public void SimultaneousAndRepeatedInput_OnePriorityMove()
        {
            var flow = Start(); flow.RequestMove(MoveDirection.Right); flow.RequestMove(MoveDirection.Up); flow.RequestMove(MoveDirection.Up);
            flow.StepOneTick(); flow.StepOneTick();
            Assert.That(flow.Game!.Row, Is.EqualTo(1)); Assert.That(flow.Game.X, Is.EqualTo(5.5));
        }
        [TestCase(true)]
        [TestCase(false)]
        public void FocusAndPause_BothOrders_StayPaused(bool pauseFirst)
        {
            var flow = Start(); flow.RequestMove(MoveDirection.Up);
            if (pauseFirst) { flow.TogglePause(); flow.SetFocus(false); }
            else { flow.SetFocus(false); flow.TogglePause(); }
            flow.Advance(1); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.PendingMove, Is.EqualTo(MoveDirection.None)); flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused)); flow.TogglePause(); flow.StepOneTick(); Assert.That(flow.Game!.Row, Is.Zero);
        }
        [Test]
        public void SmallTimePartitions_CountExactlySixTicks()
        {
            var flow = Start(); for (var i = 0; i < 10; i++) { flow.Advance(.01); }
            Assert.That(flow.Game!.TickCount, Is.EqualTo(6));
        }
        [Test]
        public void Pause_ClearsFractionAndPendingInput()
        {
            var flow = Start(); flow.Advance(.01); flow.RequestMove(MoveDirection.Up); flow.TogglePause(); flow.TogglePause(); flow.Advance(.01);
            Assert.That(flow.Game!.TickCount, Is.Zero); Assert.That(flow.PendingMove, Is.EqualTo(MoveDirection.None));
        }
        [Test]
        public void InvalidTimeAndSpike_AreBounded()
        {
            var flow = Start(); flow.Advance(double.NaN); flow.Advance(double.PositiveInfinity); flow.Advance(-1);
            Assert.That(flow.Game!.TickCount, Is.Zero); flow.Advance(100); Assert.That(flow.Game.TickCount, Is.EqualTo(5));
        }
        [Test]
        public void BackFromPlaying_ReturnsToTitleAndClearsState()
        {
            var flow = Start(); flow.RequestMove(MoveDirection.Up); flow.Back(); flow.StepOneTick();
            Assert.That(flow.Game, Is.Null); Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
        }
        [Test]
        public void ReplaysWithSameInputs_AreIdentical()
        {
            var flow = Start();
            for (var i = 0; i < 70; i++) { flow.RequestMove(Autopilot.Next(flow.Game!)); flow.StepOneTick(); }
            var x = flow.Game!.X; var row = flow.Game.Row;
            flow.Back(); flow.Submit();
            for (var i = 0; i < 70; i++) { flow.RequestMove(Autopilot.Next(flow.Game!)); flow.StepOneTick(); }
            Assert.That(flow.Game!.X, Is.EqualTo(x)); Assert.That(flow.Game.Row, Is.EqualTo(row));
        }
    }
}
