using System;
using System.IO;
using System.Text;
using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class SnipFlowTests
    {
        private const double Tick = 1.0 / SnipFlow.TickRate;

        private static string DataText(string name)
        {
            foreach (var start in new[] { TestContext.CurrentContext.TestDirectory, Environment.CurrentDirectory })
            {
                for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                {
                    var candidate = Path.Combine(directory.FullName, "Assets", "Game", "Data", name);
                    if (File.Exists(candidate)) { return File.ReadAllText(candidate, Encoding.UTF8); }
                }
            }

            throw new FileNotFoundException($"Assets/Game/Data/{name} が見つかりません。");
        }

        private static PuzzleBook Book() => PuzzleBook.Production(DataText("Puzzles.txt"), new RopeConfig());

        private static SnipFlow Flow() => new(new RopeConfig(), Book());

        private static Vec2i P(int x, int y) => new(x * Board.DataUnit, y * Board.DataUnit);

        private static void Key(SnipFlow flow, IntentKind kind, double time = 0) => flow.Apply(Intent.Of(kind, time));

        private static void Pointer(SnipFlow flow, IntentKind kind, Vec2i point, double time = 0) => flow.Apply(Intent.Mouse(kind, point, time));

        /// <summary>タイトルから 1 問目を始める（時刻 0）。</summary>
        private static SnipFlow Playing()
        {
            var flow = Flow();
            Key(flow, IntentKind.Submit);
            Key(flow, IntentKind.Submit);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            return flow;
        }

        /// <summary>時計で <paramref name="ticks"/> Tick 進める（1 フレームの上限を守るよう 1 Tick ずつ）。</summary>
        private static double Advance(SnipFlow flow, double time, int ticks)
        {
            for (var i = 0; i < ticks; i++)
            {
                time += Tick;
                flow.Advance(time + 1e-9);
            }

            return time;
        }

        [Test]
        public void TitleToSelectToPlaying()
        {
            var flow = Flow();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Pointer(flow, IntentKind.PointerDown, P(10, 10));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Select));
            Key(flow, IntentKind.Submit);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Cursor, Is.Zero);
            Assert.That(flow.Run!.Puzzle, Is.SameAs(flow.Book.Puzzles[0]));
            Assert.That(flow.TakeEvents() & FlowEvents.Started, Is.EqualTo(FlowEvents.Started));
        }

        [Test]
        public void LockedPuzzlesCannotBeChosen()
        {
            var flow = Flow();
            Key(flow, IntentKind.Submit);
            flow.Apply(Intent.Choose(3, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Select), "未解放の問を選んでも始まらない");
            flow.Apply(Intent.Move(1, 0));
            flow.Apply(Intent.Move(SnipFlow.SelectColumns, 0));
            Assert.That(flow.Cursor, Is.Zero, "カーソルは解放済みの範囲から出ない");
            flow.Apply(Intent.Choose(-1, 0));
            flow.Apply(Intent.Choose(99, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Select));
        }

        [Test]
        public void SwipeCutsAtTheTickBoundaryOfItsTime()
        {
            var flow = Playing();
            var time = Advance(flow, 0, 10);
            Assert.That(flow.Run!.Ticks, Is.EqualTo(10));

            // 時刻 12.5 Tick の入力は、12 Tick 目を進めた後（13 Tick 目の前）の形で切る。
            Pointer(flow, IntentKind.PointerDown, P(110, 120), 12.5 * Tick);
            Assert.That(flow.Run.Ticks, Is.EqualTo(12));
            Pointer(flow, IntentKind.PointerMove, P(146, 120), 12.5 * Tick);
            Assert.That(flow.Run.Moves, Is.EqualTo(1));
            Assert.That(flow.Run.LastCutTick, Is.EqualTo(12));
            Assert.That(flow.TakeEvents() & FlowEvents.Cut, Is.EqualTo(FlowEvents.Cut));
            Assert.That(time, Is.LessThan(12.5 * Tick));
        }

        [Test]
        public void PressWithoutMoveDoesNotCut()
        {
            var flow = Playing();
            Pointer(flow, IntentKind.PointerDown, P(128, 120));
            Pointer(flow, IntentKind.PointerMove, P(128, 120));
            Pointer(flow, IntentKind.PointerUp, P(128, 120));
            Assert.That(flow.Run!.Moves, Is.Zero);
        }

        [Test]
        public void MoveWithoutPressDoesNotCut()
        {
            var flow = Playing();
            Pointer(flow, IntentKind.PointerMove, P(110, 120));
            Pointer(flow, IntentKind.PointerMove, P(146, 120));
            Assert.That(flow.Run!.Moves, Is.Zero);

            Pointer(flow, IntentKind.PointerDown, P(110, 130));
            Pointer(flow, IntentKind.PointerUp, P(110, 130));
            Pointer(flow, IntentKind.PointerMove, P(146, 130));
            Assert.That(flow.Run.Moves, Is.Zero, "離した後の移動では切らない");
        }

        [Test]
        public void FastSwipeAcrossInOneMoveCuts()
        {
            var flow = Playing();
            Pointer(flow, IntentKind.PointerDown, P(20, 110));
            Pointer(flow, IntentKind.PointerMove, P(240, 115));
            Assert.That(flow.Run!.Moves, Is.EqualTo(1), "1 回の移動で大きく横切っても切れる");
        }

        [Test]
        public void SwipeContinuesAcrossMoves()
        {
            var flow = Playing();
            Pointer(flow, IntentKind.PointerDown, P(100, 120));
            Pointer(flow, IntentKind.PointerMove, P(120, 120));
            Assert.That(flow.Run!.Moves, Is.Zero);
            Pointer(flow, IntentKind.PointerMove, P(140, 120));
            Assert.That(flow.Run.Moves, Is.EqualTo(1), "前の移動の点から続けて判定する");
            Pointer(flow, IntentKind.PointerMove, P(100, 120));
            Assert.That(flow.Run.Moves, Is.EqualTo(1), "切れたロープを戻ってなぞっても増えない");
        }

        [Test]
        public void RetryRestoresPuzzle()
        {
            var flow = Playing();
            var fresh = flow.Run!.Hash();
            Advance(flow, 0, 5);
            Pointer(flow, IntentKind.PointerDown, P(100, 120), 5 * Tick);
            Key(flow, IntentKind.Retry, 5 * Tick);
            Assert.That(flow.Run.Hash(), Is.EqualTo(fresh));
            Assert.That(flow.SwipePoint, Is.Null, "やり直しでなぞりを打ち切る");
            Pointer(flow, IntentKind.PointerMove, P(146, 120), 5 * Tick);
            Assert.That(flow.Run.Moves, Is.Zero);
        }

        [Test]
        public void FrameAdvancesAtMostLimitTicks()
        {
            var flow = Playing();
            flow.Advance(10.0);
            Assert.That(flow.Run!.Ticks, Is.EqualTo(SnipFlow.MaxTicksPerFrame));
        }

        [Test]
        public void PauseStopsTimeAndBackGoesToSelect()
        {
            var flow = Playing();
            Advance(flow, 0, 3);
            Key(flow, IntentKind.Cancel, 3 * Tick);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Advance(100 * Tick);
            Assert.That(flow.Run!.Ticks, Is.EqualTo(3));
            Key(flow, IntentKind.Cancel, 100 * Tick);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            flow.Advance(101.5 * Tick);
            Assert.That(flow.Run.Ticks, Is.EqualTo(4), "再開した時刻から数え直す");
            Key(flow, IntentKind.Cancel, 102 * Tick);
            Key(flow, IntentKind.Back, 102 * Tick);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Select));
            Assert.That(flow.Run, Is.Null);
        }

        [Test]
        public void FocusLossPausesAndDropsSwipe()
        {
            var flow = Playing();
            Pointer(flow, IntentKind.PointerDown, P(100, 120));
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.SwipePoint, Is.Null);
            Pointer(flow, IntentKind.PointerMove, P(146, 120));
            flow.SetFocus(true);
            Key(flow, IntentKind.Submit);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "復帰後は手動で再開する");
            Pointer(flow, IntentKind.PointerMove, P(146, 120));
            Assert.That(flow.Run!.Moves, Is.Zero, "フォーカスを失う前のなぞりは続かない");
        }

        [Test]
        public void ClickThatRefocusesIsDropped()
        {
            var flow = Flow();
            flow.SetFocus(false);
            flow.SetFocus(true);
            Pointer(flow, IntentKind.PointerDown, P(10, 10));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title), "ウィンドウを選び直したクリックは操作にしない");
            Pointer(flow, IntentKind.PointerDown, P(10, 10));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Select));
        }

        [Test]
        public void TileClickThatRefocusesIsDropped()
        {
            // 札のクリック（Choose）は押下の後、離したときに届く。押下から離すまでをまとめて捨てる。
            var flow = Flow();
            Key(flow, IntentKind.Submit);
            flow.SetFocus(false);
            flow.SetFocus(true);
            Pointer(flow, IntentKind.PointerDown, P(10, 10));
            flow.Apply(Intent.Choose(0, 0));
            Pointer(flow, IntentKind.PointerUp, P(10, 10));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Select));
            Pointer(flow, IntentKind.PointerDown, P(10, 10));
            flow.Apply(Intent.Choose(0, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "次のクリックは効く");
        }

        [Test]
        public void ClickBeforeFocusReturnIsIgnored()
        {
            // クリックが先に届き、その後でフォーカスが戻る順序：フォーカスのない間のクリックは捨てる。
            var flow = Flow();
            flow.SetFocus(false);
            Pointer(flow, IntentKind.PointerDown, P(10, 10));
            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [Test]
        public void SuccessUnlocksNextAndSubmitStartsIt()
        {
            var flow = Playing();
            Pointer(flow, IntentKind.PointerDown, P(110, 120));
            Pointer(flow, IntentKind.PointerMove, P(146, 120));
            var time = 0.0;
            for (var i = 0; i < 600 && flow.Screen == AppScreen.Playing; i++) { time = Advance(flow, time, 1); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Outcome));
            Assert.That(flow.Run!.Status, Is.EqualTo(RunStatus.Succeeded));
            Assert.That(flow.Progress.Unlocked, Is.EqualTo(2));
            Assert.That(flow.Progress.Best(0), Is.EqualTo(1));
            Assert.That(flow.IsNewBest, Is.True);

            var hash = flow.Run.Hash();
            Pointer(flow, IntentKind.PointerMove, P(100, 60), time);
            Assert.That(flow.Run.Hash(), Is.EqualTo(hash), "結果の後の操作は試合を変えない");

            Key(flow, IntentKind.Submit, time);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Cursor, Is.EqualTo(1));
        }

        [Test]
        public void PausedRetryRestartsPuzzle()
        {
            var flow = Playing();
            Advance(flow, 0, 5);
            Key(flow, IntentKind.Cancel, 5 * Tick);
            Key(flow, IntentKind.Retry, 5 * Tick);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Run!.Ticks, Is.Zero);
            Assert.That(flow.TakeEvents() & FlowEvents.Retried, Is.EqualTo(FlowEvents.Retried));
        }

        [Test]
        public void AllPuzzlesWithPilotReachResultAndReplay()
        {
            var flow = Flow();
            flow.Pilot = new Snipper(CutPlan.Parse(DataText("Solutions.txt")));
            Key(flow, IntentKind.Submit);
            Key(flow, IntentKind.Submit);
            for (var i = 0; i < flow.Book.Puzzles.Count; i++)
            {
                Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
                Assert.That(flow.Cursor, Is.EqualTo(i));
                for (var t = 0; t < 1800 && flow.Screen == AppScreen.Playing; t++) { flow.StepTick(); }
                Assert.That(flow.Run!.Status, Is.EqualTo(RunStatus.Succeeded), $"{i + 1} 問目");
                Assert.That(flow.Progress.Best(i), Is.EqualTo(flow.Book.Puzzles[i].Par));
                Key(flow, IntentKind.Submit);
            }

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Progress.AllSolved, Is.True);
            Key(flow, IntentKind.Submit);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Key(flow, IntentKind.Submit);
            flow.Apply(Intent.Choose(9, 0));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "再プレイでは解放済みの問を選べる");
            Assert.That(flow.Cursor, Is.EqualTo(9));
        }

        [Test]
        public void SpikeFailureThenSubmitRetries()
        {
            // 4 問目（トゲの上）で何も振らずに両方切ると、真下のトゲに落ちて失敗する。
            var flow = Flow();
            flow.Pilot = new Snipper(CutPlan.Parse("30:0\n0:0 30:1\n48:0\n0:0 0:1\n"));
            Key(flow, IntentKind.Submit);
            Key(flow, IntentKind.Submit);
            for (var i = 0; i < 3; i++)
            {
                for (var t = 0; t < 1800 && flow.Screen == AppScreen.Playing; t++) { flow.StepTick(); }
                Key(flow, IntentKind.Submit);
            }

            Assert.That(flow.Cursor, Is.EqualTo(3));
            for (var t = 0; t < 1800 && flow.Screen == AppScreen.Playing; t++) { flow.StepTick(); }
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Outcome));
            Assert.That(flow.Run!.Reason, Is.EqualTo(FailReason.Spike));
            Assert.That(flow.Progress.Unlocked, Is.EqualTo(4), "失敗では解放しない");
            Key(flow, IntentKind.Submit);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Cursor, Is.EqualTo(3));
            Assert.That(flow.Run.Ticks, Is.Zero);
        }

        [Test]
        public void ProgressRecordsBestAndRejectsLocked()
        {
            var progress = new Progress(3);
            Assert.Throws<InvalidOperationException>(() => progress.Record(1, 1));
            Assert.That(progress.Record(0, 3), Is.True);
            Assert.That(progress.Record(0, 4), Is.False);
            Assert.That(progress.Record(0, 2), Is.True);
            Assert.That(progress.Best(0), Is.EqualTo(2));
            Assert.That(progress.Unlocked, Is.EqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.Best(3));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Progress(0));
        }

        [Test]
        public void IntentRejectsInvalid()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Of(IntentKind.Submit, double.NaN));
            Assert.Throws<ArgumentException>(() => Intent.Of(IntentKind.PointerDown, 0));
            Assert.Throws<ArgumentException>(() => Intent.Mouse(IntentKind.Submit, Vec2i.Zero, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.Of((IntentKind)99, 0));
            var far = Intent.Mouse(IntentKind.PointerMove, new Vec2i(int.MaxValue, int.MinValue), 0);
            Assert.That(Board.AcceptsSwipePoint(far.Point), Is.True, "画面の外のマウスは範囲へ寄せる");
        }
    }
}
