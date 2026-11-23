using System;
using Game.Core;
using NUnit.Framework;
using TileMotion;

namespace Game.App.Tests
{
    /// <summary>画面遷移・時刻付きの入力・押下の列。</summary>
    public sealed class RunnerFlowTests
    {
        private const double Tick = 1.0 / Units.TickRate;

        private static RunRules? _rules;

        /// <summary>中央のレーンに壁があるチャンクだけの組（何もしなければ必ずぶつかる）。</summary>
        private static RunRules Rules => _rules ??= RunRules.Build(new RunConfig(), new[]
        {
            new ChunkSpec { Name = "wall", Tier = 1, Weight = 1, Rows = new[] { "...", "...", "...", ".W.", "...", "..." } },
        });

        private static RunnerFlow Started(int seed = 1)
        {
            var flow = new RunnerFlow(Rules, seed);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            return flow;
        }

        /// <summary>Tick n の区間の中ほどの時刻。</summary>
        private static double During(long tick) => (tick - 0.5) * Tick;

        /// <summary>1 フレーム 1 Tick ずつ、Tick <paramref name="tick"/> の区間まで進める。</summary>
        private static void PlayUntil(RunnerFlow flow, long tick)
        {
            for (var t = flow.Run!.Ticks + 1; t <= tick && flow.Screen == AppScreen.Playing; t++) { flow.Advance(During(t)); }
        }

        private static void PlayUntilOver(RunnerFlow flow)
        {
            for (var t = 1; t < 10_000 && flow.Screen == AppScreen.Playing; t++) { flow.Advance(During(t)); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
        }

        [Test]
        public void Title_SubmitStartsRun()
        {
            var flow = new RunnerFlow(Rules, 1);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Run, Is.Null);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.TakeEvents() & FlowEvents.Started, Is.EqualTo(FlowEvents.Started));
        }

        [Test]
        public void Ticks_FollowTheClock_WithAFrameLimit()
        {
            var flow = Started();
            flow.Advance(During(4));
            Assert.That(flow.Run!.Ticks, Is.EqualTo(3), "Tick 4 の区間に入った時点では 3 Tick 進んでいる");
            flow.Advance(During(100));
            Assert.That(flow.Run.Ticks, Is.EqualTo(3 + RunnerFlow.MaxTicksPerFrame), "処理落ちした区間は飛ばす");
        }

        [Test]
        public void Press_TakesEffectInTheTickContainingIt()
        {
            var flow = Started();
            PlayUntil(flow, 9);
            flow.Apply(Intent.Press(RunMove.Right, During(11)));
            Assert.That(flow.Run!.Ticks, Is.EqualTo(10), "押した時刻の前の Tick まで進めてから列に入れる");
            flow.Advance(During(11));
            Assert.That(flow.Run.Ticks, Is.EqualTo(10), "Tick 11 は区間の終わりに達してから進める");
            Assert.That(flow.Run.Body.Lane.IsMoving, Is.False);
            flow.Advance(During(12));
            Assert.That(flow.Run.Ticks, Is.EqualTo(11));
            Assert.That(flow.Run.Body.Lane.IsMoving, Is.True);
            Assert.That(flow.Run.Body.Lane.To, Is.EqualTo(2));
        }

        [Test]
        public void PressesInOneTick_KeepTheirOrder_OneOfEachSidePerTick()
        {
            var flow = Started();
            var t = During(5);
            flow.Apply(Intent.Press(RunMove.Left, t));
            flow.Apply(Intent.Press(RunMove.Jump, t));
            flow.Apply(Intent.Press(RunMove.Right, t));
            flow.Apply(Intent.Press(RunMove.Slide, t));
            Assert.That(flow.PendingCount, Is.EqualTo(4));

            // Tick 5：左とジャンプ。右は横の 2 つ目なのでそこで止め、後ろのスライドも次の Tick へ回す。
            flow.Advance(During(6));
            var events = flow.TakeEvents();
            Assert.That(events & (FlowEvents.Steered | FlowEvents.Jumped), Is.EqualTo(FlowEvents.Steered | FlowEvents.Jumped));
            Assert.That(flow.PendingCount, Is.EqualTo(2));
            flow.Advance(During(7));
            Assert.That(flow.PendingCount, Is.Zero);
            Assert.That(flow.Run!.Body.SteerBuffer, Is.EqualTo(Steer.Right), "移動中の右は先行入力");
            Assert.That(flow.Run.Body.LeapBuffer, Is.EqualTo(Leap.Slide), "空中のスライドは先行入力");
        }

        [Test]
        public void TheKeyThatStartsOrResumes_DoesNotAlsoJump()
        {
            var flow = new RunnerFlow(Rules, 1);
            flow.Apply(Intent.Of(IntentKind.Submit, 2));
            flow.Apply(Intent.Press(RunMove.Jump, 2));
            Assert.That(flow.PendingCount, Is.Zero, "始めた押下（Space）は跳ばない");
            flow.Apply(Intent.Press(RunMove.Jump, 2 + (Tick / 2)));
            Assert.That(flow.PendingCount, Is.EqualTo(1), "その後の押下は効く");

            flow.Apply(Intent.Of(IntentKind.TogglePause, 3));
            flow.Apply(Intent.Of(IntentKind.Submit, 4));
            flow.Apply(Intent.Press(RunMove.Jump, 4));
            Assert.That(flow.PendingCount, Is.Zero, "再開した押下も跳ばない");
        }

        [Test]
        public void MashingKeys_DoesNotGrowTheQueue()
        {
            var flow = Started();
            for (var i = 0; i < 100; i++) { flow.Apply(Intent.Press(RunMove.Jump, During(3))); }
            Assert.That(flow.PendingCount, Is.EqualTo(RunnerFlow.MaxPending));
        }

        [Test]
        public void Crash_ShowsResult_ThenRetryStartsANewSeed()
        {
            var flow = Started(10);
            PlayUntilOver(flow);
            Assert.That(flow.TakeEvents() & FlowEvents.Crashed, Is.EqualTo(FlowEvents.Crashed));
            var first = flow.Run!;
            Assert.That(flow.BestMeters, Is.EqualTo(first.Meters));
            Assert.That(flow.IsNewBest, Is.True);

            // 結果画面の押下と時計は試合を変えない。
            var hash = first.Hash();
            flow.Apply(Intent.Press(RunMove.Left, 100));
            flow.Advance(200);
            Assert.That(first.Hash(), Is.EqualTo(hash));

            flow.Apply(Intent.Of(IntentKind.Restart, 300));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Run, Is.Not.SameAs(first));
            Assert.That(flow.Run!.Seed, Is.EqualTo(11));
            Assert.That(flow.IsNewBest, Is.False);
        }

        [Test]
        public void ShorterRun_KeepsTheBest()
        {
            var flow = Started();
            flow.Pilot = new Autopilot();
            for (var t = 1; t <= 3000; t++) { flow.Advance(During(t)); }
            flow.Pilot = null;

            // 自動操縦が左右のレーンに寄っていたら、中央へ戻して壁にぶつける。
            var lane = flow.Run!.Body.Lane.To;
            if (lane != 1) { flow.Apply(Intent.Press(lane == 0 ? RunMove.Right : RunMove.Left, During(3001))); }
            PlayUntil(flow, 10_000);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            var best = flow.BestMeters;
            Assert.That(best, Is.GreaterThan(100));

            flow.Apply(Intent.Of(IntentKind.Submit, 1000));
            for (var t = 1; t < 10_000 && flow.Screen == AppScreen.Playing; t++) { flow.Advance(1000 + During(t)); }
            Assert.That(flow.Run!.Meters, Is.LessThan(best));
            Assert.That(flow.BestMeters, Is.EqualTo(best));
            Assert.That(flow.IsNewBest, Is.False);
        }

        [Test]
        public void Pause_StopsTheClock_AndDropsPresses()
        {
            var flow = Started();
            PlayUntil(flow, 10);
            flow.Apply(Intent.Press(RunMove.Left, During(11)));
            flow.Apply(Intent.Of(IntentKind.TogglePause, During(11)));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.PendingCount, Is.Zero, "一時停止の前の押下を再開後へ持ち越さない");
            flow.Advance(50);
            Assert.That(flow.Run!.Ticks, Is.EqualTo(10));

            flow.Apply(Intent.Of(IntentKind.TogglePause, 60));
            flow.Advance(60 + (2.5 * Tick));
            Assert.That(flow.Run.Ticks, Is.EqualTo(12), "再開した時刻から数え直す");
            Assert.That(flow.Run.Body.Lane.To, Is.EqualTo(1));
        }

        [Test]
        public void LosingFocus_PausesAndIgnoresInput_UntilResumedByHand()
        {
            var flow = Started();
            PlayUntil(flow, 5);
            flow.Apply(Intent.Press(RunMove.Jump, During(6)));
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.PendingCount, Is.Zero);
            flow.Apply(Intent.Of(IntentKind.Submit, 10));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスのない間は操作しない");

            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
            flow.Apply(Intent.Of(IntentKind.Submit, 20));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void FocusLostAndPauseKey_InEitherOrder_StayPaused()
        {
            var a = Started();
            PlayUntil(a, 5);
            a.Apply(Intent.Of(IntentKind.TogglePause, During(6)));
            a.SetFocus(false);
            a.SetFocus(true);
            Assert.That(a.Screen, Is.EqualTo(AppScreen.Paused));

            var b = Started();
            PlayUntil(b, 5);
            b.SetFocus(false);
            b.Apply(Intent.Of(IntentKind.TogglePause, During(6)));
            b.SetFocus(true);
            Assert.That(b.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスのない間の停止キーで再開しない");
        }

        [Test]
        public void BackToTitle_FromResult_ClearsTheRun()
        {
            var flow = Started();
            PlayUntilOver(flow);
            flow.Apply(Intent.Of(IntentKind.BackToTitle, 500));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Run, Is.Null);
            flow.Apply(Intent.Of(IntentKind.Restart, 501));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title), "タイトルでは始め直しは効かない");
        }

        [Test]
        public void InvalidIntents_AreRejected()
        {
            Assert.That(() => Intent.Of(IntentKind.Press, 0), Throws.ArgumentException);
            Assert.That(() => Intent.Press((RunMove)9, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => Intent.Of(IntentKind.Submit, double.NaN), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => Intent.Of((IntentKind)99, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
