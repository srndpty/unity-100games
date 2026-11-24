using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class GolfFlowTests
    {
        private const double Tick = 1.0 / GolfFlow.TickRate;

        private static GolfFlow Started(double time = 0)
        {
            var flow = AppKit.Flow();
            flow.Apply(new Intent(IntentKind.Swing, time));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            return flow;
        }

        /// <summary>時刻 <paramref name="time"/> まで、<paramref name="frame"/> 秒ごとのフレームで進める。</summary>
        private static double RunFrames(GolfFlow flow, double from, double time, double frame)
        {
            var now = from;
            while (now < time)
            {
                now = Math.Min(now + frame, time);
                flow.Advance(now);
            }

            return now;
        }

        [Test]
        public void TitleStartsARoundAndResultLeadsToAFreshOne()
        {
            var flow = AppKit.Flow();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Round, Is.Null);
            flow.Apply(new Intent(IntentKind.AimLeftDown, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title), "タイトルでは開始の操作だけ");
            flow.Apply(new Intent(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.TakeEvents() & FlowEvents.Started, Is.EqualTo(FlowEvents.Started));

            flow.Pilot = new Autopilot();
            flow.Pilot.Reset(flow.Round!.Seed);
            var ticks = 0;
            while (flow.Screen == AppScreen.Playing && ticks++ < 100_000) { flow.StepTick(); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.TakeEvents() & FlowEvents.Result, Is.EqualTo(FlowEvents.Result));
            var first = flow.Round!;
            Assert.That(flow.BestTotal, Is.EqualTo(first.Total));
            Assert.That(flow.IsNewBest, Is.True);

            // 結果の後の操作（向き・クラブ・打点）は何も変えない。
            var hash = first.Hash();
            foreach (var kind in new[] { IntentKind.AimLeftDown, IntentKind.ClubUp, IntentKind.ImpactUp, IntentKind.Retry, IntentKind.Back })
            {
                flow.Apply(new Intent(kind, 100));
                Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            }

            flow.StepTick();
            Assert.That(first.Hash(), Is.EqualTo(hash));

            flow.Apply(new Intent(IntentKind.Swing, 101));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Round, Is.Not.SameAs(first));
            Assert.That(flow.Round!.Seed, Is.Not.EqualTo(first.Seed), "再戦は新しいシード");

            flow.Apply(new Intent(IntentKind.Cancel, 102));
            flow.Apply(new Intent(IntentKind.Back, 102));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Round, Is.Null);
        }

        [Test]
        public void TicksFollowTheClockFromTheStart()
        {
            var flow = Started(10);
            flow.Apply(new Intent(IntentKind.Swing, 10));
            RunFrames(flow, 10, 10 + (30.5 * Tick), Tick);
            Assert.That(flow.Round!.Meter!.T, Is.EqualTo(30));
        }

        [Test]
        public void OneFrameAdvancesAtMostTheFrameBudget()
        {
            var flow = Started();
            flow.Apply(new Intent(IntentKind.Swing, 0));
            flow.Advance(100);
            Assert.That(flow.Round!.Meter!.T, Is.EqualTo(GolfFlow.MaxTicksPerFrame));
            flow.Advance(100);
            Assert.That(flow.Round!.Meter!.T, Is.EqualTo(GolfFlow.MaxTicksPerFrame), "同じ時刻なら進まない");
        }

        [Test]
        public void PauseCancelsTheSwingAndStopsTheClock()
        {
            var flow = Started();
            flow.Apply(new Intent(IntentKind.Swing, 0));
            flow.Advance(20 * Tick);
            flow.Apply(new Intent(IntentKind.Swing, 20 * Tick));
            Assert.That(flow.Round!.Meter!.PowerSet, Is.True);
            flow.Apply(new Intent(IntentKind.Cancel, 21 * Tick));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.Round.Phase, Is.EqualTo(RoundPhase.Aiming), "一時停止で振りを取り消す");
            Assert.That(flow.Round.Strokes, Is.EqualTo(0));

            var ticks = flow.Round.Ticks;
            flow.Advance(10);
            Assert.That(flow.Round.Ticks, Is.EqualTo(ticks));
            flow.Apply(new Intent(IntentKind.Swing, 10));
            Assert.That(flow.Round.Phase, Is.EqualTo(RoundPhase.Aiming), "一時停止中のスイングは効かない");

            flow.Apply(new Intent(IntentKind.Cancel, 10));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            flow.Advance(10 + (3 * Tick));
            Assert.That(flow.Round.Ticks, Is.EqualTo(ticks + 3), "再開した時刻から数え直す");
        }

        [Test]
        public void RetryFromPauseStartsANewRound()
        {
            var flow = Started();
            var first = flow.Round;
            flow.Apply(new Intent(IntentKind.Cancel, 1));
            flow.Apply(new Intent(IntentKind.Retry, 1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Round, Is.Not.SameAs(first));
        }

        [Test]
        public void FocusLossPausesCancelsTheSwingAndIgnoresInputUntilBack()
        {
            var flow = Started();
            flow.Apply(new Intent(IntentKind.Swing, 0));
            flow.Apply(new Intent(IntentKind.AimLeftDown, 0));
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.Round!.Phase, Is.EqualTo(RoundPhase.Aiming));
            Assert.That(flow.AimHeld, Is.EqualTo(0), "押していたキーを離したとみなす");

            flow.Apply(new Intent(IntentKind.Cancel, 1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間の入力は捨てる");
            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
            flow.Apply(new Intent(IntentKind.Cancel, 2));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void InputArrivingAfterFocusLossDoesNotStartASwing()
        {
            // フォーカス喪失 → スイングの順でも、スイング → フォーカス喪失の順でも、振っていない状態で止まる。
            var lostFirst = Started();
            lostFirst.SetFocus(false);
            lostFirst.Apply(new Intent(IntentKind.Swing, 0.5));
            Assert.That(lostFirst.Round!.Phase, Is.EqualTo(RoundPhase.Aiming));

            var swingFirst = Started();
            swingFirst.Apply(new Intent(IntentKind.Swing, 0.5));
            swingFirst.SetFocus(false);
            foreach (var round in new[] { lostFirst.Round, swingFirst.Round! })
            {
                Assert.That(round.Phase, Is.EqualTo(RoundPhase.Aiming));
                Assert.That(round.Meter, Is.Null);
                Assert.That(round.Strokes, Is.EqualTo(0));
            }
        }

        [Test]
        public void HoldingAnAimKeyRepeatsAfterTheDelay()
        {
            var flow = Started();
            var round = flow.Round!;
            var step = round.Config.AimStep;
            var start = round.Aim;
            flow.Apply(new Intent(IntentKind.AimLeftDown, 0));
            Assert.That(round.Aim, Is.EqualTo(IntGeometry.Trig.Normalize(start + step)), "押した瞬間に 1 回");
            var now = RunFrames(flow, 0, (GolfFlow.RepeatDelayTicks - 1 + 0.5) * Tick, Tick);
            Assert.That(round.Aim, Is.EqualTo(IntGeometry.Trig.Normalize(start + step)));
            RunFrames(flow, now, (GolfFlow.RepeatDelayTicks + (2 * GolfFlow.RepeatIntervalTicks) + 0.5) * Tick, Tick);
            Assert.That(round.Aim, Is.EqualTo(IntGeometry.Trig.Normalize(start + (4 * step))));
            flow.Apply(new Intent(IntentKind.AimLeftUp, (GolfFlow.RepeatDelayTicks + (2 * GolfFlow.RepeatIntervalTicks) + 0.5) * Tick));
            RunFrames(flow, (GolfFlow.RepeatDelayTicks + (2 * GolfFlow.RepeatIntervalTicks) + 0.5) * Tick, 2, Tick);
            Assert.That(round.Aim, Is.EqualTo(IntGeometry.Trig.Normalize(start + (4 * step))), "離したら止まる");

            flow.Apply(new Intent(IntentKind.AimRightDown, 2));
            flow.Apply(new Intent(IntentKind.AimRightUp, 2));
            Assert.That(round.Aim, Is.EqualTo(IntGeometry.Trig.Normalize(start + (3 * step))));
        }

        [Test]
        public void ClubAndImpactKeysChangeTheSetup()
        {
            var flow = Started();
            var round = flow.Round!;
            var club = round.Club;
            flow.Apply(new Intent(IntentKind.ClubDown, 0));
            Assert.That(round.Club, Is.EqualTo(club + 1));
            flow.Apply(new Intent(IntentKind.ClubUp, 0));
            Assert.That(round.Club, Is.EqualTo(club));
            for (var i = 0; i < 4; i++)
            {
                flow.Apply(new Intent(IntentKind.ImpactUp, 0));
                flow.Apply(new Intent(IntentKind.ImpactLeft, 0));
            }

            Assert.That(round.ImpactY, Is.EqualTo(GolfConfig.MaxImpact), "端で止まる");
            Assert.That(round.ImpactX, Is.EqualTo(-GolfConfig.MaxImpact));
            flow.Apply(new Intent(IntentKind.ImpactDown, 0));
            flow.Apply(new Intent(IntentKind.ImpactRight, 0));
            Assert.That(round.ImpactY, Is.EqualTo(GolfConfig.MaxImpact - 1));
            Assert.That(round.ImpactX, Is.EqualTo(-GolfConfig.MaxImpact + 1));
        }

        /// <summary>同じ時刻の入力列を、フレームの刻みを変えて流す。</summary>
        private static uint PlayScript(params double[] frames)
        {
            var flow = AppKit.Flow();
            flow.Apply(new Intent(IntentKind.Swing, 0));
            var half = flow.Config.MeterHalfTicks;
            var script = new List<Intent>
            {
                new(IntentKind.AimLeftDown, 0.21),
                new(IntentKind.AimLeftUp, 0.62),
                new(IntentKind.Swing, 1.0),
                new(IntentKind.Swing, 1.0 + ((half - 7) * Tick) + 0.004),
                new(IntentKind.Swing, 1.0 + ((2 * half) * Tick) + 0.011),
            };
            var now = 0.0;
            var index = 0;
            var frame = 0;
            while (now < 30)
            {
                now += frames[frame++ % frames.Length];
                while (index < script.Count && script[index].Time <= now) { flow.Apply(script[index++]); }
                flow.Advance(now);
            }

            Assert.That(flow.Round!.Strokes, Is.EqualTo(1));
            return flow.Round.Hash();
        }

        [Test]
        public void SameInputTimesGiveTheSameResultAtAnyFrameRate()
        {
            var reference = PlayScript(1.0 / 60);
            Assert.That(PlayScript(1.0 / 30), Is.EqualTo(reference));
            Assert.That(PlayScript(1.0 / 144), Is.EqualTo(reference));
            Assert.That(PlayScript(1.0 / 240), Is.EqualTo(reference));
            Assert.That(PlayScript(0.007, 0.031, 0.016, 0.052, 0.003), Is.EqualTo(reference));
        }

        [Test]
        public void NonFiniteTimeIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Intent(IntentKind.Swing, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Intent((IntentKind)999, 0));
            var flow = Started();
            flow.Advance(double.PositiveInfinity);
            Assert.That(flow.Round!.Ticks, Is.EqualTo(0));
        }
    }
}
