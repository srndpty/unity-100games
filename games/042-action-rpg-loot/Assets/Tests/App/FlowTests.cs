using System;
using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class FlowTests
    {
        private const double Tick = 1.0 / 60;

        [Test]
        public void TitleHowToPlayResultTitle()
        {
            var flow = AppLab.NewFlow();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.HowTo));
            flow.Apply(Intent.Of(IntentKind.Back, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title), "操作説明から戻る");
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Run, Is.Not.Null);
            Assert.That(flow.RunNumber, Is.EqualTo(1));

            flow.Pilot = new Autopilot();
            for (int i = 0; i < 40000 && flow.Screen == AppScreen.Playing; i++) { flow.StepTick(); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Run!.IsOver, Is.True);

            // 結果の後の操作は試合を変えない。
            uint hash = flow.Run.Hash();
            flow.StepTick();
            flow.Apply(Intent.Down(Control.Attack, 1));
            flow.Apply(Intent.With(IntentKind.Equip, 0, 1));
            flow.Apply(Intent.With(IntentKind.PickGround, 1, 1));
            flow.Apply(Intent.Of(IntentKind.ToggleInventory, 1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Run.Hash(), Is.EqualTo(hash));

            flow.Apply(Intent.Of(IntentKind.Submit, 2));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Run, Is.Null);
        }

        [Test]
        public void EachRunUsesANewSeed()
        {
            var flow = AppLab.NewFlow(firstSeed: 7);
            Begin(flow, 0);
            int first = flow.Run!.Seed;
            flow.Apply(Intent.Of(IntentKind.Back, 0));
            flow.Apply(Intent.Of(IntentKind.BackToTitle, 0));
            Begin(flow, 0);
            Assert.That(flow.Run!.Seed, Is.EqualTo(first + 1));
            Assert.That(flow.RunNumber, Is.EqualTo(2));
            Assert.That(flow.Run.Tick, Is.EqualTo(0), "新しい試合は 0 から");
        }

        [Test]
        public void TimeMapsToTicksAndFrameBudgetCapsCatchUp()
        {
            var flow = AppLab.NewFlow();
            Begin(flow, 10);
            flow.Advance(10 + (3 * Tick) + 0.001);
            Assert.That(flow.Run!.Tick, Is.EqualTo(3));
            flow.Advance(10 + 100);
            Assert.That(flow.Run.Tick, Is.EqualTo(3 + DelveFlow.MaxTicksPerFrame), "処理落ちでは 1 フレーム 5 Tick まで");
        }

        [Test]
        public void InventoryAndPauseStopTicksAndResumeFromNow()
        {
            var flow = AppLab.NewFlow();
            Begin(flow, 0);
            Frames(flow, 0, 10 * Tick + 0.001);
            Assert.That(flow.Run!.Tick, Is.EqualTo(10));
            flow.Apply(Intent.Of(IntentKind.ToggleInventory, (10 * Tick) + 0.002));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Inventory));
            flow.Advance(100);
            Assert.That(flow.Run.Tick, Is.EqualTo(10), "持ち物の画面の間は進まない");
            flow.Apply(Intent.Of(IntentKind.ToggleInventory, 100));
            flow.Advance(100 + (2 * Tick) + 0.001);
            Assert.That(flow.Run.Tick, Is.EqualTo(12), "閉じた時刻から数え直す");

            flow.Apply(Intent.Of(IntentKind.Back, 100 + (2 * Tick) + 0.002));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Advance(200);
            Assert.That(flow.Run.Tick, Is.EqualTo(12));
            flow.Apply(Intent.Of(IntentKind.Back, 200));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void InventoryIntentsOnlyWorkOnTheInventoryScreen()
        {
            var flow = AppLab.NewFlow();
            Begin(flow, 0);
            var run = flow.Run!;
            flow.Apply(Intent.With(IntentKind.Unequip, (int)Slot.Weapon, 0));
            Assert.That(run.EquippedAt(Slot.Weapon), Is.Not.Null, "プレイ中は外さない");

            flow.Apply(Intent.Of(IntentKind.ToggleInventory, 0));
            flow.Apply(Intent.With(IntentKind.Unequip, (int)Slot.Weapon, 0));
            Assert.That(run.EquippedAt(Slot.Weapon), Is.Null);
            Assert.That(run.Inventory.Count, Is.EqualTo(1));
            flow.Apply(Intent.With(IntentKind.Equip, 0, 0));
            Assert.That(run.EquippedAt(Slot.Weapon), Is.Not.Null);
            flow.Apply(Intent.With(IntentKind.Equip, 5, 0));
            Assert.That(flow.LastItemResult, Is.EqualTo(ItemResult.NotFound));
            flow.Apply(Intent.With(IntentKind.Equip, long.MaxValue, 0));
            Assert.That(flow.LastItemResult, Is.EqualTo(ItemResult.NotFound), "範囲外の番号");
            flow.Apply(Intent.With(IntentKind.Unequip, 99, 0));
            Assert.That(run.EquippedAt(Slot.Weapon), Is.Not.Null, "未定義の枠は無視");
        }

        [Test]
        public void ShortClickStillSwingsOnceAndHeldAttackRepeats()
        {
            var flow = AppLab.NewFlow();
            Begin(flow, 0);
            var run = flow.Run!;
            flow.SetAim(run.Hero.Position + new Vec2i(256, 0));
            flow.Apply(Intent.Down(Control.Attack, 0.001));
            flow.Apply(Intent.Up(Control.Attack, 0.002));
            flow.Advance(Tick + 0.001);
            Assert.That((flow.TakeEvents() & RunEvents.Swing) != 0, Is.True, "同じ Tick の中で押して離しても振る");
            Assert.That(run.Hero.SwingStartTick, Is.EqualTo(1));
            flow.Advance(60 * Tick);
            Assert.That(run.Hero.SwingStartTick, Is.EqualTo(1), "離した後は続けない");

            flow.Apply(Intent.Down(Control.Attack, 60 * Tick + 0.0001));
            flow.Advance(61 * Tick + 0.001);
            long first = run.Hero.SwingStartTick;
            flow.EndFrame();
            for (int i = 0; i < run.AttackInterval; i++) { flow.StepTick(); }
            Assert.That(run.Hero.SwingStartTick, Is.EqualTo(first + run.AttackInterval), "押したままで続ける");
        }

        [Test]
        public void SkillPressFiresOnceEvenWhileHeld()
        {
            var flow = AppLab.NewFlow(config: new DelveConfig { SkillCooldown = 5 });
            Begin(flow, 0);
            var run = flow.Run!;
            flow.Apply(Intent.Down(Control.Skill, 0.001));
            for (int i = 0; i < 30; i++) { flow.StepTick(); }
            Assert.That(run.Hero.SpinStartTick, Is.EqualTo(1), "押したままでも 1 回だけ");
        }

        [Test]
        public void FocusLossPausesAndDropsHeldKeys()
        {
            var flow = AppLab.NewFlow();
            Begin(flow, 0);
            var run = flow.Run!;
            var start = run.Hero.Position;
            flow.Apply(Intent.Down(Control.Right, 0.001));
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Of(IntentKind.Back, 1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間の入力は捨てる");
            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
            flow.Apply(Intent.Of(IntentKind.Back, 2));
            flow.Advance(2 + (30 * Tick));
            Assert.That(run.Hero.Position, Is.EqualTo(start), "押していたキーは持ち越さない");
        }

        [Test]
        public void MovementKeysAreHeldPerPhysicalKey()
        {
            // D（0 番）を押したまま一時停止して再開し、右矢印（1 番）を押すと右へ動く。D を離しても右矢印で動き続ける。
            var flow = AppLab.NewFlow();
            Begin(flow, 0);
            var run = flow.Run!;
            flow.Apply(Intent.Down(Control.Right, 0.001, 0));
            flow.Apply(Intent.Of(IntentKind.Back, 0.002));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Of(IntentKind.Back, 0.003));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            flow.Apply(Intent.Down(Control.Right, 0.004, 1));
            var start = run.Hero.Position;
            Frames(flow, 0.004, 0.004 + (10 * Tick));
            Assert.That(run.Hero.Position.X, Is.GreaterThan(start.X), "別のキーで押し直すと動く");
            flow.Apply(Intent.Up(Control.Right, 0.004 + (10 * Tick), 0));
            var mid = run.Hero.Position;
            Frames(flow, 0.004 + (10 * Tick), 0.004 + (20 * Tick));
            Assert.That(run.Hero.Position.X, Is.GreaterThan(mid.X), "一時停止の前に押した D を離しても、右矢印で動き続ける");
            flow.Apply(Intent.Up(Control.Right, 0.004 + (20 * Tick), 1));
            Frames(flow, 0.004 + (20 * Tick), 0.004 + (22 * Tick));
            var end = run.Hero.Position;
            Frames(flow, 0.004 + (22 * Tick), 0.004 + (30 * Tick));
            Assert.That(run.Hero.Position, Is.EqualTo(end), "両方離すと止まる");
        }

        [Test]
        public void HeldControlsKeepEachPhysicalKey()
        {
            var held = new HeldControls();
            held.Press(Control.Left, 0);
            held.Press(Control.Left, 1);
            held.Release(Control.Left, 0);
            Assert.That(held.Horizontal, Is.EqualTo(-1), "もう 1 本のキーを押している");
            held.Press(Control.Right, 0);
            Assert.That(held.Horizontal, Is.EqualTo(1), "後から押したキーの向き");
            held.Release(Control.Right, 0);
            Assert.That(held.Horizontal, Is.EqualTo(-1));
            held.Release(Control.Left, 1);
            Assert.That(held.Horizontal, Is.EqualTo(0));
            held.Press(Control.Attack, 0);
            held.Press(Control.Attack, 1);
            held.Release(Control.Attack, 0);
            held.Take();
            Assert.That(held.Take().Attack, Is.True, "もう 1 本で押したまま");
            held.Release(Control.Attack, 1);
            Assert.That(held.Take().Attack, Is.False);
        }

        [Test]
        public void PickGroundOnlyWhilePlaying()
        {
            var flow = AppLab.NewFlow();
            Begin(flow, 0);
            flow.Apply(Intent.Of(IntentKind.Back, 0));
            flow.Apply(Intent.With(IntentKind.PickGround, 1, 0));
            Assert.That(flow.LastItemResult, Is.EqualTo(ItemResult.Done), "一時停止中は拾わない（結果も変わらない）");
            flow.Apply(Intent.Of(IntentKind.Back, 0));
            flow.Apply(Intent.With(IntentKind.PickGround, 12345, 0));
            Assert.That(flow.LastItemResult, Is.EqualTo(ItemResult.NotFound));
        }

        [Test]
        public void RejectsMalformedIntents()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Of(IntentKind.Submit, double.NaN));
            Assert.Throws<ArgumentException>(() => Intent.Of(IntentKind.ControlDown, 0));
            Assert.Throws<ArgumentException>(() => Intent.With(IntentKind.Submit, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Down((Control)99, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Down(Control.Left, 0, Intent.MaxKey + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Up(Control.Left, 0, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Of((IntentKind)99, 0));
        }

        /// <summary>1 Tick ごとのフレームで時刻を進める（1 フレームの上限に掛からない）。</summary>
        private static void Frames(DelveFlow flow, double from, double to)
        {
            for (double t = from; t < to; t += Tick) { flow.Advance(t); }
            flow.Advance(to);
        }

        private static void Begin(DelveFlow flow, double time)
        {
            flow.Apply(Intent.Of(IntentKind.Submit, time));
            flow.Apply(Intent.Of(IntentKind.Submit, time));
        }
    }
}
