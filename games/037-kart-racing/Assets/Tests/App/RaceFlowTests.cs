using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class RaceFlowTests
    {
        private const double Tick = 1.0 / RaceFlow.TickRate;

        [Test]
        public void TitleStartsARaceAndTheResultCanBeReplayed()
        {
            var config = new KartConfig { MaxRaceTicks = 40, StartDelayTicks = 2 };
            var flow = new RaceFlow(config, Track.Parse(TestKit.DataText("Track.txt"), config));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            flow.Apply(new Intent(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Frames(flow, 0, 1.0);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            var first = flow.Race.Hash();
            flow.Apply(new Intent(IntentKind.Submit, 1.0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Race.Tick, Is.EqualTo(0));
            Assert.That(flow.Race.Hash(), Is.Not.EqualTo(first));
        }

        [Test]
        public void InputAfterTheResultDoesNotChangeTheRace()
        {
            var config = new KartConfig { MaxRaceTicks = 20, StartDelayTicks = 0 };
            var flow = new RaceFlow(config, Track.Parse(TestKit.DataText("Track.txt"), config));
            flow.Apply(new Intent(IntentKind.Submit, 0));
            Frames(flow, 0, 1.0);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            var hash = flow.Race.Hash();
            flow.Apply(new Intent(IntentKind.AccelDown, 1.0));
            flow.Advance(2.0);
            Assert.That(flow.Race.Hash(), Is.EqualTo(hash));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
        }

        [Test]
        public void TheAutopilotWinsAndTheCpusFinish()
        {
            var flow = TestKit.Flow();
            flow.Pilot = new Autopilot();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            var guard = 0;
            while (flow.Screen == AppScreen.Playing && guard < new KartConfig().MaxRaceTicks + 5)
            {
                flow.StepTick();
                guard++;
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Won, Is.True, $"順位 {flow.Race.PlaceOf(0)}");
            for (var i = 1; i <= 3; i++) { Assert.That(flow.Race.KartAt(i).Finished, Is.True, $"CPU {i}"); }
        }

        [Test]
        public void NoInputStillReachesTheResultWithThePlayerLast()
        {
            var flow = TestKit.Flow();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            var guard = 0;
            while (flow.Screen == AppScreen.Playing && guard < new KartConfig().MaxRaceTicks + 5)
            {
                flow.StepTick();
                guard++;
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Won, Is.False);
            Assert.That(flow.Race.PlaceOf(0), Is.EqualTo(4));
        }

        [Test]
        public void SameKeysGiveTheSameStateAtAnyFrameRate()
        {
            var keys = new List<Intent>
            {
                new(IntentKind.AccelDown, 0.20),
                new(IntentKind.SteerLeftDown, 0.50),
                new(IntentKind.DriftDown, 0.70),
                new(IntentKind.DriftUp, 0.90),
                new(IntentKind.SteerLeftUp, 1.05),
                new(IntentKind.AccelUp, 1.40),
            };

            uint? expected = null;
            foreach (var frame in new[] { 1.0 / 30, 1.0 / 60, 1.0 / 144, 1.0 / 240, -1.0 })
            {
                var flow = TestKit.Flow();
                flow.Apply(new Intent(IntentKind.Submit, 0));
                var random = new Random(5);
                var next = 0;
                for (var now = 0.0; now < 2.0;)
                {
                    now = Math.Min(2.0, now + (frame > 0 ? frame : 0.002 + (random.NextDouble() * 0.04)));
                    while (next < keys.Count && keys[next].Time <= now) { flow.Apply(keys[next++]); }
                    flow.Advance(now);
                }

                Assert.That(flow.Race.Tick, Is.EqualTo(2 * RaceFlow.TickRate), $"frame {frame}");
                var hash = flow.Race.Hash();
                expected ??= hash;
                Assert.That(hash, Is.EqualTo(expected), $"frame {frame}");
            }
        }

        [Test]
        public void FocusLossDropsTheDriftWithoutABoost([Values(true, false)] bool keyUpFirst)
        {
            var config = new KartConfig { StartDelayTicks = 0, TurnAtLow = 30, TurnAtHigh = 20, DriftTurn = 10 };
            var flow = new RaceFlow(config, Track.Parse(WideText(), config));
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Apply(new Intent(IntentKind.AccelDown, 0));
            Frames(flow, 0, 40 * Tick);
            flow.Apply(new Intent(IntentKind.SteerLeftDown, 40 * Tick));
            flow.Apply(new Intent(IntentKind.DriftDown, 40 * Tick));
            Frames(flow, 40 * Tick, 50 * Tick);
            Assert.That(flow.Race.KartAt(0).DriftTicks, Is.GreaterThan(0));

            var up = new Intent(IntentKind.DriftUp, 50 * Tick);
            if (keyUpFirst) { flow.Apply(up); }
            flow.SetFocus(false);
            if (!keyUpFirst) { flow.Apply(up); }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.Race.KartAt(0).DriftTicks, Is.EqualTo(0));
            Assert.That(flow.Race.KartAt(0).BoostLeft, Is.EqualTo(0));
            var tick = flow.Race.Tick;
            flow.Advance(2.0);
            Assert.That(flow.Race.Tick, Is.EqualTo(tick));
            flow.SetFocus(true);
            flow.Apply(new Intent(IntentKind.Cancel, 2.0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void ACleanReleaseStillGrantsTheBoost()
        {
            var config = new KartConfig { StartDelayTicks = 0, TurnAtLow = 30, TurnAtHigh = 20, DriftTurn = 10 };
            var flow = new RaceFlow(config, Track.Parse(WideText(), config));
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Apply(new Intent(IntentKind.AccelDown, 0));
            Frames(flow, 0, 40 * Tick);
            flow.Apply(new Intent(IntentKind.SteerLeftDown, 40 * Tick));
            flow.Apply(new Intent(IntentKind.DriftDown, 40 * Tick));
            var releaseAt = (40 + config.MiniTurboTicks + 2) * Tick;
            Frames(flow, 40 * Tick, releaseAt);
            flow.Apply(new Intent(IntentKind.DriftUp, releaseAt));
            flow.Advance(releaseAt + Tick);
            Assert.That(flow.Race.KartAt(0).BoostLeft, Is.GreaterThan(0));
        }

        private static void Frames(RaceFlow flow, double from, double to)
        {
            for (var now = from; now < to - 1e-9; now += Tick) { flow.Advance(Math.Min(to, now + Tick)); }
        }

        private static string WideText()
        {
            var text = new System.Text.StringBuilder();
            void Station(int x, int y) => text.Append("station ").Append(x).Append(' ').Append(y).Append(" 4000\n");
            for (var x = 0; x <= 60000; x += 4000) { Station(x, 0); }
            for (var y = 4000; y <= 40000; y += 4000) { Station(60000, y); }
            for (var x = 56000; x >= 0; x -= 4000) { Station(x, 40000); }
            for (var y = 36000; y >= 4000; y -= 4000) { Station(0, y); }
            text.Append("checkpoint 0\ncheckpoint 15\ncheckpoint 25\ncheckpoint 40\n");
            return text.ToString();
        }
    }
}
