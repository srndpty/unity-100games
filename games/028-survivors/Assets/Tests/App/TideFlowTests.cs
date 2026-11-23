using System;
using Game.Core;
using IntGeometry;
using NUnit.Framework;
using TileMotion;

namespace Game.App.Tests
{
    /// <summary>画面遷移・時刻付きの入力・レベルアップの 3 択の経路。</summary>
    public sealed class TideFlowTests
    {
        private const double Tick = 1.0 / Units.TickRate;

        /// <summary>敵が出ない設定。</summary>
        private static TideConfig Quiet(Action<TideConfig>? edit = null)
        {
            var config = new TideConfig();
            foreach (var stage in config.Stages) { stage.TargetAlive = 0; }
            edit?.Invoke(config);
            return config;
        }

        private static TideFlow Started(TideConfig? config = null, int seed = 1)
        {
            var flow = new TideFlow(config ?? Quiet(), seed);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            return flow;
        }

        /// <summary>Tick n の区間の中ほどの時刻。</summary>
        private static double During(int tick) => (tick - 0.5) * Tick;

        /// <summary>1 フレーム 1 Tick ずつ、Tick <paramref name="tick"/> の区間まで進める（フレームの Tick 上限に当たらない）。</summary>
        private static void PlayUntil(TideFlow flow, int tick)
        {
            for (var t = 1; t <= tick; t++) { flow.Advance(During(t)); }
        }

        /// <summary>次の Tick でレベルが上がるよう、自機の上に宝石を置いて 3 択を出す。出た時刻を返す。</summary>
        private static double RaiseLevel(TideFlow flow, int tick, int levels = 1)
        {
            var run = flow.Run!;
            var amount = 0;
            for (var k = 0; k < levels; k++) { amount += run.Config.ExperienceToNext(run.Level + k); }
            run.PlaceGem(run.Player, amount);
            flow.Advance(During(tick));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.LevelUp));
            return flow.ChoiceShownAt;
        }

        [Test]
        public void Title_SubmitStartsRun()
        {
            var flow = new TideFlow(Quiet(), 1);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Run, Is.Null);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.TakeEvents() & FlowEvents.Started, Is.EqualTo(FlowEvents.Started));
        }

        [Test]
        public void Ticks_FollowTheClock_AndKeysMoveThePlayer()
        {
            var flow = Started();
            var run = flow.Run!;
            flow.Apply(Intent.Down(Control.Right, During(1)));
            flow.Advance(During(4));
            Assert.That(run.Elapsed, Is.EqualTo(3), "Tick 4 の区間に入った時点では 3 Tick 進んでいる");
            Assert.That(run.Player.X, Is.EqualTo(run.Config.PlayerSpeed * 3));
        }

        [Test]
        public void OppositeKeys_UseLatestPressed()
        {
            var flow = Started();
            var run = flow.Run!;
            flow.Apply(Intent.Down(Control.Left, During(1)));
            flow.Apply(Intent.Down(Control.Right, During(1)));
            flow.Advance(During(2));
            Assert.That(run.Player.X, Is.EqualTo(run.Config.PlayerSpeed));
            flow.Apply(Intent.Up(Control.Right, During(2)));
            flow.Advance(During(3));
            Assert.That(run.Player.X, Is.Zero, "離すと、まだ押しているほうに戻る");
        }

        [Test]
        public void LevelUp_StopsTicksUntilChosen_AndTheClockRestartsFromTheChoice()
        {
            var flow = Started();
            var run = flow.Run!;
            var shown = RaiseLevel(flow, 2);
            Assert.That(run.Elapsed, Is.EqualTo(1));
            Assert.That(shown, Is.EqualTo(Tick).Within(1e-9), "3 択を出した Tick の終わりの時刻");
            Assert.That(flow.TakeEvents() & FlowEvents.LevelUp, Is.EqualTo(FlowEvents.LevelUp));

            flow.Advance(5);
            Assert.That(run.Elapsed, Is.EqualTo(1), "3 択の間は進まない");

            flow.Apply(Intent.Pick(1, 5));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(run.IsChoosing, Is.False);
            Assert.That(flow.TakeEvents() & FlowEvents.Chose, Is.EqualTo(FlowEvents.Chose));
            flow.Advance(5 + During(3));
            Assert.That(run.Elapsed, Is.EqualTo(3), "選んだ時刻から数え直す");
        }

        [Test]
        public void ChoiceRightAfterItAppears_IsIgnored()
        {
            var flow = Started();
            var run = flow.Run!;
            var shown = RaiseLevel(flow, 2);
            flow.Apply(Intent.Pick(0, shown + TideFlow.ChoiceGuardSeconds - 0.01));
            flow.Apply(Intent.Of(IntentKind.Submit, shown + TideFlow.ChoiceGuardSeconds - 0.001));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.LevelUp), "出たばかりの 3 択は選ばない");
            Assert.That(run.IsChoosing, Is.True);
            flow.Apply(Intent.Pick(0, shown + TideFlow.ChoiceGuardSeconds));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void SeveralLevels_ShowOneChoiceAfterAnother_AndADoubledPressDoesNotSkipOne()
        {
            var flow = Started();
            var run = flow.Run!;
            var shown = RaiseLevel(flow, 2, 2);
            Assert.That(run.PendingLevelUps, Is.EqualTo(2));
            var at = shown + 1;
            flow.Apply(Intent.Pick(0, at));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.LevelUp), "2 つ目の 3 択");
            flow.Apply(Intent.Pick(0, at + 0.01));
            Assert.That(run.PendingLevelUps, Is.EqualTo(1), "連打の 2 回目は次の 3 択を選ばない");
            flow.Apply(Intent.Of(IntentKind.Submit, at + 1));
            Assert.That(run.PendingLevelUps, Is.Zero);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void ArrowKeys_MoveTheHighlight_AndKeepTheHeldMovement()
        {
            var flow = Started();
            var run = flow.Run!;
            var shown = RaiseLevel(flow, 2);
            flow.Apply(Intent.Down(Control.Right, shown + 0.1));
            Assert.That(flow.Highlight, Is.EqualTo(1));
            flow.Apply(Intent.Down(Control.Up, shown + 0.2));
            flow.Apply(Intent.Up(Control.Up, shown + 0.25));
            Assert.That(flow.Highlight, Is.Zero);
            flow.Apply(Intent.Down(Control.Left, shown + 0.3));
            flow.Apply(Intent.Up(Control.Left, shown + 0.35));
            Assert.That(flow.Highlight, Is.EqualTo(run.ChoiceCount - 1), "端から回り込む");

            var expected = run.ChoiceAt(flow.Highlight);
            flow.Apply(Intent.Of(IntentKind.Submit, shown + 1));
            Assert.That(run.WeaponLevelOf(expected.Weapon), Is.EqualTo(expected.Level));

            // 3 択の間に押したままの右キーは、選んだ後も効く。
            var x = run.Player.X;
            flow.Advance(shown + 1 + During(2));
            Assert.That(run.Player.X, Is.EqualTo(x + run.Config.PlayerSpeed));
        }

        [Test]
        public void ChooseOutsideLevelUp_IsIgnored()
        {
            var flow = Started();
            var run = flow.Run!;
            flow.Apply(Intent.Pick(0, During(1)));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(run.WeaponLevelOf(1), Is.Zero);
            Assert.That(() => Intent.Pick(3, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => Intent.Of(IntentKind.Choose, 0), Throws.ArgumentException);
        }

        [Test]
        public void PauseDuringLevelUp_ReturnsToTheSameChoice()
        {
            var flow = Started();
            var run = flow.Run!;
            RaiseLevel(flow, 2);
            var first = run.ChoiceAt(0);
            flow.Apply(Intent.Of(IntentKind.TogglePause, 2));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Of(IntentKind.TogglePause, 3));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.LevelUp));
            Assert.That(run.ChoiceAt(0), Is.EqualTo(first));
        }

        [Test]
        public void Pause_StopsTicks_AndDropsHeldKeys()
        {
            var flow = Started();
            var run = flow.Run!;
            flow.Apply(Intent.Down(Control.Right, During(1)));
            flow.Apply(Intent.Of(IntentKind.TogglePause, During(3)));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            var position = run.Player;
            flow.Advance(During(100));
            Assert.That(run.Elapsed, Is.EqualTo(2));

            flow.Apply(Intent.Of(IntentKind.TogglePause, 10));
            flow.Advance(10 + During(5));
            Assert.That(run.Elapsed, Is.EqualTo(6));
            Assert.That(run.Player, Is.EqualTo(position), "一時停止の前に押したキーは持ち越さない");
        }

        [Test]
        public void FocusLoss_PausesAndIgnoresInputUntilBack()
        {
            var flow = Started();
            flow.Apply(Intent.Down(Control.Right, During(1)));
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Of(IntentKind.Submit, 1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスのない間の入力は捨てる");

            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
            var position = flow.Run!.Player;
            flow.Apply(Intent.Of(IntentKind.Submit, 2));
            flow.Advance(2 + During(10));
            Assert.That(flow.Run.Player, Is.EqualTo(position), "フォーカスを失う前の押下は消えている");
        }

        [Test]
        public void FocusLossDuringLevelUp_KeepsTheChoice_AndDropsKeys()
        {
            var flow = Started();
            var shown = RaiseLevel(flow, 2);
            flow.Apply(Intent.Down(Control.Right, shown + 0.1));
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.LevelUp));
            flow.Apply(Intent.Pick(0, shown + 1));
            Assert.That(flow.Run!.IsChoosing, Is.True, "フォーカスのない間の選択は捨てる");
            flow.SetFocus(true);
            flow.Apply(Intent.Pick(0, shown + 2));
            var x = flow.Run.Player.X;
            flow.Advance(shown + 2 + During(5));
            Assert.That(flow.Run.Player.X, Is.EqualTo(x));
        }

        [Test]
        public void FrameTickBudget_SkipsLongGaps()
        {
            var flow = Started();
            flow.Advance(10);
            Assert.That(flow.Run!.Elapsed, Is.EqualTo(TideFlow.MaxTicksPerFrame));
        }

        [Test]
        public void EndOfRun_ShowsResult_AndRestartUsesNewSeed()
        {
            var flow = Started(Quiet(c =>
            {
                c.DurationTicks = 10;
                c.BossTimeLimitTicks = 20;
                c.Enemies[3].ContactDamage = 0;
            }));
            var first = flow.Run!;
            PlayUntil(flow, 31);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(first.End, Is.EqualTo(RunEnd.TimeUp));
            Assert.That(flow.TakeEvents() & FlowEvents.Lost, Is.EqualTo(FlowEvents.Lost));

            // 結果の画面でのキーと時間は、終わった試合を変えない。
            var hash = first.Hash();
            flow.Apply(Intent.Down(Control.Right, 2));
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
            Assert.That(() => new TideFlow(new TideConfig { PlayerSpeed = 0 }, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Pilot_ReplacesKeys_AndChoosesWithoutTheLevelUpScreen()
        {
            var flow = Started(new TideConfig());
            flow.Pilot = new Autopilot();
            var leveled = false;
            for (var i = 1; i <= 1800; i++)
            {
                flow.StepTick();
                flow.EndFrame();
                Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
                leveled |= (flow.TakeEvents() & FlowEvents.Chose) != 0;
            }

            Assert.That(flow.Run!.Elapsed, Is.EqualTo(1800));
            Assert.That(flow.Run.Kills, Is.GreaterThan(0));
            Assert.That(leveled, Is.True, "操縦役が 3 択を選ぶ");
            Assert.That(flow.Run.Player, Is.Not.EqualTo(Vec2i.Zero), "操縦役が動かす");
        }
    }
}
