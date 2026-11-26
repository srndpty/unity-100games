using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>画面遷移・連続移動・持ち物の画面。</summary>
    public sealed class FlowTests
    {
        private static CrawlFlow Playing(int seed = 1)
        {
            var flow = new CrawlFlow(new CrawlConfig(), seed);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            return flow;
        }

        /// <summary>自機を部屋の中央に置き、敵を除く。</summary>
        private static CrawlFlow Quiet(int seed = 1)
        {
            var flow = Playing(seed);
            var run = flow.Run!;
            run.ClearMonsters();
            for (int y = 0; y < run.Grid.Height; y++)
            {
                for (int x = 0; x < run.Grid.Width; x++) { run.SetItem(new Vec2i(x, y), null); }
            }

            foreach (var room in run.Grid.Rooms)
            {
                if (room.Width >= 5 && room.Height >= 3)
                {
                    run.Teleport(new Vec2i(room.X0 + 1, room.Center.Y));
                    break;
                }
            }

            return flow;
        }

        [Test]
        public void TitleHowToPlayResultTitleAndNewSeed()
        {
            var flow = new CrawlFlow(new CrawlConfig(), 10);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.View, Is.Null);
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.HowTo));
            flow.Apply(Intent.Of(IntentKind.Back, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Seed, Is.EqualTo(10));

            // 死ぬと結果画面。結果の後の操作は試合を変えない。
            flow.Run!.Player.Hp = 1;
            flow.Run.Player.Satiety = 0;
            flow.Apply(Intent.Down(Control.Wait, 1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            var hash = flow.Run.Hash();
            flow.Apply(Intent.Down(Control.Pad, 2, (int)Direction.East));
            flow.Apply(Intent.Of(IntentKind.ToggleInventory, 2));
            flow.Advance(10);
            Assert.That(flow.Run.Hash(), Is.EqualTo(hash));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));

            flow.Apply(Intent.Of(IntentKind.Submit, 3));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            flow.Apply(Intent.Of(IntentKind.Submit, 3));
            flow.Apply(Intent.Of(IntentKind.Submit, 3));
            Assert.That(flow.Seed, Is.EqualTo(11), "再戦は新しいシード");
            Assert.That(flow.RunNumber, Is.EqualTo(2));
        }

        [Test]
        public void PauseAndBackToTitle()
        {
            var flow = Playing();
            flow.Apply(Intent.Of(IntentKind.Back, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            var hash = flow.Run!.Hash();
            flow.Apply(Intent.Down(Control.Wait, 0));
            Assert.That(flow.Run.Hash(), Is.EqualTo(hash), "一時停止中は進まない");
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            flow.Apply(Intent.Of(IntentKind.Back, 0));
            flow.Apply(Intent.Of(IntentKind.BackToTitle, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.View, Is.Null);
        }

        [Test]
        public void HoldingADirectionRepeatsAfterTheDelayAndStopsOnRelease()
        {
            var flow = Quiet();
            var run = flow.Run!;
            var start = run.Player.Position;
            flow.Apply(Intent.Down(Control.Right, 1.0));
            Assert.That(run.Player.Position, Is.EqualTo(start + new Vec2i(1, 0)), "押してすぐ 1 歩");
            flow.Advance(1.0 + flow.RepeatDelay - 0.001);
            Assert.That(run.Player.Actions, Is.EqualTo(1), "待ちの前は歩かない");
            flow.Advance(1.0 + flow.RepeatDelay);
            Assert.That(run.Player.Actions, Is.EqualTo(2));
            flow.Advance(1.0 + flow.RepeatDelay + 10);
            Assert.That(run.Player.Actions, Is.EqualTo(3), "1 フレームで 1 歩まで");
            flow.Apply(Intent.Up(Control.Right, 12));
            flow.Advance(20);
            Assert.That(run.Player.Actions, Is.EqualTo(3));
        }

        [Test]
        public void RepeatHaltsWhenAMonsterComesIntoViewUntilPressedAgain()
        {
            var flow = Quiet(3);
            var run = flow.Run!;
            flow.Apply(Intent.Down(Control.Wait, 0));
            Assert.That(flow.RepeatHalted, Is.False);

            // 見えるマスに敵を置くと、次の連続の 1 歩で止まる。
            var spot = run.Player.Position;
            foreach (var room in run.Grid.Rooms)
            {
                if (room.Contains(spot)) { spot = new Vec2i(room.X1, room.Y0); }
            }

            var m = run.AddMonster(MonsterKind.Golem, spot);
            m.SleepTurns = 1000;
            flow.Advance(flow.RepeatDelay);
            Assert.That(flow.RepeatHalted, Is.True);
            var actions = run.Player.Actions;
            flow.Advance(5);
            Assert.That(run.Player.Actions, Is.EqualTo(actions), "止まったら押し直すまで歩かない");
            flow.Apply(Intent.Up(Control.Wait, 6));
            flow.Apply(Intent.Down(Control.Wait, 6));
            Assert.That(run.Player.Actions, Is.EqualTo(actions + 1));
        }

        [Test]
        public void WalkingIntoAWallHaltsTheRepeatWithoutUsingTime()
        {
            var flow = Quiet(4);
            var run = flow.Run!;
            flow.Apply(Intent.Down(Control.Left, 0));
            for (int i = 1; i < 40; i++) { flow.Advance(i); }
            Assert.That(flow.RepeatHalted, Is.True);
            Assert.That(run.Grid.CanStep(run.Player.Position, Direction.West), Is.False);
        }

        [Test]
        public void ShiftOnlyAllowsDiagonalsAndCtrlOnlyTurns()
        {
            var flow = Quiet(5);
            var run = flow.Run!;
            var start = run.Player.Position;
            flow.Apply(Intent.Down(Control.Diagonal, 0));
            flow.Apply(Intent.Down(Control.Down, 0));
            Assert.That(run.Player.Position, Is.EqualTo(start), "Shift と 1 キーでは動かない");
            flow.Apply(Intent.Down(Control.Right, 0));
            Assert.That(run.Player.Position, Is.EqualTo(start + new Vec2i(1, 1)));
            flow.Apply(Intent.Up(Control.Down, 0));
            flow.Apply(Intent.Up(Control.Right, 0));
            flow.Apply(Intent.Up(Control.Diagonal, 0));

            var actions = run.Player.Actions;
            flow.Apply(Intent.Down(Control.Turn, 1));
            flow.Apply(Intent.Down(Control.Pad, 1, (int)Direction.NorthWest));
            Assert.That(run.Player.Facing, Is.EqualTo(Direction.NorthWest));
            Assert.That(run.Player.Actions, Is.EqualTo(actions), "向きだけ変える");
            flow.Advance(10);
            Assert.That(run.Player.Actions, Is.EqualTo(actions), "Ctrl では連続移動しない");
        }

        [Test]
        public void ArrowsAndWasdAreHeldPerKey()
        {
            var flow = Quiet(6);
            var run = flow.Run!;
            flow.Apply(Intent.Down(Control.Right, 0, key: 0));
            flow.Apply(Intent.Down(Control.Right, 0.01, key: 1));
            flow.Apply(Intent.Up(Control.Right, 0.02, key: 0));
            var actions = run.Player.Actions;
            flow.Advance(flow.RepeatDelay + 0.1);
            Assert.That(run.Player.Actions, Is.GreaterThan(actions), "もう 1 つのキーを押している間は続く");
        }

        [Test]
        public void FocusLossPausesAndDropsHeldKeys()
        {
            var flow = Quiet(7);
            var run = flow.Run!;
            flow.Apply(Intent.Down(Control.Right, 0));
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            var actions = run.Player.Actions;
            flow.Apply(Intent.Of(IntentKind.Submit, 1));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間は操作を受けない");
            flow.SetFocus(true);
            flow.Apply(Intent.Of(IntentKind.Submit, 2));
            flow.Advance(5);
            Assert.That(run.Player.Actions, Is.EqualTo(actions), "押していたキーは捨てる");
        }

        [Test]
        public void InventoryScreenUsesThrowsAndDropsTheSelectedItem()
        {
            var flow = Quiet(8);
            var run = flow.Run!;
            run.Player.Inventory.Add(ItemKind.Stone);
            run.Player.Inventory.Add(ItemKind.Bread);
            run.Player.Satiety = 10;
            run.Refresh();
            flow.Apply(Intent.Of(IntentKind.ToggleInventory, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Inventory));
            flow.Apply(Intent.Of(IntentKind.InventoryUse, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Inventory), "石は使えないので閉じない");
            Assert.That(flow.Messages.Lines[flow.Messages.Lines.Count - 1], Does.StartWith("それは使えない"));
            flow.Apply(Intent.Move(1, 0));
            Assert.That(flow.Selected, Is.EqualTo(1));
            flow.Apply(Intent.Move(1, 0));
            Assert.That(flow.Selected, Is.EqualTo(0), "端で折り返す");
            flow.Apply(Intent.Move(-1, 0));
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            Assert.That(run.Player.Satiety, Is.EqualTo(10 + run.Config.BreadSatiety));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));

            flow.Apply(Intent.Of(IntentKind.ToggleInventory, 0));
            Assert.That(flow.Selected, Is.EqualTo(0), "選択を範囲に収める");
            flow.Apply(Intent.Of(IntentKind.InventoryDrop, 0));
            Assert.That(run.ItemAt(run.Player.Position), Is.EqualTo(ItemKind.Stone));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            flow.Apply(Intent.Of(IntentKind.ToggleInventory, 0));
            var hash = run.Hash();
            flow.Apply(Intent.Of(IntentKind.InventoryUse, 0));
            Assert.That(run.Hash(), Is.EqualTo(hash), "空の持ち物では何もしない");
        }

        [Test]
        public void SubmitOnStairsDescendsAndElsewhereExplains()
        {
            var flow = Quiet(9);
            var run = flow.Run!;
            flow.Apply(Intent.Of(IntentKind.Submit, 0));
            Assert.That(run.Floor, Is.EqualTo(1));
            Assert.That(flow.Messages.Lines[flow.Messages.Lines.Count - 1], Is.EqualTo(Names.Reason(RejectReason.NotOnStairs)));
            run.Teleport(run.Grid.Stairs);
            flow.Apply(Intent.Down(Control.Descend, 1));
            Assert.That(run.Floor, Is.EqualTo(2));
        }

        [Test]
        public void IntentsRejectBadValues()
        {
            Assert.That(() => Intent.Down(Control.Pad, 0, 8), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(() => Intent.Down((Control)99, 0), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(() => Intent.Of(IntentKind.Submit, double.NaN), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(() => Intent.Of(IntentKind.KeyDown, 0), Throws.ArgumentException);
            Assert.That(() => new CrawlFlow(new CrawlConfig(), 1, repeatDelay: double.PositiveInfinity), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }
    }
}
