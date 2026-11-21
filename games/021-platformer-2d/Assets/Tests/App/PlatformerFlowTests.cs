using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class PlatformerFlowTests
    {
        private const int Interlude = 30;
        private const int TickLimit = 60 * 60 * 10;

        /// <summary>右へ歩けばすぐゴールに着く面。</summary>
        private static readonly string[] _short = { "...G", "S..G", "####" };

        /// <summary>右へ歩くと穴に落ちる面。</summary>
        private static readonly string[] _pit = { "G...", "S...", "#..." };

        private static readonly string[] _flat = { "......G", "S......", "#######" };

        private static Level Short() => Level.Parse("short", _short);

        private static Level Pit() => Level.Parse("pit", _pit);

        private static PlatformerFlow Create(params Level[] levels)
        {
            return new PlatformerFlow(new PlatformerConfig(), levels.Length == 0 ? new[] { Short(), Short(), Short() } : levels, Interlude);
        }

        private static PlatformerFlow Playing(params Level[] levels)
        {
            var flow = Create(levels);
            flow.Apply(Intent.Of(IntentKind.Submit));
            flow.TakeEvents();
            return flow;
        }

        private static FlowEvents StepWhile(PlatformerFlow flow, AppScreen screen)
        {
            var events = FlowEvents.None;
            for (var i = 0; i < TickLimit && flow.Screen == screen; i++)
            {
                flow.StepTick();
                events |= flow.TakeEvents();
            }

            return events;
        }

        [Test]
        public void StartsAtTitleWithoutAGame()
        {
            var flow = Create();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            flow.StepTick();
            flow.Advance(1);
            Assert.That(flow.Campaign, Is.Null);
        }

        [Test]
        public void TitleToPlayingToClearToNextLevelToWinToReplay()
        {
            var flow = Create();
            flow.Apply(Intent.Of(IntentKind.Submit));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Started | FlowEvents.LevelStarted));

            flow.Apply(Intent.Down(Control.Right));
            Assert.That(StepWhile(flow, AppScreen.Playing), Is.EqualTo(FlowEvents.Cleared));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Interlude));

            for (var i = 0; i < Interlude - 1; i++) { flow.StepTick(); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Interlude));
            flow.StepTick();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Campaign!.LevelIndex, Is.EqualTo(1));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.LevelStarted));

            StepWhile(flow, AppScreen.Playing);
            StepWhile(flow, AppScreen.Interlude);
            var events = StepWhile(flow, AppScreen.Playing);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(events, Is.EqualTo(FlowEvents.Cleared | FlowEvents.Won));
            Assert.That(flow.BestScore, Is.EqualTo(flow.Campaign.Score));
            Assert.That(flow.IsNewBest, Is.True);

            flow.Apply(Intent.Of(IntentKind.Submit));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Campaign!.LevelIndex, Is.Zero, "再戦は 1 面から");
            Assert.That(flow.Campaign.Score, Is.Zero);
            Assert.That(flow.IsNewBest, Is.False);
        }

        [Test]
        public void HeldDirectionCarriesThroughInterlude()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Right));
            StepWhile(flow, AppScreen.Playing);
            StepWhile(flow, AppScreen.Interlude);
            flow.StepTick();
            flow.StepTick();
            Assert.That(flow.Campaign!.Run.Player.VelocityX, Is.GreaterThan(0), "面の合間をはさんでも押したままのキーが効く");
        }

        [Test]
        public void ClearHappensOnceEvenIfTicksContinue()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Right));
            var events = StepWhile(flow, AppScreen.Playing);
            Assert.That(events, Is.EqualTo(FlowEvents.Cleared));
            var score = flow.Campaign!.Score;

            // 面の合間に何度 Tick しても、2 回目のクリアや面の飛ばしは起きない。
            events = StepWhile(flow, AppScreen.Interlude);
            Assert.That(events, Is.EqualTo(FlowEvents.LevelStarted));
            Assert.That(flow.Campaign.LevelIndex, Is.EqualTo(1));
            Assert.That(flow.Campaign.Score, Is.EqualTo(score));
        }

        [Test]
        public void MissRetriesThenLosesAndShowsResult()
        {
            var flow = Playing(Pit(), Short(), Short());
            flow.Apply(Intent.Down(Control.Right));
            var events = FlowEvents.None;
            for (var i = 0; i < TickLimit && flow.Screen != AppScreen.Result; i++)
            {
                flow.StepTick();
                events |= flow.TakeEvents();
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(events, Is.EqualTo(FlowEvents.Missed | FlowEvents.LevelStarted | FlowEvents.Lost));
            Assert.That(flow.Campaign!.Lives, Is.Zero);
            Assert.That(flow.Campaign.LevelIndex, Is.Zero);
        }

        [Test]
        public void PauseStopsTicksAndResumes()
        {
            var flow = Playing();
            flow.Apply(Intent.Of(IntentKind.TogglePause));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Advance(1);
            flow.StepTick();
            Assert.That(flow.Campaign!.Run.Ticks, Is.Zero);

            flow.Apply(Intent.Of(IntentKind.Submit));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            flow.StepTick();
            Assert.That(flow.Campaign.Run.Ticks, Is.EqualTo(1));
        }

        [Test]
        public void PauseClearsHeldKeys()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Right));
            flow.Apply(Intent.Of(IntentKind.TogglePause));
            flow.Apply(Intent.Of(IntentKind.TogglePause));
            for (var i = 0; i < 10; i++) { flow.StepTick(); }
            Assert.That(flow.Campaign!.Run.Player.VelocityX, Is.Zero, "押し直すまで動かない");
        }

        [Test]
        public void RestartAndBackToTitleOnlyFromPauseOrResult()
        {
            var flow = Playing();
            flow.Apply(Intent.Of(IntentKind.Restart));
            flow.Apply(Intent.Of(IntentKind.BackToTitle));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "プレイ中は無視");

            flow.StepTick();
            flow.Apply(Intent.Of(IntentKind.TogglePause));
            flow.Apply(Intent.Of(IntentKind.Restart));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Campaign!.Run.Ticks, Is.Zero, "1 面の最初から");

            flow.Apply(Intent.Of(IntentKind.TogglePause));
            flow.Apply(Intent.Of(IntentKind.BackToTitle));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Campaign, Is.Null);
        }

        [Test]
        public void ResultIgnoresControlsAndPause()
        {
            var flow = Playing(Pit());
            flow.Apply(Intent.Down(Control.Right));
            for (var i = 0; i < TickLimit && flow.Screen != AppScreen.Result; i++) { flow.StepTick(); }
            var campaign = flow.Campaign!;
            var ticks = campaign.Run.Ticks;
            flow.Apply(Intent.Of(IntentKind.TogglePause));
            flow.Apply(Intent.Down(Control.Jump));
            flow.StepTick();
            flow.Advance(1);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(campaign.Run.Ticks, Is.EqualTo(ticks));
        }

        [Test]
        public void FocusLossPausesAndDropsInputUntilRestored()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Right));
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Of(IntentKind.Submit));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間は操作を受け付けない");

            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
            flow.Apply(Intent.Of(IntentKind.Submit));
            for (var i = 0; i < 10; i++) { flow.StepTick(); }
            Assert.That(flow.Campaign!.Run.Player.VelocityX, Is.Zero, "失う前に押していたキーは消える");
        }

        [Test]
        public void FocusLossDuringInterludeStopsTime()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Right));
            StepWhile(flow, AppScreen.Playing);
            flow.SetFocus(false);
            flow.Advance(10);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Interlude));
            flow.SetFocus(true);
            for (var i = 0; i < Interlude - 1; i++) { flow.Advance(1.0 / 60); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Interlude), "フォーカスがない間の時間は数えない");
            flow.Advance(1.0 / 60);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void ShortJumpPressBetweenTicksStillJumps()
        {
            var flow = Playing(Level.Parse("flat", _flat));
            flow.StepTick();
            flow.TakeEvents();
            flow.Apply(Intent.Down(Control.Jump));
            flow.Apply(Intent.Up(Control.Jump));
            flow.StepTick();
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Jumped));
        }

        [Test]
        public void Advance_ConvertsRealTimeToTicksWithLimit()
        {
            var flow = Playing();
            for (var i = 0; i < 10; i++) { flow.Advance(0.1 / 6); }
            Assert.That(flow.Campaign!.Run.Ticks, Is.EqualTo(10), "1/60 秒ずつ 10 回で 10 Tick");

            flow.Advance(100);
            Assert.That(flow.Campaign.Run.Ticks, Is.EqualTo(10 + PlatformerFlow.MaxTicksPerFrame));

            flow.Advance(double.NaN);
            flow.Advance(-1);
            flow.Advance(double.PositiveInfinity);
            Assert.That(flow.Campaign.Run.Ticks, Is.EqualTo(10 + PlatformerFlow.MaxTicksPerFrame), "非有限・負の時間は無視");
        }

        [Test]
        public void Pilot_PlaysAllRealLevelsToWin()
        {
            var flow = new PlatformerFlow(new PlatformerConfig(), Levels.All(), Interlude) { Pilot = new Autopilot() };
            flow.Apply(Intent.Of(IntentKind.Submit));
            var events = FlowEvents.None;
            for (var i = 0; i < TickLimit && flow.Screen != AppScreen.Result; i++)
            {
                flow.StepTick();
                events |= flow.TakeEvents();
            }

            Assert.That(flow.Campaign!.Status, Is.EqualTo(CampaignStatus.Won));
            Assert.That(flow.Campaign.Lives, Is.EqualTo(new PlatformerConfig().Lives), "ミスなし");
            Assert.That(events & FlowEvents.Stomped, Is.EqualTo(FlowEvents.Stomped));
        }

        [Test]
        public void Constructor_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PlatformerFlow(new PlatformerConfig(), Levels.All(), 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PlatformerFlow(new PlatformerConfig(), Levels.All(), PlatformerFlow.MaxInterludeTicks + 1));
            Assert.Throws<ArgumentException>(() => _ = new PlatformerFlow(new PlatformerConfig(), Array.Empty<Level>(), Interlude));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PlatformerFlow(new PlatformerConfig { Lives = 0 }, Levels.All(), Interlude));
        }

        [Test]
        public void Intent_RejectsInvalidKinds()
        {
            Assert.Throws<ArgumentException>(() => Intent.Of(IntentKind.ControlDown));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Of((IntentKind)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Down((Control)99));
        }
    }
}
