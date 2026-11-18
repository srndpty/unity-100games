using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class StrataFlowTests
    {
        private const int RepeatDelay = 10;
        private const int RepeatInterval = 2;
        private const int Seed = 77;
        private const int MaxDropsToLose = 200;

        private static StrataFlow NewFlow() => new(new StrataConfig(), RepeatDelay, RepeatInterval, Seed);

        private static StrataFlow Started()
        {
            var flow = NewFlow();
            flow.Submit();
            return flow;
        }

        private static ActivePiece ActiveOf(StrataFlow flow) => flow.Game!.Active!.Value;

        private static void Steps(StrataFlow flow, int count)
        {
            for (var i = 0; i < count; i++) { flow.StepTick(); }
        }

        private static void PlayUntilResult(StrataFlow flow)
        {
            for (var i = 0; i < MaxDropsToLose && flow.Screen == AppScreen.Playing; i++)
            {
                flow.Press(PlayerAction.HardDrop);
                Steps(flow, 20);
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
        }

        // 画面遷移

        [Test]
        public void FlowStartsOnTheTitleWithoutAGame()
        {
            var flow = NewFlow();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
        }

        [Test]
        public void SubmitStartsAGameWithTheGivenSeed()
        {
            var flow = Started();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.GameSeed, Is.EqualTo(Seed));
            Assert.That(flow.Game, Is.Not.Null);
        }

        [Test]
        public void SubmitDuringPlayDoesNotRestartTheGame()
        {
            var flow = Started();
            var game = flow.Game;
            flow.Submit();
            Assert.That(flow.Game, Is.SameAs(game));
        }

        [Test]
        public void PauseStopsTimeAndResumeContinues()
        {
            var flow = Started();
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));

            Steps(flow, 5);
            flow.Advance(1);
            Assert.That(flow.Game!.TickCount, Is.EqualTo(0));

            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            flow.StepTick();
            Assert.That(flow.Game!.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void SubmitResumesFromPause()
        {
            var flow = Started();
            var game = flow.Game;
            flow.TogglePause();
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game, Is.SameAs(game));
        }

        [Test]
        public void PauseIsIgnoredOutsideOfPlay()
        {
            var flow = NewFlow();
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));

            flow.Submit();
            PlayUntilResult(flow);
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
        }

        [Test]
        public void RestartFromPauseStartsANewGameWithANewSeed()
        {
            var flow = Started();
            var game = flow.Game;
            flow.TogglePause();

            flow.Restart();

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game, Is.Not.SameAs(game));
            Assert.That(flow.GameSeed, Is.EqualTo(Seed + 1));
        }

        [Test]
        public void RestartAndBackAreIgnoredDuringPlay()
        {
            var flow = Started();
            var game = flow.Game;
            flow.Restart();
            flow.BackToTitle();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game, Is.SameAs(game));
        }

        [Test]
        public void BackToTitleDiscardsTheGame()
        {
            var flow = Started();
            flow.TogglePause();
            flow.BackToTitle();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
        }

        [Test]
        public void LosingShowsTheResultAndStopsTheGame()
        {
            var flow = Started();
            PlayUntilResult(flow);
            var ticks = flow.Game!.TickCount;

            flow.Press(PlayerAction.HardDrop);
            flow.StepTick();
            flow.Advance(1);

            Assert.That(flow.Game!.TickCount, Is.EqualTo(ticks));
            Assert.That(flow.Perform(GameCommand.MoveLeft), Is.False);
        }

        [Test]
        public void ReplayFromTheResultUsesANewSeedEveryTime()
        {
            var flow = Started();
            PlayUntilResult(flow);
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.GameSeed, Is.EqualTo(Seed + 1));

            PlayUntilResult(flow);
            flow.Restart();
            Assert.That(flow.GameSeed, Is.EqualTo(Seed + 2));
        }

        [Test]
        public void BestScoreKeepsTheHighestResult()
        {
            var flow = Started();
            PlayUntilResult(flow);
            var first = flow.Game!.Score;
            Assert.That(flow.BestScore, Is.EqualTo(first));
            Assert.That(flow.IsNewBest, Is.True);

            flow.Submit();
            Assert.That(flow.IsNewBest, Is.False);
            flow.TogglePause();
            flow.BackToTitle();
            Assert.That(flow.BestScore, Is.EqualTo(first));
        }

        // 入力

        [Test]
        public void InputIsIgnoredBeforeTheGameStarts()
        {
            var flow = NewFlow();
            flow.Press(PlayerAction.Left);
            flow.Release(PlayerAction.SoftDrop);
            Assert.That(flow.Perform(GameCommand.HardDrop), Is.False);
        }

        [Test]
        public void HeldMoveRepeatsAfterTheDelayAtTheInterval()
        {
            var flow = Started();
            var startX = ActiveOf(flow).X;

            flow.Press(PlayerAction.Left);
            Assert.That(ActiveOf(flow).X, Is.EqualTo(startX - 1));

            Steps(flow, RepeatDelay - 1);
            Assert.That(ActiveOf(flow).X, Is.EqualTo(startX - 1));
            flow.StepTick();
            Assert.That(ActiveOf(flow).X, Is.EqualTo(startX - 2));

            Steps(flow, RepeatInterval - 1);
            Assert.That(ActiveOf(flow).X, Is.EqualTo(startX - 2));
            flow.StepTick();
            Assert.That(ActiveOf(flow).X, Is.EqualTo(startX - 3));
        }

        [Test]
        public void ReleasedMoveDoesNotRepeat()
        {
            var flow = Started();
            var startX = ActiveOf(flow).X;
            flow.Press(PlayerAction.Right);
            flow.Release(PlayerAction.Right);
            Steps(flow, RepeatDelay * 2);
            Assert.That(ActiveOf(flow).X, Is.EqualTo(startX + 1));
        }

        [Test]
        public void PressAndReleaseInTheSameFrameStillMovesOnce()
        {
            var flow = Started();
            var startX = ActiveOf(flow).X;
            flow.Press(PlayerAction.Left);
            flow.Release(PlayerAction.Left);
            Assert.That(ActiveOf(flow).X, Is.EqualTo(startX - 1));
        }

        [Test]
        public void SoftDropFollowsTheKey()
        {
            var flow = Started();
            var startY = ActiveOf(flow).Y;

            flow.Press(PlayerAction.SoftDrop);
            Steps(flow, 4);
            Assert.That(ActiveOf(flow).Y, Is.EqualTo(startY - 2));

            flow.Release(PlayerAction.SoftDrop);
            Steps(flow, 4);
            Assert.That(ActiveOf(flow).Y, Is.EqualTo(startY - 2));
        }

        [Test]
        public void HeldKeysAreNotCarriedAcrossAPause()
        {
            var flow = Started();
            flow.Press(PlayerAction.Left);
            flow.Press(PlayerAction.SoftDrop);
            var held = ActiveOf(flow);

            flow.TogglePause();
            flow.TogglePause();
            Steps(flow, RepeatDelay);

            Assert.That(ActiveOf(flow), Is.EqualTo(held));
        }

        [Test]
        public void HeldKeysAreNotCarriedIntoTheNextGame()
        {
            var flow = Started();
            flow.Press(PlayerAction.Left);
            flow.TogglePause();
            flow.Restart();
            var startX = ActiveOf(flow).X;

            Steps(flow, RepeatDelay);

            Assert.That(ActiveOf(flow).X, Is.EqualTo(startX));
        }

        // フォーカス

        [Test]
        public void LosingFocusPausesAndRegainingItDoesNotResume()
        {
            var flow = Started();
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
        }

        [Test]
        public void OperationsAreIgnoredWithoutFocus()
        {
            var flow = Started();
            flow.TogglePause();
            flow.SetFocus(false);

            flow.Submit();
            flow.TogglePause();
            flow.Restart();
            flow.BackToTitle();

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.GameSeed, Is.EqualTo(Seed));
        }

        [Test]
        public void PauseKeyAndFocusLossInEitherOrderStayPaused()
        {
            var keyFirst = Started();
            keyFirst.TogglePause();
            keyFirst.SetFocus(false);
            Assert.That(keyFirst.Screen, Is.EqualTo(AppScreen.Paused));

            var focusFirst = Started();
            focusFirst.SetFocus(false);
            focusFirst.TogglePause();
            Assert.That(focusFirst.Screen, Is.EqualTo(AppScreen.Paused));
        }

        [Test]
        public void HeldKeyIsForgottenWhenFocusIsLost()
        {
            var flow = Started();
            flow.Press(PlayerAction.Left);
            var held = ActiveOf(flow);

            flow.SetFocus(false);
            flow.SetFocus(true);
            flow.Submit();
            Steps(flow, RepeatDelay);

            Assert.That(ActiveOf(flow).X, Is.EqualTo(held.X));
        }

        // 時間

        [Test]
        public void OneSecondAdvancesSixtyTicks()
        {
            var flow = Started();
            for (var i = 0; i < 60; i++) { flow.Advance(1d / 60d); }
            Assert.That(flow.Game!.TickCount, Is.EqualTo(60));
        }

        [Test]
        public void SmallTimeStepsAddUpWithoutLosingATick()
        {
            var flow = Started();
            for (var i = 0; i < 10; i++) { flow.Advance(.01); }
            Assert.That(flow.Game!.TickCount, Is.EqualTo(6));
        }

        [Test]
        public void ALongFrameAdvancesAtMostTheFrameLimit()
        {
            var flow = Started();
            flow.Advance(10);
            Assert.That(flow.Game!.TickCount, Is.EqualTo(StrataFlow.MaxTicksPerFrame));
        }

        [TestCase(0d)]
        [TestCase(-1d)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void InvalidTimeIsIgnored(double seconds)
        {
            var flow = Started();
            flow.Advance(seconds);
            Assert.That(flow.Game!.TickCount, Is.EqualTo(0));
        }

        [Test]
        public void TimeBeforeAPauseIsNotAddedAfterResuming()
        {
            var flow = Started();
            flow.Advance(.9d / 60d);
            flow.TogglePause();
            flow.TogglePause();
            flow.Advance(.9d / 60d);
            Assert.That(flow.Game!.TickCount, Is.EqualTo(0));
        }

        // 出来事

        [Test]
        public void EventsAreReportedOnce()
        {
            var flow = Started();
            flow.Press(PlayerAction.Left);
            Assert.That(flow.TakeEvents() & GameEvents.Moved, Is.EqualTo(GameEvents.Moved));
            Assert.That(flow.TakeEvents(), Is.EqualTo(GameEvents.None));
        }

        [Test]
        public void ConstructorRejectsInvalidSettings()
        {
            Assert.Throws<ArgumentNullException>(() => new StrataFlow(null!, RepeatDelay, RepeatInterval, Seed));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StrataFlow(new StrataConfig(), 0, RepeatInterval, Seed));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StrataFlow(new StrataConfig(), RepeatDelay, 0, Seed));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StrataFlow(new StrataConfig { SoftDropTicks = 0 }, RepeatDelay, RepeatInterval, Seed));
        }
    }
}
