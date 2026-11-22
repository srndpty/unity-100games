using System;
using Game.Core;
using NUnit.Framework;
using TileMotion;

namespace Game.App.Tests
{
    /// <summary>画面遷移・時刻付きの入力・照準の経路。</summary>
    public sealed class SiegeFlowTests
    {
        private const double Tick = 1.0 / Units.TickRate;

        /// <summary>敵が出ない設定。</summary>
        private static SiegeConfig Quiet(Action<SiegeConfig>? edit = null)
        {
            var config = new SiegeConfig
            {
                StageTicks = SiegeConfig.MaxDurationTicks,
                Stages = new[] { new SpawnStage { IntervalTicks = SiegeConfig.MaxDurationTicks, ChaserWeight = 1 } },
            };
            edit?.Invoke(config);
            return config;
        }

        private static SiegeFlow Started(SiegeConfig? config = null, int seed = 1)
        {
            var flow = new SiegeFlow(config ?? Quiet(), seed);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            return flow;
        }

        /// <summary>Tick n の区間の中ほどの時刻。</summary>
        private static double During(int tick) => (tick - 0.5) * Tick;

        /// <summary>1 フレーム 1 Tick ずつ、Tick <paramref name="tick"/> の区間まで進める（フレームの Tick 上限に当たらない）。</summary>
        private static void PlayUntil(SiegeFlow flow, int tick)
        {
            for (var t = 1; t <= tick; t++) { flow.Advance(During(t)); }
        }

        [Test]
        public void Title_SubmitStartsRun()
        {
            var flow = new SiegeFlow(Quiet(), 1);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Run, Is.Null);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.TakeEvents() & FlowEvents.Started, Is.EqualTo(FlowEvents.Started));
        }

        [Test]
        public void Ticks_FollowTheClock()
        {
            var flow = Started();
            flow.Advance(During(4));
            Assert.That(flow.Run!.Elapsed, Is.EqualTo(3), "Tick 4 の区間に入った時点では 3 Tick 進んでいる");
        }

        [Test]
        public void MoveKeysAndAim_TakeSeparatePaths()
        {
            var flow = Started();
            var run = flow.Run!;
            var start = run.Ship.Position;

            // 右へ動きながら、左下を狙って撃つ。
            flow.SetAim(start + new Vec2i(-20 * Units.Tile, -20 * Units.Tile));
            flow.Apply(Intent.Down(Control.Right, During(1)));
            flow.Apply(Intent.Down(Control.Fire, During(1)));
            PlayUntil(flow, 11);

            Assert.That(run.Ship.Position.X, Is.EqualTo(start.X + (run.Config.ShipSpeed * 10)));
            Assert.That(run.Ship.Position.Y, Is.EqualTo(start.Y));
            Assert.That(run.Bullets, Is.Not.Empty);
            foreach (var bullet in run.Bullets)
            {
                Assert.That(bullet.Velocity.X, Is.LessThan(0));
                Assert.That(bullet.Velocity.Y, Is.LessThan(0));
            }
        }

        [Test]
        public void AimChanges_ApplyToLaterTicksOnly()
        {
            var flow = Started();
            var run = flow.Run!;
            flow.SetAim(run.Ship.Position + new Vec2i(0, Units.Tile));
            flow.Advance(During(2));
            Assert.That(run.Ship.ShotVelocity, Is.EqualTo(new Vec2i(0, run.Config.BulletSpeed)));
            flow.SetAim(run.Ship.Position + new Vec2i(-Units.Tile, 0));
            Assert.That(run.Ship.ShotVelocity, Is.EqualTo(new Vec2i(0, run.Config.BulletSpeed)), "Tick を進めるまでは変わらない");
            flow.Advance(During(3));
            Assert.That(run.Ship.ShotVelocity, Is.EqualTo(new Vec2i(-run.Config.BulletSpeed, 0)));
        }

        [Test]
        public void ShortClick_FiresOnce()
        {
            var flow = Started();
            flow.Apply(Intent.Down(Control.Fire, During(1)));
            flow.Apply(Intent.Up(Control.Fire, During(1) + 0.001));
            flow.Advance(During(30));
            Assert.That(flow.Run!.Bullets, Has.Count.EqualTo(1));
        }

        [Test]
        public void OppositeKeys_UseLatestPressed()
        {
            var flow = Started();
            var run = flow.Run!;
            var start = run.Ship.Position;
            flow.Apply(Intent.Down(Control.Left, During(1)));
            flow.Apply(Intent.Down(Control.Right, During(1)));
            flow.Advance(During(2));
            Assert.That(run.Ship.Position.X, Is.EqualTo(start.X + run.Config.ShipSpeed));
            flow.Apply(Intent.Up(Control.Right, During(2)));
            flow.Advance(During(3));
            Assert.That(run.Ship.Position.X, Is.EqualTo(start.X), "離すと、まだ押しているほうに戻る");
        }

        [Test]
        public void Pause_StopsTicks_AndDropsHeldKeys()
        {
            var flow = Started();
            var run = flow.Run!;
            flow.Apply(Intent.Down(Control.Right, During(1)));
            flow.Apply(Intent.Of(IntentKind.TogglePause, During(3)));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            var position = run.Ship.Position;
            flow.Advance(During(100));
            Assert.That(run.Elapsed, Is.EqualTo(2));

            flow.Apply(Intent.Of(IntentKind.TogglePause, 10));
            flow.Advance(10 + During(5));
            Assert.That(run.Elapsed, Is.EqualTo(6));
            Assert.That(run.Ship.Position, Is.EqualTo(position), "一時停止の前に押したキーは持ち越さない");
        }

        [Test]
        public void FocusLoss_PausesAndIgnoresInputUntilBack()
        {
            var flow = Started();
            flow.Apply(Intent.Down(Control.Fire, During(1)));
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Of(IntentKind.Submit, 1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスのない間の入力は捨てる");

            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
            flow.Apply(Intent.Of(IntentKind.Submit, 2));
            flow.Advance(2 + During(10));
            Assert.That(flow.Run!.Bullets, Is.Empty, "フォーカスを失う前の押下は消えている");
        }

        [Test]
        public void ControlsPressedWhilePaused_AreNotCarriedIntoPlay()
        {
            var flow = Started();
            flow.Apply(Intent.Of(IntentKind.TogglePause, During(1)));
            flow.Apply(Intent.Down(Control.Fire, 1));
            flow.Apply(Intent.Of(IntentKind.TogglePause, 2));
            flow.Advance(2 + During(10));
            Assert.That(flow.Run!.Bullets, Is.Empty);
        }

        [Test]
        public void FrameTickBudget_SkipsLongGaps()
        {
            var flow = Started();
            flow.Advance(10);
            Assert.That(flow.Run!.Elapsed, Is.EqualTo(SiegeFlow.MaxTicksPerFrame));
        }

        [Test]
        public void EndOfRun_ShowsResult_AndRestartUsesNewSeed()
        {
            var flow = Started(Quiet(c => c.DurationTicks = 30));
            var first = flow.Run!;
            PlayUntil(flow, 31);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(first.Status, Is.EqualTo(RunStatus.Won));
            Assert.That(flow.TakeEvents() & FlowEvents.Won, Is.EqualTo(FlowEvents.Won));

            // 結果の画面でのキーと時間は、終わった試合を変えない。
            var hash = first.Hash();
            flow.Apply(Intent.Down(Control.Fire, 2));
            flow.Advance(5);
            Assert.That(first.Hash(), Is.EqualTo(hash));

            flow.Apply(Intent.Of(IntentKind.Submit, 6));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Run, Is.Not.SameAs(first));
            Assert.That(flow.Run!.Seed, Is.EqualTo(first.Seed + 1));
        }

        [Test]
        public void BackToTitle_ClearsRun_AndIsIgnoredWhilePlaying()
        {
            var flow = Started();
            flow.Apply(Intent.Of(IntentKind.BackToTitle, During(1)));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "プレイ中は効かない");
            flow.Apply(Intent.Of(IntentKind.TogglePause, During(2)));
            flow.Apply(Intent.Of(IntentKind.BackToTitle, During(3)));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Run, Is.Null);
        }

        [Test]
        public void Restart_FromPause_StartsFreshRun()
        {
            var flow = Started();
            var first = flow.Run!;
            flow.Apply(Intent.Of(IntentKind.TogglePause, During(5)));
            flow.Apply(Intent.Of(IntentKind.Restart, 1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Run, Is.Not.SameAs(first));
            Assert.That(flow.Run!.Elapsed, Is.Zero);
        }

        [Test]
        public void InvalidIntents_AreRejected()
        {
            Assert.That(() => Intent.Of(IntentKind.ControlDown, 0), Throws.ArgumentException);
            Assert.That(() => Intent.Of((IntentKind)99, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => Intent.Down((Control)99, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => Intent.Of(IntentKind.Submit, double.NaN), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new SiegeFlow(new SiegeConfig { Lives = 0 }, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Pilot_ReplacesKeysAndMouse()
        {
            var flow = Started(new SiegeConfig());
            flow.Pilot = new Autopilot();
            for (var i = 1; i <= 600; i++)
            {
                flow.StepTick();
                flow.EndFrame();
            }

            Assert.That(flow.Run!.Elapsed, Is.EqualTo(600));
            Assert.That(flow.Run.Kills, Is.GreaterThan(0), "自動操縦は敵を撃つ");
        }
    }
}
