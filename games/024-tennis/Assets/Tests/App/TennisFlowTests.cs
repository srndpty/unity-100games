using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class TennisFlowTests
    {
        private const double Start = 1.0;
        private const double Tick = 1.0 / Units.TickRate;

        private static TennisFlow Create(int seed = 1) => new(new TennisConfig(), seed);

        private static TennisFlow Playing(int seed = 1)
        {
            var flow = Create(seed);
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            flow.TakeEvents();
            return flow;
        }

        /// <summary>時刻 <paramref name="from"/> から <paramref name="ticks"/> Tick ぶん、1 Tick ずつ時計を進める（Tick の区間の途中で止める）。</summary>
        private static FlowEvents Walk(TennisFlow flow, double from, int ticks)
        {
            var events = FlowEvents.None;
            for (var i = 1; i <= ticks; i++)
            {
                flow.Advance(from + ((i + 0.5) * Tick));
                events |= flow.TakeEvents();
            }

            return events;
        }

        private static void PlayToResult(TennisFlow flow, int limit = 400000)
        {
            for (var i = 0; i < limit && flow.Screen == AppScreen.Playing; i++)
            {
                flow.StepTick();
                flow.EndFrame();
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
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
        public void Title_Play_Result_Rematch_WithANewSeed()
        {
            var flow = Create(seed: 7);
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Started));
            Assert.That(flow.Match!.Seed, Is.EqualTo(7));
            flow.Pilot = new Autopilot();
            PlayToResult(flow);
            Assert.That(flow.TakeEvents() & FlowEvents.MatchWon, Is.EqualTo(FlowEvents.MatchWon));
            Assert.That(flow.Match.IsFinished, Is.True);

            var hash = flow.Match.Hash();
            flow.StepTick();
            flow.Advance(Start + 10000);
            flow.Apply(Intent.Down(Control.Stroke, Start + 10001));
            Assert.That(flow.Match.Hash(), Is.EqualTo(hash), "結果の画面では試合が進まない");

            flow.Apply(Intent.Of(IntentKind.Submit, Start + 10002));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Match.Seed, Is.EqualTo(8), "再戦は新しいシード");
            Assert.That(flow.Match.Tick, Is.Zero);
        }

        [Test]
        public void ResultCanGoBackToTitleOrRestart()
        {
            var flow = Playing();
            flow.Pilot = new Autopilot();
            PlayToResult(flow);
            flow.Apply(Intent.Of(IntentKind.Restart, Start + 5000));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Match!.Tick, Is.Zero);
            flow.Apply(Intent.Of(IntentKind.BackToTitle, Start + 5001));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "プレイ中は一時停止してからでないとタイトルへ戻れない");
            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + 5002));
            flow.Apply(Intent.Of(IntentKind.BackToTitle, Start + 5003));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Match, Is.Null);
        }

        [Test]
        public void Ticks_FollowTheClock_FromTheStartTime()
        {
            var flow = Playing();
            flow.Advance(Start + (0.5 * Tick));
            Assert.That(flow.Match!.Tick, Is.Zero, "1 Tick ぶんの時間が経つまで進まない");
            flow.Advance(Start + (3.5 * Tick));
            Assert.That(flow.Match.Tick, Is.EqualTo(3));
            flow.Advance(Start + (2 * Tick));
            Assert.That(flow.Match.Tick, Is.EqualTo(3), "時刻が戻っても進まない");
        }

        [Test]
        public void Input_AppliesFromTheTickContainingItsTimestamp()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Stroke, Start + (2.5 * Tick)));
            Assert.That(flow.Match!.Tick, Is.EqualTo(2), "押下より前の 2 Tick を先に進めた");
            Assert.That(flow.Match.Rally.Phase, Is.EqualTo(RallyPhase.Ready));
            flow.Advance(Start + (3.5 * Tick));
            Assert.That(flow.Match.Rally.Phase, Is.EqualTo(RallyPhase.Toss), "3 Tick 目にトスを上げる");
            Assert.That(flow.TakeEvents() & FlowEvents.Toss, Is.EqualTo(FlowEvents.Toss));
        }

        [Test]
        public void ShortTapStillPresses()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Stroke, Start + (2.2 * Tick)));
            flow.Apply(Intent.Up(Control.Stroke, Start + (2.4 * Tick)));
            flow.Advance(Start + (3.5 * Tick));
            Assert.That(flow.Match!.Rally.Phase, Is.EqualTo(RallyPhase.Toss), "同じ Tick の中で押して離してもトスを上げる");
        }

        [Test]
        public void Hitch_SharesTheBudget_AndCountsPlayedTime()
        {
            var flow = Playing();
            flow.Apply(Intent.Down(Control.Left, 2.0));
            flow.Apply(Intent.Up(Control.Left, 3.0));
            flow.Advance(Start + 10);
            Assert.That(flow.Match!.Tick, Is.EqualTo(TennisFlow.MaxTicksPerFrame), "入力と時計で合わせて上限まで");
            Assert.That(flow.PlayedTicks, Is.EqualTo(600), "遊んだ時間は実際に経った 10 秒");
        }

        [Test]
        public void Pause_StopsTime_AndKeysAreNotCarried()
        {
            var flow = Playing();
            Walk(flow, Start, 10);
            flow.Apply(Intent.Down(Control.Left, Start + (10.6 * Tick)));
            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + (10.7 * Tick)));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            var tick = flow.Match!.Tick;
            flow.Advance(Start + 50);
            Assert.That(flow.Match.Tick, Is.EqualTo(tick), "一時停止中は進まない");

            flow.Apply(Intent.Of(IntentKind.Submit, Start + 60));
            Walk(flow, Start + 60, 20);
            Assert.That(flow.Match.Near.Position, Is.EqualTo(Positions.Serve(Side.Near, ServeCourt.Deuce, flow.Config)), "サーバーは動かない（押したままの左は持ち越さない）");
            Assert.That(flow.Match.Tick, Is.EqualTo(tick + 20));
        }

        [Test]
        public void HeldKeyIsNotCarriedAcrossPause()
        {
            // サーブを返す側：手前の選手がレシーバーになる 2 ポイント目から始める代わりに、場面を作る。
            var flow = Playing();
            var match = flow.Match!;
            var from = new Vec3(0, 100000, 10000);
            match.StageRally(Side.Far, from, ShotSolver.Solve(from, 0, -90000, 2500, 30000, flow.Config.Gravity), new Vec3(0, -110000, 0), from.WithZ(0));
            flow.Apply(Intent.Down(Control.Left, Start + (0.5 * Tick)));
            Walk(flow, Start, 5);
            var x = match.Near.Position.X;
            Assert.That(x, Is.LessThan(0));
            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + (5.7 * Tick)));
            flow.Apply(Intent.Of(IntentKind.TogglePause, Start + 10));
            Walk(flow, Start + 10, 5);
            Assert.That(match.Near.Position.X, Is.EqualTo(x), "再開後は押し直すまで動かない");
        }

        [Test]
        public void FocusLossPauses_AndInputWhileUnfocusedIsIgnored()
        {
            var flow = Playing();
            Walk(flow, Start, 3);
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Of(IntentKind.Submit, Start + 1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間の入力は捨てる");
            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
            flow.Apply(Intent.Of(IntentKind.Submit, Start + 2));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void SameKeysGiveTheSameMatchAtAnyFrameRate()
        {
            var expected = PlayScript(60, irregular: false);
            foreach (var fps in new[] { 30, 144, 240 })
            {
                Assert.That(PlayScript(fps, irregular: false), Is.EqualTo(expected), $"{fps}fps");
            }

            Assert.That(PlayScript(60, irregular: true), Is.EqualTo(expected), "不規則な間隔");
        }

        [Test]
        public void AutopilotForgetsTheAbandonedMatch()
        {
            // 途中でやめた試合の後に始めた試合は、その試合のシードから単独で始めた試合と同じ結果になる。
            var pilot = new Autopilot();
            var a = Create(seed: 10);
            a.Pilot = pilot;
            a.Apply(Intent.Of(IntentKind.Submit, Start));
            for (var i = 0; i < 700; i++) { a.StepTick(); }
            a.Apply(Intent.Of(IntentKind.TogglePause, Start));
            a.Apply(Intent.Of(IntentKind.Restart, Start));
            PlayToResult(a);

            var b = Create(seed: 11);
            b.Pilot = new Autopilot();
            b.Apply(Intent.Of(IntentKind.Submit, Start));
            PlayToResult(b);
            Assert.That(a.Match!.Hash(), Is.EqualTo(b.Match!.Hash()));
        }

        [Test]
        public void UndefinedOrNonFiniteIntentsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Of((IntentKind)99, 0));
            Assert.Throws<ArgumentException>(() => Intent.Of(IntentKind.ControlDown, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Down((Control)99, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Of(IntentKind.Submit, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Up(Control.Lob, double.PositiveInfinity));
        }

        [Test]
        public void DuplicateSubmitDoesNotStartTwoMatches()
        {
            var flow = Create(seed: 3);
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            flow.Apply(Intent.Of(IntentKind.Submit, Start));
            Assert.That(flow.Match!.Seed, Is.EqualTo(3), "プレイ中の決定は何もしない");
        }

        /// <summary>
        /// 同じキー操作（時刻つき）を、指定のフレームレートで届けたときの 4 秒後の状態のハッシュ。
        /// 操作：トスを上げ、頂点で打ち、右へ動いてから左へ。
        /// </summary>
        private static uint PlayScript(int fps, bool irregular)
        {
            var flow = Playing(seed: 4);
            var apex = flow.Config.TossApexTick;
            var script = new List<Intent>
            {
                Intent.Down(Control.Stroke, Start + (3.3 * Tick)),
                Intent.Up(Control.Stroke, Start + (5.1 * Tick)),
                Intent.Down(Control.Stroke, Start + ((5 + apex - 0.4) * Tick)),
                Intent.Up(Control.Stroke, Start + ((5 + apex + 2.2) * Tick)),
                Intent.Down(Control.Right, Start + (50.7 * Tick)),
                Intent.Up(Control.Right, Start + (80.2 * Tick)),
                Intent.Down(Control.Left, Start + (90.9 * Tick)),
                Intent.Down(Control.Up, Start + (91.3 * Tick)),
                Intent.Up(Control.Left, Start + (120.5 * Tick)),
                Intent.Up(Control.Up, Start + (150.5 * Tick)),
            };
            var random = new SeededRandom(fps);
            var now = Start;
            var next = 0;
            while (now < Start + 4)
            {
                var step = 1.0 / fps;
                if (irregular) { step *= 0.3 + (random.Next(100) / 40.0); }
                now = Math.Min(now + step, Start + 4);
                while (next < script.Count && script[next].Time <= now) { flow.Apply(script[next++]); }
                flow.Advance(now);
            }

            Assert.That(flow.Match!.Tick, Is.EqualTo(240));
            return flow.Match.Hash();
        }
    }
}
