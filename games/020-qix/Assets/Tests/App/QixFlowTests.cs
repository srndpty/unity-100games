using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class QixFlowTests
    {
        private const long TickLimit = 60L * 60 * 10;

        private static QixFlow Playing(QixConfig? config = null, int seed = 100)
        {
            var flow = new QixFlow(config ?? new QixConfig(), seed);
            flow.Apply(Intent.Of(IntentKind.Submit));
            flow.TakeEvents();
            return flow;
        }

        private static void RunToResult(QixFlow flow)
        {
            while (flow.Screen == AppScreen.Playing && flow.Game!.Ticks < TickLimit) { flow.StepTick(); }
        }

        [Test]
        public void StartsAtTitleWithoutAGame()
        {
            var flow = new QixFlow(new QixConfig(), 1);

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
            flow.StepTick();
            flow.Advance(1);
            Assert.That(flow.Game, Is.Null);
        }

        [Test]
        public void TitleToPlayingToWinToReplayWithNewSeed()
        {
            var flow = new QixFlow(new QixConfig(), 100);
            flow.Apply(Intent.Of(IntentKind.Submit));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game!.Seed, Is.EqualTo(100));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Started));

            flow.Pilot = new Autopilot();
            var events = FlowEvents.None;
            while (flow.Screen == AppScreen.Playing && flow.Game.Ticks < TickLimit)
            {
                flow.StepTick();
                events |= flow.TakeEvents();
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Game.Status, Is.EqualTo(GameStatus.Won));
            Assert.That(events, Is.EqualTo(FlowEvents.DrawStarted | FlowEvents.Closed | FlowEvents.Won));
            Assert.That(flow.BestScore, Is.EqualTo(flow.Game.Score));
            Assert.That(flow.IsNewBest, Is.True);

            flow.Apply(Intent.Of(IntentKind.Submit));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game.Seed, Is.EqualTo(101), "再戦は新しいシード");
            Assert.That(flow.Game.ClaimedCells, Is.Zero);
            Assert.That(flow.IsNewBest, Is.False);
        }

        [Test]
        public void EveryReplayUsesANewSeed()
        {
            var flow = Playing(seed: int.MaxValue);
            var seeds = new HashSet<int> { flow.Game!.Seed };
            for (var i = 0; i < 3; i++)
            {
                flow.Apply(Intent.Of(IntentKind.TogglePause));
                flow.Apply(Intent.Of(IntentKind.Restart));
                Assert.That(seeds.Add(flow.Game!.Seed), Is.True);
            }

            Assert.That(seeds, Does.Contain(int.MinValue), "桁あふれしても止まらない");
        }

        [Test]
        public void LineLeftInTheSeaEventuallyLosesTheLastLife()
        {
            var config = new QixConfig { Lives = 1 };
            var flow = Playing(config);

            flow.Apply(Intent.Down(Control.Draw));
            flow.Apply(Intent.Down(Control.Up));
            for (var i = 0; i < 30; i++) { flow.StepTick(); }
            flow.Apply(Intent.Up(Control.Up));
            var events = flow.TakeEvents();
            RunToResult(flow);
            events |= flow.TakeEvents();

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Game!.Status, Is.EqualTo(GameStatus.Lost));
            Assert.That(events, Is.EqualTo(FlowEvents.DrawStarted | FlowEvents.Miss | FlowEvents.Lost));
        }

        [Test]
        public void AfterAMissDrawKeyMustBeReleasedAndPressedAgain()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Draw));
            flow.Apply(Intent.Down(Control.Up));
            for (var i = 0; i < 20; i++) { flow.StepTick(); }
            flow.Apply(Intent.Up(Control.Up));
            while (flow.Game!.Lives == 3 && flow.Game.Ticks < TickLimit) { flow.StepTick(); }
            Assert.That(flow.Game.Lives, Is.EqualTo(2));

            // Space を押したまま、もう一度 ↑ を押し続ける。
            flow.Apply(Intent.Down(Control.Up));
            for (var i = 0; i < 120; i++) { flow.StepTick(); }
            Assert.That(flow.Game.Lives, Is.EqualTo(2));
            Assert.That(flow.Game.IsDrawing, Is.False);

            flow.Apply(Intent.Up(Control.Draw));
            flow.StepTick();
            flow.Apply(Intent.Down(Control.Draw));
            flow.StepTick();
            Assert.That(flow.Game.IsDrawing, Is.True, "離して押し直すと描ける");
        }

        [Test]
        public void HeldControlsDriveThePlayerOnlyWhilePlaying()
        {
            var flow = new QixFlow(new QixConfig(), 1);
            flow.Apply(Intent.Down(Control.Left));
            flow.Apply(Intent.Of(IntentKind.Submit));
            var startX = flow.Game!.PlayerX;

            flow.StepTick();
            Assert.That(flow.Game.PlayerX, Is.EqualTo(startX), "タイトルで押したキーは持ち越さない");

            flow.Apply(Intent.Down(Control.Left));
            flow.StepTick();
            Assert.That(flow.Game.PlayerX, Is.EqualTo(startX - 1));

            flow.Apply(Intent.Up(Control.Left));
            flow.StepTick();
            flow.StepTick();
            Assert.That(flow.Game.PlayerX, Is.EqualTo(startX - 1));
        }

        [Test]
        public void PauseStopsTimeAndClearsHeldKeys()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Right));
            flow.Apply(Intent.Of(IntentKind.TogglePause));
            var ticks = flow.Game!.Ticks;

            flow.Advance(1);
            flow.StepTick();
            Assert.That(flow.Game.Ticks, Is.EqualTo(ticks));

            flow.Apply(Intent.Of(IntentKind.Submit));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            var x = flow.Game.PlayerX;
            flow.StepTick();
            Assert.That(flow.Game.PlayerX, Is.EqualTo(x), "一時停止の前に押していたキーでは動かない");
        }

        [Test]
        public void PressThenPauseInSameFrameKeepsOrder()
        {
            var flow = Playing();
            flow.Apply(Intent.Of(IntentKind.TogglePause));
            flow.Apply(Intent.Down(Control.Right));
            flow.Apply(Intent.Of(IntentKind.TogglePause));

            var x = flow.Game!.PlayerX;
            flow.StepTick();

            Assert.That(flow.Game.PlayerX, Is.EqualTo(x), "一時停止中に押したキーは数えない");
        }

        [Test]
        public void FocusLossPausesIgnoresInputAndDoesNotAutoResume()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Right));

            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Of(IntentKind.Submit));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間は操作を受け付けない");

            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Of(IntentKind.Submit));
            var x = flow.Game!.PlayerX;
            flow.StepTick();
            Assert.That(flow.Game.PlayerX, Is.EqualTo(x));
        }

        [Test]
        public void RestartAndTitleOnlyFromPauseOrResult()
        {
            var flow = Playing();
            var seed = flow.Game!.Seed;
            flow.Apply(Intent.Of(IntentKind.Restart));
            flow.Apply(Intent.Of(IntentKind.BackToTitle));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game.Seed, Is.EqualTo(seed));

            flow.Apply(Intent.Of(IntentKind.TogglePause));
            flow.Apply(Intent.Of(IntentKind.BackToTitle));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);

            flow.Apply(Intent.Of(IntentKind.Quit));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [TestCase(0.05, 20)]
        [TestCase(0.01, 100)]
        [TestCase(1.0 / 60, 60)]
        public void AdvanceConvertsRealTimeToTicksWithoutLoss(double seconds, int frames)
        {
            var flow = Playing();

            for (var i = 0; i < frames; i++) { flow.Advance(seconds); }

            Assert.That(flow.Game!.Ticks, Is.EqualTo(QixConfig.TickRate));
        }

        [Test]
        public void AdvanceCapsTicksPerFrameAndIgnoresInvalidTime()
        {
            var flow = Playing();

            flow.Advance(10);
            Assert.That(flow.Game!.Ticks, Is.EqualTo(QixFlow.MaxTicksPerFrame));

            foreach (var seconds in new[] { double.NaN, double.PositiveInfinity, -1, 0 })
            {
                flow.Advance(seconds);
            }

            Assert.That(flow.Game.Ticks, Is.EqualTo(QixFlow.MaxTicksPerFrame));
        }

        [Test]
        public void ResultIgnoresGameplayInput()
        {
            var flow = Playing();
            flow.Pilot = new Autopilot();
            RunToResult(flow);
            var hash = flow.Game!.Hash();

            flow.Apply(Intent.Down(Control.Left));
            flow.StepTick();
            flow.Advance(1);

            Assert.That(flow.Game.Hash(), Is.EqualTo(hash));
        }

        [Test]
        public void RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentNullException>(() => _ = new QixFlow(null!, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new QixFlow(new QixConfig { TargetPercent = 100 }, 1));
            Assert.Throws<ArgumentException>(() => Intent.Of(IntentKind.ControlDown));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Down((Control)42));
        }
    }

    public sealed class HeldControlsTests
    {
        [Test]
        public void LastPressedDirectionWinsAndReleaseFallsBack()
        {
            var held = new HeldControls();
            Assert.That(held.Direction, Is.EqualTo(Direction.None));

            held.Press(Control.Left);
            held.Press(Control.Up);
            Assert.That(held.Direction, Is.EqualTo(Direction.Up));

            held.Release(Control.Up);
            Assert.That(held.Direction, Is.EqualTo(Direction.Left));

            held.Press(Control.Left);
            held.Release(Control.Left);
            Assert.That(held.Direction, Is.EqualTo(Direction.None), "二重の押下でも 1 回離せば消える");
        }

        [Test]
        public void DrawIsIndependentAndClearResetsAll()
        {
            var held = new HeldControls();
            held.Press(Control.Draw);
            held.Press(Control.Right);
            Assert.That(held.Draw, Is.True);

            held.Clear();
            Assert.That(held.Draw, Is.False);
            Assert.That(held.Direction, Is.EqualTo(Direction.None));
        }
    }

    public sealed class AutopilotTests
    {
        /// <summary>撮影で勝利まで撮れること。乱数の敵でも決着し、ミスなく勝つ。</summary>
        [Test]
        public void WinsWithinTickLimit([Range(0, 9)] int seed)
        {
            var game = new QixGame(new QixConfig(), seed);
            var pilot = new Autopilot();

            while (!game.IsOver && game.Ticks < 60L * 60 * 10)
            {
                var (direction, draw) = pilot.Next(game);
                game.SetInput(direction, draw);
                game.Tick();
            }

            Assert.That(game.Status, Is.EqualTo(GameStatus.Won));
            Assert.That(game.HasReachedTarget, Is.True);
        }

        [Test]
        public void ReturnsNoInputAfterGameOver()
        {
            var config = new QixConfig { TargetPercent = 1 };
            var game = new QixGame(config, 3);
            var pilot = new Autopilot();
            while (!game.IsOver)
            {
                var (direction, draw) = pilot.Next(game);
                game.SetInput(direction, draw);
                game.Tick();
            }

            Assert.That(pilot.Next(game), Is.EqualTo((Direction.None, false)));
        }
    }
}
