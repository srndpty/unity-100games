using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class SortieFlowTests
    {
        private const double Tick = 1.0 / SortieFlow.TickRate;

        private static SortieFlow Flow(MissionConfig? config = null, int seed = 100) =>
            new(new FlightConfig(), config ?? new MissionConfig(), seed);

        [Test]
        public void TitleStartsAMissionAndTheResultCanBeRetriedWithANewSeed()
        {
            var flow = Flow(new MissionConfig { MissionTicks = 60 });
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            flow.Apply(new Intent(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.TakeEvents() & FlowEvents.Started, Is.EqualTo(FlowEvents.Started));
            var firstSeed = flow.Mission.Seed;

            Frames(flow, 0, 2.0);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Mission.Outcome, Is.EqualTo(Outcome.TimeUp));
            Assert.That(flow.Won, Is.False);
            Assert.That(flow.TakeEvents() & FlowEvents.Result, Is.EqualTo(FlowEvents.Result));

            flow.Apply(new Intent(IntentKind.Submit, 2.0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Mission.Tick, Is.Zero);
            Assert.That(flow.Mission.Seed, Is.Not.EqualTo(firstSeed), "再挑戦は新しいシード");

            Frames(flow, 2.0, 4.0);
            flow.Apply(new Intent(IntentKind.Cancel, 4.0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [Test]
        public void InputAfterTheResultDoesNotChangeTheMission()
        {
            var flow = Flow(new MissionConfig { MissionTicks = 60 });
            flow.Apply(new Intent(IntentKind.Submit, 0));
            Frames(flow, 0, 2.0);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            var hash = flow.Mission.Hash();
            flow.Apply(new Intent(IntentKind.ThrottleUpDown, 2.0));
            flow.Apply(new Intent(IntentKind.FireDown, 2.0));
            flow.Advance(3.0);
            flow.StepTick();
            Assert.That(flow.Mission.Hash(), Is.EqualTo(hash));
        }

        [Test]
        public void HeldKeysBecomeAxesAndEachPhysicalKeyIsTrackedSeparately()
        {
            var flow = Flow();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Apply(new Intent(IntentKind.PitchUpDown, 0, 0));
            Assert.That(flow.Held.Pitch, Is.EqualTo(Controls.Full));
            flow.Apply(new Intent(IntentKind.PitchDownDown, 0));
            Assert.That(flow.Held.Pitch, Is.Zero, "反対の 2 つを押すと 0");
            flow.Apply(new Intent(IntentKind.PitchDownUp, 0));

            // S と ↓ の両方で機首上げ。片方を離してももう片方で押したまま。
            flow.Apply(new Intent(IntentKind.PitchUpDown, 0, 1));
            flow.Apply(new Intent(IntentKind.PitchUpUp, 0, 0));
            Assert.That(flow.Held.Pitch, Is.EqualTo(Controls.Full));
            flow.Apply(new Intent(IntentKind.PitchUpUp, 0, 1));
            Assert.That(flow.Held.Pitch, Is.Zero);

            flow.Apply(new Intent(IntentKind.RollRightDown, 0));
            flow.Apply(new Intent(IntentKind.YawLeftDown, 0));
            flow.Apply(new Intent(IntentKind.ThrottleDownDown, 0));
            Assert.That(flow.Held, Is.EqualTo(new Controls(0, Controls.Full, -Controls.Full, -Controls.Full)));
        }

        [Test]
        public void FirePressGoesToTheNextTickOnceEvenIfTheDownArrivesTwice()
        {
            var flow = Flow();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Apply(new Intent(IntentKind.FireDown, 0.001));
            flow.Apply(new Intent(IntentKind.FireDown, 0.002));
            flow.Advance(Tick * 1.5);
            Assert.That(flow.LastFire, Is.EqualTo(FireResult.Grounded), "滑走路の上なので拒否されるが、要求は届く");
            Assert.That(flow.LastFireTick, Is.EqualTo(1));

            // 押したままの重複した押下は次の要求にならない。
            flow.Apply(new Intent(IntentKind.FireDown, Tick * 2));
            flow.Advance(Tick * 3.5);
            Assert.That(flow.LastFireTick, Is.EqualTo(1));

            // 離して押し直すと次の要求になる。
            flow.Apply(new Intent(IntentKind.FireUp, Tick * 3.6));
            flow.Apply(new Intent(IntentKind.FireDown, Tick * 3.7));
            flow.Advance(Tick * 4.5);
            Assert.That(flow.LastFireTick, Is.EqualTo(4));
        }

        [Test]
        public void LosingFocusPausesAndDropsHeldKeysAndPendingFire()
        {
            var flow = Flow();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Apply(new Intent(IntentKind.ThrottleUpDown, 0));
            flow.Apply(new Intent(IntentKind.FireDown, 0.001));
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.Held, Is.EqualTo(Controls.None));

            // フォーカスがない間の入力は捨てる。復帰は手動。
            flow.Apply(new Intent(IntentKind.Submit, 0.5));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.SetFocus(true);
            flow.Apply(new Intent(IntentKind.Submit, 1.0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            flow.Advance(1.0 + (Tick * 3));
            Assert.That(flow.LastFire, Is.EqualTo(FireResult.None), "保留していた発射は捨てた");
            Assert.That(flow.Mission.Player.View.Throttle, Is.Zero, "押していたスロットルも離した");
        }

        [Test]
        public void PauseStopsTimeAndRetryOrBackLeaveIt()
        {
            var flow = Flow();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Advance(1.0);
            var tick = flow.Mission.Tick;
            flow.Apply(new Intent(IntentKind.Cancel, 1.0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Advance(5.0);
            Assert.That(flow.Mission.Tick, Is.EqualTo(tick));
            flow.Apply(new Intent(IntentKind.Cancel, 5.0));
            flow.Advance(5.0 + (Tick * 2.5));
            Assert.That(flow.Mission.Tick, Is.EqualTo(tick + 2), "再開した時刻から数える");

            flow.Apply(new Intent(IntentKind.Cancel, 6.0));
            var seed = flow.Mission.Seed;
            flow.Apply(new Intent(IntentKind.Retry, 6.0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Mission.Seed, Is.Not.EqualTo(seed));
            flow.Apply(new Intent(IntentKind.Cancel, 6.0));
            flow.Apply(new Intent(IntentKind.Back, 6.0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Mission.Tick, Is.Zero);
        }

        [Test]
        public void OneFrameAdvancesAtMostTheTickCap()
        {
            var flow = Flow();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Advance(10.0);
            Assert.That(flow.Mission.Tick, Is.EqualTo(SortieFlow.MaxTicksPerFrame));
        }

        [Test]
        public void SameKeyTimelineGivesTheSameHashAtAnyFrameRate()
        {
            var timeline = new List<Intent>
            {
                new(IntentKind.ThrottleUpDown, 0.05),
                new(IntentKind.ThrottleUpUp, 1.7),
                new(IntentKind.PitchUpDown, 4.3),
                new(IntentKind.PitchUpUp, 5.9),
                new(IntentKind.RollRightDown, 6.25),
                new(IntentKind.RollRightUp, 6.61),
                new(IntentKind.PitchUpDown, 6.7),
                new(IntentKind.FireDown, 7.01),
                new(IntentKind.FireUp, 7.2),
                new(IntentKind.PitchUpUp, 8.3),
                new(IntentKind.YawLeftDown, 8.4),
                new(IntentKind.YawLeftUp, 9.0),
            };

            uint? expected = null;
            foreach (var fps in new[] { 30.0, 60.0, 144.0, 240.0, 47.3 })
            {
                var flow = Flow(seed: 9);
                flow.Apply(new Intent(IntentKind.Submit, 0));
                var next = 0;
                for (var frame = 1; frame * (1 / fps) <= 10.0; frame++)
                {
                    var now = frame / fps;
                    while (next < timeline.Count && timeline[next].Time <= now) { flow.Apply(timeline[next++]); }
                    flow.Advance(now);
                }

                flow.Advance(10.0);
                Assert.That(flow.Mission.Tick, Is.EqualTo(600));
                Assert.That(flow.Mission.Player.View.Grounded, Is.False, "離陸した");
                var hash = flow.Mission.Hash();
                expected ??= hash;
                Assert.That(hash, Is.EqualTo(expected.Value), $"{fps} fps");
            }
        }

        [Test]
        public void UndefinedOrInvalidIntentsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Intent((IntentKind)999, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Intent(IntentKind.Submit, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Intent(IntentKind.Submit, 0, Intent.MaxKeys));
        }

        private static void Frames(SortieFlow flow, double from, double to)
        {
            for (var time = from; time <= to; time += Tick) { flow.Advance(time); }
        }
    }
}
