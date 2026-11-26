using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    /// <summary>画面の遷移・入力の扱い・セーブの読み書き・再現性。</summary>
    public sealed class FlowTests
    {
        /// <summary>シード 1 の操縦役の通しで、結果画面に着いたときの世界のハッシュ（Mono と .NET で同じ）。</summary>
        public const uint SeedOneClearHash = 1241441768u;

        private static void Confirm(KeeperFlow flow)
        {
            flow.Tap(InputButton.Confirm);
            flow.StepTicks(1);
        }

        [Test]
        public void TitleToStarterToField()
        {
            var flow = AppLab.Flow();
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(flow.HasSave, Is.False);
            Assert.That(flow.Items.Count, Is.EqualTo(2), "セーブがなければ つづきから は出ない");
            Confirm(flow);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.StarterPick));
            flow.Tap(InputButton.Down);
            Confirm(flow);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Field));
            Assert.That(flow.InDialogue, Is.True);
            Assert.That(flow.World!.Roster.Party[0].Species.Id, Is.EqualTo("ripplet"));
            Confirm(flow);
            Confirm(flow);
            Assert.That(flow.InDialogue, Is.False);
        }

        [Test]
        public void HowToOpensAndCloses()
        {
            var flow = AppLab.Flow();
            flow.Tap(InputButton.Up);
            Confirm(flow);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.HowTo));
            flow.Tap(InputButton.Cancel);
            flow.StepTicks(1);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Title));
        }

        [Test]
        public void TappingADirectionWalksOneStep_EvenIfReleasedInTheSameFrame()
        {
            var flow = AppLab.FieldReady();
            var x = flow.World!.Explorer.X;
            flow.Tap(InputButton.Left);
            flow.StepTicks(30);
            Assert.That(flow.World.Explorer.X, Is.EqualTo(x - 1));
            Assert.That(flow.World.Explorer.IsMoving, Is.False, "離したら止まる");
        }

        [Test]
        public void HoldingKeepsWalking_AndFocusLossPausesAndDropsHeldKeys()
        {
            var flow = AppLab.FieldReady();
            var x = flow.World!.Explorer.X;
            flow.Press(InputButton.Left, 1);
            flow.StepTicks(Lab8 * 3);
            Assert.That(flow.World.Explorer.X, Is.LessThan(x - 1));
            flow.OnFocusChanged(false);
            Assert.That(flow.IsPaused, Is.True);
            Assert.That(flow.HeldDirection, Is.Null);
            var stop = flow.World.Explorer.X;
            var ticks = flow.World.PlayTicks;
            flow.StepTicks(60);
            Assert.That(flow.World.PlayTicks, Is.EqualTo(ticks), "一時停止の間は時間も進まない");
            flow.OnFocusChanged(true);
            flow.StepTicks(30);
            Assert.That(flow.IsPaused, Is.True, "復帰しても手で再開する");
            Confirm(flow);
            Assert.That(flow.IsPaused, Is.False);
            flow.StepTicks(30);
            Assert.That(flow.World.Explorer.X, Is.InRange(stop - 1, stop), "押したままの扱いは消えている");
        }

        private const int Lab8 = 8;

        [Test]
        public void DoubleConfirmOnTheBallUsesOnlyOne()
        {
            var flow = AppLab.FieldReady();
            var w = flow.World!;
            w.StartWildForTest(Monster.Create(AppLab.Data.SpeciesOf("brinecrab"), 3, AppLab.Data.Rules));
            EnterBattleScreen(flow);
            var balls = w.Battle!.Balls;
            Assert.That(flow.BattleMenu, Is.EqualTo(BattleMenu.Root));
            flow.Tap(InputButton.Down);
            flow.Tap(InputButton.Down);
            Confirm(flow);
            Assert.That(flow.BattleMenu, Is.EqualTo(BattleMenu.Items));
            flow.Tap(InputButton.Down);
            flow.Tap(InputButton.Confirm);
            flow.Tap(InputButton.Confirm);
            flow.Tap(InputButton.Confirm);
            flow.StepTicks(1);
            Assert.That(w.Battle!.Balls, Is.EqualTo(balls - 1), "重なった決定は文送りになり、2 個目を投げない");
        }

        /// <summary>テスト用：世界で始めた戦闘を画面にも出す（草むらを歩かずに戦闘の画面を確かめる）。</summary>
        private static void EnterBattleScreen(KeeperFlow flow)
        {
            flow.EnterBattleForTest();
            for (int i = 0; i < 2000 && flow.BattleMenu == BattleMenu.Playing; i++) { flow.StepTicks(1); }
        }

        [Test]
        public void TrainerBattleRejectsRunAndBallWithANotice()
        {
            var flow = AppLab.FieldReady();
            var w = flow.World!;
            w.WarpForTest("route", 11, 7, Direction.Right);
            Confirm(flow);
            Assert.That(flow.InDialogue, Is.True);
            Confirm(flow);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Battle));
            for (int i = 0; i < 2000 && flow.BattleMenu == BattleMenu.Playing; i++) { flow.StepTicks(1); }
            flow.Tap(InputButton.Up);
            var hash = w.Battle!.Hash();
            Confirm(flow);
            Assert.That(flow.Notice.Contains("にげられない", System.StringComparison.Ordinal), Is.True, flow.Notice);
            Assert.That(w.Battle.Hash(), Is.EqualTo(hash));
            Assert.That(flow.Cues.Reject, Is.GreaterThan(0));
        }

        [Test]
        public void SaveAtTheCenterThenContinueFromTheTitle()
        {
            var save = new MemorySaveStore();
            var flow = AppLab.FieldReady(1, save);
            var w = flow.World!;
            w.WarpForTest("town", 10, 4, Direction.Up);
            Confirm(flow);
            Assert.That(flow.InDialogue, Is.True);
            Confirm(flow);
            Confirm(flow);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.CenterMenu));
            Confirm(flow);
            Assert.That(save.Content, Is.Not.Null);
            Assert.That(flow.Cues.Save, Is.EqualTo(1));
            var hash = w.Hash();

            var again = AppLab.Flow(2, save);
            Assert.That(again.HasSave, Is.True);
            Assert.That(again.Items[again.Cursor].Label, Is.EqualTo("つづきから"));
            Confirm(again);
            Assert.That(again.Screen, Is.EqualTo(FlowScreen.Field));
            Assert.That(again.World!.Hash(), Is.EqualTo(hash));
        }

        [Test]
        public void BrokenSaveShowsANoticeAndNoContinue()
        {
            var save = new MemorySaveStore();
            save.TryWrite("THORNWILD-SAVE\nversion=1\n", out _);
            var flow = AppLab.Flow(1, save);
            Assert.That(flow.HasSave, Is.False);
            Assert.That(flow.TitleNotice.Contains("読み込めません", System.StringComparison.Ordinal), Is.True, flow.TitleNotice);
            Assert.That(flow.Items.Count, Is.EqualTo(2));

            var failing = new MemorySaveStore { ReadFailure = "アクセスが拒否されました" };
            Assert.That(AppLab.Flow(1, failing).TitleNotice.Contains("アクセスが拒否されました", System.StringComparison.Ordinal), Is.True, AppLab.Flow(1, failing).TitleNotice);
        }

        [Test]
        public void WriteFailureIsReportedNotThrown()
        {
            var save = new MemorySaveStore { WriteFailure = "ディスクがいっぱいです" };
            var flow = AppLab.FieldReady(1, save);
            flow.World!.WarpForTest("town", 10, 4, Direction.Up);
            for (int i = 0; i < 3; i++) { Confirm(flow); }
            Confirm(flow);
            Assert.That(flow.Dialogue.Contains("ディスクがいっぱいです", System.StringComparison.Ordinal), Is.True, flow.Dialogue);
        }

        [Test]
        public void ClearingShowsTheResultSavesRecords_AndRematchUsesANewSeed()
        {
            var records = new MemorySaveStore();
            var flow = AppLab.Flow(1, records: records);
            var pilot = new Autopilot();
            AppLab.RunUntil(flow, pilot, f => f.Screen == FlowScreen.Result, AutopilotTests.MaxTicks);
            var first = flow.World!;
            Assert.That(first.Hash(), Is.EqualTo(SeedOneClearHash), "シード 1 の通しは Mono と .NET で同じ");
            Assert.That(flow.Records.Clears, Is.EqualTo(1));
            Assert.That(records.Content!.Contains("clears=1", System.StringComparison.Ordinal), Is.True, records.Content);

            // 結果画面では決定以外は効かない。
            flow.Tap(InputButton.Cancel);
            flow.Tap(InputButton.Left);
            flow.StepTicks(5);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Result));
            Confirm(flow);
            Assert.That(flow.Screen, Is.EqualTo(FlowScreen.Title));
            Assert.That(flow.Records.Clears, Is.EqualTo(1), "タイトルで記録を読み直す");
            Confirm(flow);
            Assert.That(flow.World!.Seed, Is.Not.EqualTo(first.Seed), "再戦は新しいシード");
        }

        [Test]
        public void ContinuingFromASaveGivesTheSameFuture()
        {
            var save = new MemorySaveStore();
            var flow = AppLab.Flow(4, save);
            var pilot = new Autopilot { SaveAtCenter = true };
            AppLab.RunUntil(flow, pilot, _ => save.Content != null, AutopilotTests.MaxTicks);
            AppLab.RunUntil(flow, pilot, f => f.Screen == FlowScreen.Field && !f.InDialogue, 2000);
            var a = AppLab.Flow(9, save);
            var b = AppLab.Flow(9, save);
            Confirm(a);
            Confirm(b);
            var pa = new Autopilot();
            var pb = new Autopilot();
            for (int i = 0; i < 20000; i++)
            {
                pa.Step(a);
                pb.Step(b);
                a.StepTicks(1);
                b.StepTicks(1);
            }

            Assert.That(a.World!.Hash(), Is.EqualTo(b.World!.Hash()));
            Assert.That(a.World.BattleCount, Is.GreaterThan(0));
        }
    }
}
