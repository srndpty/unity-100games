using System;
using Game.Core;
using IntGeometry;
using NUnit.Framework;
using TileMotion;

namespace Game.App.Tests
{
    public sealed class WardFlowTests
    {
        private const double Start = 1.0;
        private const double Tick = 1.0 / Units.TickRate;

        /// <summary>何もいない部屋。</summary>
        private static readonly string[] _empty = { "##########", "#........#", "#E......O#", "##########" };

        /// <summary>入口の目の前に、こちらを向いた見張りがいる部屋（立っていれば捕まる）。</summary>
        private static readonly string[] _guarded = { "##########", "#........#", "#E......O#", "##########" };

        /// <summary>入口の 2 マス右に目的物がある部屋。</summary>
        private static readonly string[] _short = { "######", "#E.O.#", "######" };

        private static Facility Empty() => new(FacilityMap.Parse(_empty));

        private static Facility Guarded() => new(FacilityMap.Parse(_guarded), new GuardPlan("S", Trig.Left, new Waypoint(new Cell(4, 1), 600, Trig.Left)));

        private static Facility Short() => new(FacilityMap.Parse(_short));

        private static WardFlow Create(Func<Facility>? facility = null, int seed = 1) => new(new WardConfig(), seed, facility ?? Empty);

        private static WardFlow Playing(Func<Facility>? facility = null)
        {
            var flow = Create(facility);
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            flow.TakeEvents();
            return flow;
        }

        /// <summary>時刻 <paramref name="from"/> から <paramref name="ticks"/> Tick ぶん、1 Tick ずつ時計を進める（Tick の区間の途中で止める）。</summary>
        private static FlowEvents Walk(WardFlow flow, double from, int ticks)
        {
            var events = FlowEvents.None;
            for (var i = 1; i <= ticks; i++)
            {
                flow.Advance(from + ((i + 0.5) * Tick));
                events |= flow.TakeEvents();
            }

            return events;
        }

        [Test]
        public void StartsAtTitle_TimeDoesNothing()
        {
            var flow = Create();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            flow.Advance(100);
            flow.StepTick();
            Assert.That(flow.Match, Is.Null);
        }

        [Test]
        public void Title_Play_Caught_RetryWithANewSeed()
        {
            var flow = Create(Guarded);
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Started));
            var firstSeed = flow.Match!.Seed;
            var events = Walk(flow, Start, 600);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Caught));
            Assert.That(events & (FlowEvents.Suspicious | FlowEvents.Discovered | FlowEvents.Caught), Is.EqualTo(FlowEvents.Suspicious | FlowEvents.Discovered | FlowEvents.Caught));
            var hash = flow.Match.Hash();
            flow.Advance(Start + 100);
            flow.StepTick();
            flow.Apply(Intent.Down(Control.Right, Start + 101));
            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + 101));
            Assert.That(flow.Match.Hash(), Is.EqualTo(hash), "結果の画面では試合が進まない");
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Caught), "結果の画面で一時停止はしない");

            flow.Apply(Intent.Of(IntentKind.Submit, Start + 102));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Match.Ticks, Is.Zero, "新しい試合");
            Assert.That(flow.Match.Seed, Is.EqualTo(firstSeed + 1), "再挑戦は新しいシード");
        }

        [Test]
        public void Escape_EndsTheMatch_AndSubmitStartsANewOne()
        {
            var flow = Playing(Short);
            flow.Apply(Intent.Down(Control.Right, Start + (0.5 * Tick)));
            Walk(flow, Start, 30);
            Assert.That(flow.Match!.HasObjective, Is.True);
            flow.Apply(Intent.Up(Control.Right, Start + (30.6 * Tick)));
            flow.Apply(Intent.Down(Control.Left, Start + (30.7 * Tick)));
            var events = Walk(flow, Start + (30 * Tick), 60);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Escaped));
            Assert.That(events & (FlowEvents.ObjectiveTaken | FlowEvents.Escaped), Is.EqualTo(FlowEvents.Escaped));
            var played = flow.PlayedTicks;
            Assert.That(played, Is.EqualTo(flow.Match.Ticks));
            flow.StepTick();
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None), "2 回目の脱出は起きない");
            Assert.That(flow.PlayedTicks, Is.EqualTo(played));

            flow.Apply(Intent.Of(IntentKind.Submit, Start + 10));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Match.HasObjective, Is.False);
        }

        [Test]
        public void Ticks_FollowTheClock_FromTheStartTime()
        {
            var flow = Playing();
            flow.Advance(Start + (0.5 * Tick));
            Assert.That(flow.Match!.Ticks, Is.Zero, "1 Tick ぶんの時間が経つまで進まない");
            flow.Advance(Start + (3.5 * Tick));
            Assert.That(flow.Match.Ticks, Is.EqualTo(3));
            flow.Advance(Start + (2 * Tick));
            Assert.That(flow.Match.Ticks, Is.EqualTo(3), "時刻が戻っても進まない");
        }

        [Test]
        public void Hitch_SharesTheBudget_AndCountsPlayedTime()
        {
            var flow = Playing();
            var x = flow.Match!.Intruder.Body.X;
            flow.Apply(Intent.Down(Control.Right, 2.0));
            flow.Apply(Intent.Up(Control.Right, 3.0));
            flow.Advance(Start + 10);
            Assert.That(flow.Match.Ticks, Is.EqualTo(WardFlow.MaxTicksPerFrame), "入力と時計で合わせて上限まで");
            Assert.That(flow.PlayedTicks, Is.EqualTo(600), "遊んだ時間は実際に経った 10 秒");
            var after = flow.Match.Intruder.Body.X;
            Walk(flow, Start + 10, 30);
            Assert.That(flow.Match.Intruder.Body.X, Is.EqualTo(after), "飛ばした区間で離したキーは離したまま");
            Assert.That(after, Is.EqualTo(x), "押していた区間はすべて飛ばした");
        }

        [Test]
        public void Input_AppliesFromTheTickContainingItsTimestamp()
        {
            var flow = Playing();
            var x = flow.Match!.Intruder.Body.X;
            flow.Apply(Intent.Down(Control.Right, Start + (2.5 * Tick)));
            Assert.That(flow.Match.Ticks, Is.EqualTo(2), "押下より前の 2 Tick を先に進めた");
            Assert.That(flow.Match.Intruder.Body.X, Is.EqualTo(x));
            flow.Advance(Start + (3.5 * Tick));
            Assert.That(flow.Match.Intruder.Body.X, Is.EqualTo(x + new WardConfig().WalkSpeed), "3 Tick 目から右へ歩く");
        }

        [Test]
        public void RunAndSneakKeysChangeTheGait()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Right, Start + (0.5 * Tick)));
            flow.Apply(Intent.Down(Control.Run, Start + (0.5 * Tick)));
            Walk(flow, Start, 1);
            Assert.That(flow.Match!.Intruder.Gait, Is.EqualTo(Gait.Run));
            flow.Apply(Intent.Down(Control.Sneak, Start + (1.6 * Tick)));
            Walk(flow, Start + Tick, 1);
            Assert.That(flow.Match.Intruder.Gait, Is.EqualTo(Gait.Sneak), "両方押したら忍び足");
            flow.Apply(Intent.Up(Control.Sneak, Start + (2.6 * Tick)));
            flow.Apply(Intent.Up(Control.Run, Start + (2.6 * Tick)));
            Walk(flow, Start + (2 * Tick), 1);
            Assert.That(flow.Match.Intruder.Gait, Is.EqualTo(Gait.Walk));
        }

        [Test]
        public void Pause_StopsTime_AndKeysAreNotCarried()
        {
            var flow = Playing();
            Walk(flow, Start, 10);
            flow.Apply(Intent.Down(Control.Right, Start + (10.6 * Tick)));
            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + (10.7 * Tick)));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Advance(Start + 50);
            flow.StepTick();
            Assert.That(flow.Match!.Ticks, Is.EqualTo(10));
            Assert.That(flow.PlayedTicks, Is.EqualTo(10), "一時停止の間は数えない");
            var x = flow.Match.Intruder.Body.X;
            flow.Apply(Intent.Down(Control.Left, Start + 50));
            flow.Apply(Intent.Of(IntentKind.Submit, Start + 50));
            Walk(flow, Start + 50, 30);
            Assert.That(flow.Match.Ticks, Is.EqualTo(40), "停止していた時間を取り戻さない");
            Assert.That(flow.Match.Intruder.Body.X, Is.EqualTo(x), "停止の前後に押したキーは押し直すまで効かない");
        }

        [Test]
        public void RestartAndBackToTitle_OnlyFromPauseOrResults()
        {
            var flow = Playing();
            Walk(flow, Start, 20);
            flow.Apply(Intent.Of(IntentKind.Restart, Start + (20.5 * Tick)));
            flow.Apply(Intent.Of(IntentKind.BackToTitle, Start + (20.5 * Tick)));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "プレイ中は無視する");
            Assert.That(flow.Match!.Ticks, Is.EqualTo(20));

            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + 1));
            flow.Apply(Intent.Of(IntentKind.Restart, Start + 1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Match.Ticks, Is.Zero, "最初から");

            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + 2));
            flow.Apply(Intent.Of(IntentKind.BackToTitle, Start + 2));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Match, Is.Null);
            flow.Apply(Intent.Of(IntentKind.Quit, Start + 3));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title), "終了は Unity 層が行う");
        }

        [Test]
        public void DebugDisplayTogglesOnAnyScreenAndSurvivesMatches()
        {
            var flow = Create();
            Assert.That(flow.ShowDebug, Is.False);
            flow.Apply(Intent.Of(IntentKind.ToggleDebug, Start));
            Assert.That(flow.ShowDebug, Is.True);
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.DebugToggled));
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            Assert.That(flow.ShowDebug, Is.True, "試合を始めても保つ");
            flow.Apply(Intent.Of(IntentKind.ToggleDebug, Start + 1));
            Assert.That(flow.ShowDebug, Is.False);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "画面は変わらない");
        }

        [Test]
        public void FocusLoss_PausesAndIgnoresInputUntilRefocusedAndResumed([Values(true, false)] bool keyBeforeFocusLoss)
        {
            var flow = Playing();
            var x = flow.Match!.Intruder.Body.X;
            if (keyBeforeFocusLoss) { flow.Apply(Intent.Down(Control.Right, Start)); }
            flow.SetFocus(false);
            if (!keyBeforeFocusLoss) { flow.Apply(Intent.Down(Control.Right, Start)); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Of(IntentKind.Submit, Start + 1));
            flow.Advance(Start + 2);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間の操作は受け付けない");

            flow.SetFocus(true);
            flow.Advance(Start + 3);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
            flow.Apply(Intent.Of(IntentKind.Submit, Start + 3));
            Walk(flow, Start + 3, 30);
            Assert.That(flow.Match.Ticks, Is.EqualTo(30));
            Assert.That(flow.Match.Intruder.Body.X, Is.EqualTo(x), "フォーカスを失う前後のキーは持ち越さない");
        }

        [Test]
        public void RejectsUndefinedIntents()
        {
            Assert.That(() => Intent.Down((Control)99, Start), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Intent.Of((IntentKind)99, Start), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Intent.Of(IntentKind.ControlDown, Start), Throws.ArgumentException);
            Assert.That(() => Intent.Of(IntentKind.Submit, double.NaN), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new WardFlow(new WardConfig { RunSpeed = 0 }, 1), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Pilot_PlaysTheWholeGameThroughTheFlow()
        {
            var flow = new WardFlow(new WardConfig(), 1) { Pilot = new StealthPilot() };
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            var events = FlowEvents.None;
            for (var i = 0; i < 20000 && flow.Screen == AppScreen.Playing; i++)
            {
                flow.StepTick();
                events |= flow.TakeEvents();
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Escaped));
            Assert.That(flow.PlayedTicks, Is.EqualTo(flow.Match!.Ticks), "StepTick で直接進めた Tick は 1 Tick ぶんの時間として数える");
            Assert.That(events & (FlowEvents.ObjectiveTaken | FlowEvents.Escaped | FlowEvents.Step), Is.EqualTo(FlowEvents.ObjectiveTaken | FlowEvents.Escaped | FlowEvents.Step));
            Assert.That(events & (FlowEvents.Suspicious | FlowEvents.Discovered), Is.EqualTo(FlowEvents.None));
        }
    }
}
