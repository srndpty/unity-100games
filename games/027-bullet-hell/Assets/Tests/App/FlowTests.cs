using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>画面遷移・時刻と Tick・入力の受け付け・再挑戦。</summary>
    public sealed class FlowTests
    {
        private const double Tick = 1.0 / Field.TicksPerSecond;

        /// <summary>撃たない弾幕（時間の経過だけを見る）。</summary>
        private const string Quiet = "pattern \"Q\" hp=100000 time=36000\nemit ring every=3600 delay=36000 speed=1";

        /// <summary>真上から自機へ大玉を毎 Tick 撃つ（動かなければ約 74 Tick で被弾する）。</summary>
        private const string Lethal = "pattern \"L\" hp=100000 time=36000\nemit aimed every=1 speed=4 shape=large";

        private static VeilConfig Quick() => new() { IntervalTicks = 1 };

        [Test]
        public void Title_SubmitStartsAndTicksFollowTime()
        {
            var flow = new VeilFlow(Quick(), TestScripts.Of(Quiet, Quiet), firstSeed: 10);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Run, Is.Null);

            flow.Apply(Intent.Of(IntentKind.Submit, 5.0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Run!.Seed, Is.EqualTo(10));
            Assert.That(flow.TakeEvents() & FlowEvents.Started, Is.EqualTo(FlowEvents.Started));

            flow.Advance(5.0 + (3.5 * Tick));
            Assert.That(flow.Run.Elapsed, Is.EqualTo(3));
            flow.Advance(5.0 + (3.9 * Tick));
            Assert.That(flow.Run.Elapsed, Is.EqualTo(3));
        }

        [Test]
        public void LongFrame_IsCappedAndSkipsTheRest()
        {
            var flow = Started(Quiet, Quiet);
            flow.Advance(10.0);
            Assert.That(flow.Run!.Elapsed, Is.EqualTo(VeilFlow.MaxTicksPerFrame));
            flow.Advance(10.0 + Tick);
            Assert.That(flow.Run.Elapsed, Is.EqualTo(VeilFlow.MaxTicksPerFrame + 1), "飛ばした区間は後で取り戻さない");
        }

        [Test]
        public void KeyPress_AppliesFromTheTickContainingItsTime()
        {
            var flow = Started(Quiet, Quiet);
            flow.Advance(2.5 * Tick);
            var x = flow.Run!.Player.X;
            flow.Apply(Intent.Down(Control.Right, 3.5 * Tick));
            Assert.That(flow.Run.Elapsed, Is.EqualTo(3), "押した時刻の前の Tick を先に進める");
            Assert.That(flow.Run.Player.X, Is.EqualTo(x));
            flow.Advance(4.5 * Tick);
            Assert.That(flow.Run.Player.X, Is.EqualTo(x + Quick().PlayerSpeed), "押した時刻を含む 4 Tick 目から動く");
        }

        [Test]
        public void SlowAndShot_AreHeldAndShortShotTapStillFires()
        {
            var flow = Started(Quiet, Quiet);
            flow.Apply(Intent.Down(Control.Slow, 0.1 * Tick));
            flow.Apply(Intent.Down(Control.Shot, 0.2 * Tick));
            flow.Apply(Intent.Up(Control.Shot, 0.3 * Tick));
            flow.Advance(1.5 * Tick);
            Assert.That(flow.LastCommand.Slow, Is.True);
            Assert.That(flow.LastCommand.Shoot, Is.True, "次の Tick までに離した短い押下も 1 回撃つ");
            Assert.That(flow.Run!.Shots.Count, Is.EqualTo(3));
            flow.Advance(2.5 * Tick);
            Assert.That(flow.LastCommand.Shoot, Is.False);
        }

        [Test]
        public void Bomb_IsUsedOncePerPress()
        {
            var config = Quick();
            config.BombTicks = 3;
            var flow = new VeilFlow(config, TestScripts.Of(Quiet, Quiet), 1);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            flow.Apply(Intent.Down(Control.Bomb, 0.5 * Tick));
            flow.Advance(10.5 * Tick);
            Assert.That(flow.Run!.Bombs, Is.EqualTo(config.Bombs - 1), "押したままでもボムは 1 回");

            flow.Apply(Intent.Up(Control.Bomb, 11.1 * Tick));
            flow.Apply(Intent.Down(Control.Bomb, 11.2 * Tick));
            flow.Apply(Intent.Up(Control.Bomb, 11.4 * Tick));
            flow.Advance(12.5 * Tick);
            Assert.That(flow.Run.Bombs, Is.EqualTo(config.Bombs - 2), "押し直すと次の 1 回");
        }

        [Test]
        public void Pause_StopsTimeAndResumeRestartsTheClock()
        {
            var flow = Started(Quiet, Quiet);
            flow.Advance(2.5 * Tick);
            flow.Apply(Intent.Of(IntentKind.TogglePause, 2.6 * Tick));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Advance(100.0);
            Assert.That(flow.Run!.Elapsed, Is.EqualTo(2));
            flow.Apply(Intent.Of(IntentKind.TogglePause, 100.0));
            flow.Advance(100.0 + (1.5 * Tick));
            Assert.That(flow.Run.Elapsed, Is.EqualTo(3));
        }

        [Test]
        public void FocusLoss_PausesAndDropsHeldKeysAndInputs()
        {
            var flow = Started(Quiet, Quiet);
            flow.Apply(Intent.Down(Control.Left, 0.5 * Tick));
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Of(IntentKind.TogglePause, 1.0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間の入力は捨てる");

            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
            flow.Apply(Intent.Of(IntentKind.TogglePause, 2.0));
            var x = flow.Run!.Player.X;
            flow.Advance(2.0 + (3.5 * Tick));
            Assert.That(flow.Run.Player.X, Is.EqualTo(x), "失う前に押していたキーは持ち越さない");
        }

        [Test]
        public void ControlsOutsidePlay_AreNotCarriedIntoTheGame()
        {
            var flow = new VeilFlow(Quick(), TestScripts.Of(Quiet, Quiet), 1);
            flow.Apply(Intent.Down(Control.Up, 0));
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            flow.Advance(3.5 * Tick);
            Assert.That(flow.Run!.Player, Is.EqualTo(Quick().PlayerStart));
        }

        [Test]
        public void Lost_ResultOffersRetryFromTheSameBossWithNewSeed()
        {
            var config = Quick();
            config.Lives = 1;
            var flow = new VeilFlow(config, TestScripts.Of("pattern \"E\" hp=3 time=600\nemit ring every=3600 delay=36000 speed=1", Lethal), 7);
            flow.Pilot = new ShootOnly();
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            RunUntil(flow, () => flow.Screen == AppScreen.Result);

            var lost = flow.Run!;
            Assert.That(lost.Status, Is.EqualTo(RunStatus.Lost));
            Assert.That(lost.BossIndex, Is.EqualTo(1));
            Assert.That(lost.BossStartScore, Is.GreaterThan(0));
            Assert.That(lost.Score, Is.GreaterThanOrEqualTo(lost.BossStartScore));
            Assert.That(flow.CanRetryBoss, Is.True);

            flow.Apply(Intent.Down(Control.Right, 1000.0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result), "結果の画面では操作キーを受け付けない");

            flow.TakeEvents();
            flow.Apply(Intent.Of(IntentKind.RetryBoss, 1000.0));
            var retry = flow.Run!;
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(retry, Is.Not.SameAs(lost));
            Assert.That(retry.BossIndex, Is.EqualTo(1));
            Assert.That(retry.PatternIndex, Is.EqualTo(0));
            Assert.That(retry.Lives, Is.EqualTo(config.Lives));
            Assert.That(retry.Score, Is.EqualTo(lost.BossStartScore));
            Assert.That(retry.Seed, Is.EqualTo(lost.Seed + 1), "再挑戦も新しいシード");
            Assert.That(flow.TakeEvents() & FlowEvents.Retried, Is.EqualTo(FlowEvents.Retried));
        }

        [Test]
        public void LostOnFirstBoss_RetryIsAlsoMarked()
        {
            var config = Quick();
            config.Lives = 1;
            var flow = new VeilFlow(config, TestScripts.Of(Lethal, Lethal), 1);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            RunUntil(flow, () => flow.Screen == AppScreen.Result);
            flow.TakeEvents();
            flow.Apply(Intent.Of(IntentKind.RetryBoss, 1000.0));
            Assert.That(flow.Run!.BossIndex, Is.EqualTo(0));
            Assert.That(flow.TakeEvents() & FlowEvents.Retried, Is.EqualTo(FlowEvents.Retried));
        }

        [Test]
        public void Won_ResultHasNoRetryButRestartAndTitleWork()
        {
            var flow = new VeilFlow(Quick(), TestScripts.Of("pattern \"E\" hp=100 time=2\nemit ring every=3600 delay=36000 speed=1", "pattern \"F\" hp=100 time=2\nemit ring every=3600 delay=36000 speed=1"), 3);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            RunUntil(flow, () => flow.Screen == AppScreen.Result);
            Assert.That(flow.Run!.Status, Is.EqualTo(RunStatus.Won));
            Assert.That(flow.CanRetryBoss, Is.False);

            var won = flow.Run;
            flow.Apply(Intent.Of(IntentKind.RetryBoss, 1000.0));
            Assert.That(flow.Run, Is.SameAs(won), "勝った結果では再挑戦しない");

            flow.Apply(Intent.Of(IntentKind.Restart, 1000.0));
            Assert.That(flow.Run, Is.Not.SameAs(won));
            Assert.That(flow.Run!.BossIndex, Is.EqualTo(0));
            Assert.That(flow.Run.Seed, Is.EqualTo(4));

            flow.Apply(Intent.Of(IntentKind.TogglePause, 1000.0));
            flow.Apply(Intent.Of(IntentKind.BackToTitle, 1000.0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Run, Is.Null);
        }

        [Test]
        public void ScreenIntents_OnWrongScreen_AreIgnored()
        {
            var flow = new VeilFlow(Quick(), TestScripts.Of(Quiet, Quiet), 1);
            flow.Apply(Intent.Of(IntentKind.RetryBoss, 0));
            flow.Apply(Intent.Of(IntentKind.Restart, 0));
            flow.Apply(Intent.Of(IntentKind.BackToTitle, 0));
            flow.Apply(Intent.Of(IntentKind.TogglePause, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Run, Is.Null);

            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            var run = flow.Run;
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            flow.Apply(Intent.Of(IntentKind.RetryBoss, 0));
            Assert.That(flow.Run, Is.SameAs(run), "プレイ中の決定・再挑戦は何もしない");
        }

        [Test]
        public void InvalidIntentsAndSetup_AreRejected()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Intent.Of(IntentKind.Submit, double.NaN));
            Assert.Throws<System.ArgumentException>(() => Intent.Of(IntentKind.ControlDown, 0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Intent.Down((Control)99, 0));
            Assert.Throws<System.ArgumentException>(() => _ = new VeilFlow(Quick(), new[] { TestScripts.Of(Quiet, Quiet)[0] }, 1));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _ = new VeilFlow(new VeilConfig { Bombs = -1 }, TestScripts.Of(Quiet, Quiet), 1));
        }

        private static VeilFlow Started(string first, string second)
        {
            var flow = new VeilFlow(Quick(), TestScripts.Of(first, second), 1);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            return flow;
        }

        private static void RunUntil(VeilFlow flow, System.Func<bool> done)
        {
            for (var n = 0; n < 20000 && !done(); n++)
            {
                flow.StepTick();
                flow.EndFrame();
            }

            Assert.That(done(), Is.True);
        }

        /// <summary>動かずに撃ち続ける操縦役。</summary>
        private sealed class ShootOnly : IPilot
        {
            public PlayerCommand CommandFor(VeilRun run) => new(0, 0, false, true, false);
        }
    }
}
