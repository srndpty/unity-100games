using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class PinballFlowTests
    {
        private const double Tick = 1.0 / PinballFlow.TickRate;

        /// <summary>1/60 秒ごとのフレームで時刻 <paramref name="to"/> まで進める（1 フレームの Tick の上限に掛からないように）。</summary>
        private static void Frames(PinballFlow flow, double from, double to)
        {
            for (var now = from; now < to;)
            {
                now = Math.Min(now + Tick, to);
                flow.Advance(now);
            }
        }

        [Test]
        public void TitleStartsWithSpaceWithoutPullingThePlunger()
        {
            var flow = AppKit.Flow();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Run, Is.Null);

            flow.Apply(new Intent(IntentKind.PlungerDown, 1.0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Started));

            // 開始に使った押下は引きにならない（離すまで押したままでも引かない）。
            Frames(flow, 1.0, 1.6);
            Assert.That(flow.Run!.Pull, Is.Zero);
            flow.Apply(new Intent(IntentKind.PlungerUp, 1.6));
            Frames(flow, 1.6, 1.7);
            flow.Apply(new Intent(IntentKind.PlungerDown, 1.7));
            Frames(flow, 1.7, 2.0);
            Assert.That(flow.Run.Pull, Is.EqualTo(18), "押し直した後は引く（1.7 秒の後の Tick から 2.0 秒まで）");
        }

        [Test]
        public void FlipperKeyTakesEffectInTheNextTick()
        {
            var flow = AppKit.Flow();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            Frames(flow, 0, 10 * Tick);
            var rest = flow.Table.Left.RestAngle;
            Assert.That(flow.Run!.FlipperOf(FlipperSide.Left).Angle, Is.EqualTo(rest));

            flow.Apply(new Intent(IntentKind.LeftDown, 10.5 * Tick));
            flow.Advance(11 * Tick);
            Assert.That(flow.Run.Ticks, Is.EqualTo(11));
            Assert.That(flow.Run.FlipperOf(FlipperSide.Left).Angle, Is.GreaterThan(rest), "押した直後の Tick で動く");

            Frames(flow, 11 * Tick, 30.2 * Tick);
            flow.Apply(new Intent(IntentKind.LeftUp, 30.2 * Tick));
            Frames(flow, 30.2 * Tick, 80 * Tick);
            Assert.That(flow.Run.FlipperOf(FlipperSide.Left).Angle, Is.EqualTo(rest), "離すと戻る");
        }

        /// <summary>同じキー操作を 30〜240fps・不規則な間隔のフレームで届けても、同じ状態になる（022）。</summary>
        [Test]
        public void SameKeysGiveTheSameStateAtAnyFrameRate()
        {
            var keys = new List<Intent>
            {
                new(IntentKind.PlungerDown, 0.30), new(IntentKind.PlungerUp, 1.21),
                new(IntentKind.LeftDown, 2.003), new(IntentKind.RightDown, 2.004), new(IntentKind.LeftUp, 2.31),
                new(IntentKind.RightUp, 2.6), new(IntentKind.LeftDown, 3.14159), new(IntentKind.LeftUp, 3.3),
                new(IntentKind.RightDown, 4.0), new(IntentKind.RightUp, 4.25),
            };

            uint? expected = null;
            foreach (var frame in new[] { 1.0 / 30, 1.0 / 60, 1.0 / 144, 1.0 / 240, -1 })
            {
                var flow = AppKit.Flow(3);
                flow.Apply(new Intent(IntentKind.Submit, 0));
                var random = new Random(5);
                var next = 0;
                for (var now = 0.0; now < 6.0;)
                {
                    now = Math.Min(6.0, now + (frame > 0 ? frame : 0.002 + (random.NextDouble() * 0.06)));
                    while (next < keys.Count && keys[next].Time <= now) { flow.Apply(keys[next++]); }
                    flow.Advance(now);
                }

                Assert.That(flow.Run!.Ticks, Is.EqualTo(6 * PinballFlow.TickRate), $"frame {frame}");
                var hash = flow.Run.Hash();
                expected ??= hash;
                Assert.That(hash, Is.EqualTo(expected), $"frame {frame}");
            }
        }

        [Test]
        public void FrameTickBudgetSkipsLongStalls()
        {
            var flow = AppKit.Flow();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Advance(10.0);
            Assert.That(flow.Run!.Ticks, Is.EqualTo(PinballFlow.MaxTicksPerFrame));
            flow.Advance(10.0 + Tick);
            Assert.That(flow.Run.Ticks, Is.EqualTo(PinballFlow.MaxTicksPerFrame + 1));
        }

        [Test]
        public void EscPausesAndTimeDoesNotAdvance()
        {
            var flow = AppKit.Flow();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Apply(new Intent(IntentKind.LeftDown, 0.01));
            Frames(flow, 0.01, 0.5);
            var ticks = flow.Run!.Ticks;
            flow.Apply(new Intent(IntentKind.Cancel, 0.5));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.Held, Is.EqualTo(new PinballInput(false, false, false)), "一時停止でキーを離す");
            flow.Advance(5.0);
            Assert.That(flow.Run.Ticks, Is.EqualTo(ticks));

            flow.Apply(new Intent(IntentKind.Cancel, 5.0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Frames(flow, 5.0, 5.0 + (10 * Tick));
            Assert.That(flow.Run.Ticks, Is.EqualTo(ticks + 10), "再開した時刻から数える");
        }

        [Test]
        public void FocusLossPausesAndReleasesKeys([Values(true, false)] bool keyUpFirst)
        {
            var flow = AppKit.Flow();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Apply(new Intent(IntentKind.RightDown, 0.1));
            flow.Apply(new Intent(IntentKind.PlungerDown, 0.1));
            flow.Advance(0.2);

            if (keyUpFirst) { flow.Apply(new Intent(IntentKind.RightUp, 0.25)); }
            flow.SetFocus(false);
            if (!keyUpFirst) { flow.Apply(new Intent(IntentKind.RightUp, 0.25)); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.Held, Is.EqualTo(new PinballInput(false, false, false)));

            // 戻っても自動では再開しない。キーを押しても一時停止のまま。
            flow.SetFocus(true);
            flow.Apply(new Intent(IntentKind.RightDown, 1.0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.Held.Right, Is.False);
            flow.Apply(new Intent(IntentKind.Submit, 1.1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Held, Is.EqualTo(new PinballInput(false, false, false)), "再開してもキーは押していないとみなす");
        }

        [Test]
        public void InputsWhileUnfocusedAreIgnored()
        {
            var flow = AppKit.Flow();
            flow.SetFocus(false);
            flow.Apply(new Intent(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [Test]
        public void GameRunsToResultAndReplaysWithANewSeed()
        {
            var flow = AppKit.Flow(4);
            flow.Pilot = new Autopilot();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            var first = flow.Run!;
            while (flow.Screen == AppScreen.Playing && first.Ticks < 60000) { flow.StepTick(); }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(first.Status, Is.EqualTo(RunStatus.Over));
            Assert.That(flow.TakeEvents() & FlowEvents.Result, Is.EqualTo(FlowEvents.Result));
            Assert.That(flow.BestScore, Is.EqualTo(first.Score));
            Assert.That(flow.IsNewBest, Is.True);

            // 結果の画面ではフリッパーのキーで何も起きない。
            var hash = first.Hash();
            flow.Apply(new Intent(IntentKind.LeftDown, 1));
            flow.StepTick();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(first.Hash(), Is.EqualTo(hash));

            flow.Apply(new Intent(IntentKind.Submit, 2));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Run, Is.Not.SameAs(first));
            Assert.That(flow.Run!.Seed, Is.Not.EqualTo(first.Seed), "再戦は新しいシード");
            Assert.That(flow.Run.Score, Is.Zero);
            Assert.That(flow.Run.BallNumber, Is.EqualTo(1));
        }

        [Test]
        public void ResultCancelReturnsToTitleAndPauseCanRetryOrQuit()
        {
            var flow = AppKit.Flow();
            flow.Apply(new Intent(IntentKind.Submit, 0));
            flow.Apply(new Intent(IntentKind.Cancel, 0.1));
            var seed = flow.Run!.Seed;
            flow.Apply(new Intent(IntentKind.Retry, 0.2));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Run!.Seed, Is.Not.EqualTo(seed));

            flow.Apply(new Intent(IntentKind.Cancel, 0.3));
            flow.Apply(new Intent(IntentKind.Back, 0.4));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Run, Is.Null);
        }

        [Test]
        public void ConfigIsCopied()
        {
            var config = new PinballConfig();
            var flow = new PinballFlow(config, AppKit.Production(), 1);
            config.BumperScore = 999;
            var copy = flow.Config;
            copy.BumperScore = 998;
            Assert.That(flow.Config.BumperScore, Is.EqualTo(new PinballConfig().BumperScore));
        }

        [Test]
        public void IntentRejectsInvalidValues()
        {
            Assert.That(() => new Intent(IntentKind.Submit, double.NaN), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => new Intent((IntentKind)99, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
        }
    }
}
