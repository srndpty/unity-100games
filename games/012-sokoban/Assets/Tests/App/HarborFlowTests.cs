using System;
using System.Collections.Generic;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class HarborFlowTests
    {
        private const float Delay = .2f;
        private const float Interval = .1f;
        private const float Frame = .016f;
        private const float LongFrame = Delay + .05f;

        // 右へ2回押すとクリアできる面。
        private static readonly string[] _quickLevel =
        {
            "#######",
            "#     #",
            "# @$ .#",
            "#     #",
            "#######",
        };

        private static HarborFlow Flow(int levels = 7)
        {
            var maps = new List<LevelMap>();
            for (var i = 0; i < levels; i++) { maps.Add(LevelParser.Parse(_quickLevel)); }
            return new HarborFlow(maps, Delay, Interval);
        }

        private static HarborFlow Playing(int level = 0, int levels = 7)
        {
            var flow = Flow(levels);
            flow.Submit();
            Assert.That(flow.SelectLevel(level), Is.True);
            flow.TakeEvents();
            return flow;
        }

        // キーを押したフレームの Tick を済ませてから、リピートに十分な時間を進める。
        private static void ElapseRepeat(HarborFlow flow)
        {
            flow.Tick(Frame);
            flow.Tick(Delay + 1f);
        }

        private static void Clear(HarborFlow flow)
        {
            flow.Move(Direction.Right);
            Assert.That(flow.Move(Direction.Right), Is.EqualTo(MoveResult.Cleared));
        }

        [Test]
        public void Constructor_RejectsMissingLevels()
        {
            Assert.Throws<ArgumentNullException>(() => new HarborFlow(null!, Delay, Interval));
            Assert.Throws<ArgumentException>(() => new HarborFlow(new List<LevelMap>(), Delay, Interval));
        }

        [TestCase(0f, Interval)]
        [TestCase(Delay, 0f)]
        [TestCase(float.NaN, Interval)]
        [TestCase(Delay, float.PositiveInfinity)]
        public void Constructor_RejectsInvalidRepeatTimes(float delay, float interval) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new HarborFlow(LevelCatalog.Maps, delay, interval));

        [Test]
        public void Screens_FollowTitleSelectPlayClear()
        {
            var flow = Flow();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.LevelSelect));
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game, Is.Not.Null);
            Clear(flow);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Cleared));
        }

        [Test]
        public void Back_StepsToThePreviousScreen()
        {
            var flow = Playing();
            flow.Back();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.LevelSelect));
            Assert.That(flow.Game, Is.Null);
            flow.Back();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            flow.Back();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [Test]
        public void Submit_WhilePlayingDoesNothing()
        {
            var flow = Playing();
            var game = flow.Game;
            flow.Move(Direction.Up);
            flow.Submit();
            Assert.That(flow.Game, Is.SameAs(game));
            Assert.That(flow.Game!.MoveCount, Is.EqualTo(1));
        }

        [TestCase(-1)]
        [TestCase(7)]
        public void SelectLevel_RejectsOutOfRange(int level)
        {
            var flow = Flow();
            flow.Submit();
            Assert.That(flow.SelectLevel(level), Is.False);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.LevelSelect));
        }

        [Test]
        public void SelectLevel_OnlyWorksOnTheSelectScreen()
        {
            var flow = Flow();
            Assert.That(flow.SelectLevel(0), Is.False);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [Test]
        public void Operations_OutsidePlayAreRejected()
        {
            var flow = Flow();
            Assert.That(flow.Move(Direction.Right), Is.EqualTo(MoveResult.Rejected));
            Assert.That(flow.Undo(), Is.False);
            Assert.That(flow.Restart(), Is.False);
            flow.Submit();
            Assert.That(flow.Move(Direction.Right), Is.EqualTo(MoveResult.Rejected));
            Assert.That(flow.Undo(), Is.False);
            Assert.That(flow.Restart(), Is.False);
        }

        [Test]
        public void AfterClear_MoveAndUndoAreRejected()
        {
            var flow = Playing();
            Clear(flow);
            Assert.That(flow.Move(Direction.Left), Is.EqualTo(MoveResult.Rejected));
            Assert.That(flow.Undo(), Is.False);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Cleared));
        }

        [Test]
        public void Submit_AfterClearStartsTheNextLevel()
        {
            var flow = Playing(2);
            Clear(flow);
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.SelectedLevel, Is.EqualTo(3));
            Assert.That(flow.Game!.MoveCount, Is.Zero);
        }

        [Test]
        public void Submit_AfterTheLastLevelReturnsToSelect()
        {
            var flow = Playing(6);
            Assert.That(flow.HasNextLevel, Is.False);
            Clear(flow);
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.LevelSelect));
            Assert.That(flow.Game, Is.Null);
        }

        [Test]
        public void Restart_AfterClearPlaysTheSameLevelAgain()
        {
            var flow = Playing(1);
            Clear(flow);
            Assert.That(flow.Restart(), Is.True);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.SelectedLevel, Is.EqualTo(1));
            Assert.That(flow.Game!.Board, Is.EqualTo(flow.Game.Map.Initial));
        }

        [Test]
        public void Restart_WhilePlayingReturnsToTheInitialBoard()
        {
            var flow = Playing();
            flow.Move(Direction.Right);
            Assert.That(flow.Restart(), Is.True);
            Assert.That(flow.Game!.Board, Is.EqualTo(flow.Game.Map.Initial));
            Assert.That(flow.TakeEvents() & FlowEvents.Restarted, Is.EqualTo(FlowEvents.Restarted));
        }

        [Test]
        public void Records_KeepTheFewestMoves()
        {
            var flow = Playing();
            flow.Move(Direction.Up);
            flow.Move(Direction.Down);
            Clear(flow);
            Assert.That(flow.Records.BestMoves(0), Is.EqualTo(4));

            flow.Restart();
            Clear(flow);
            Assert.That(flow.Records.BestMoves(0), Is.EqualTo(2));

            flow.Restart();
            flow.Move(Direction.Up);
            flow.Move(Direction.Down);
            Clear(flow);
            Assert.That(flow.Records.BestMoves(0), Is.EqualTo(2), "悪い記録で上書きしない");
            Assert.That(flow.Records.ClearedCount, Is.EqualTo(1), "同じ面は1回だけ数える");
        }

        [Test]
        public void Records_ReportAllCleared()
        {
            var flow = Playing(0, 2);
            Clear(flow);
            Assert.That(flow.Records.IsAllCleared, Is.False);
            flow.Submit();
            Clear(flow);
            Assert.That(flow.Records.IsAllCleared, Is.True);
            Assert.That(flow.Records.IsCleared(1), Is.True);
        }

        [Test]
        public void Records_RejectNonPositiveMoves() =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new SessionRecords(1).Record(0, 0));

        [Test]
        public void Events_DescribeWhatHappened()
        {
            var flow = Playing();
            flow.Move(Direction.Right);
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Moved | FlowEvents.Pushed));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None));

            flow.Undo();
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Undone));

            Clear(flow);
            var events = flow.TakeEvents();
            Assert.That(events & FlowEvents.BoxPlaced, Is.EqualTo(FlowEvents.BoxPlaced));
            Assert.That(events & FlowEvents.Cleared, Is.EqualTo(FlowEvents.Cleared));
        }

        [Test]
        public void RejectedMove_RaisesNoEvents()
        {
            var flow = Playing();
            flow.Move(Direction.Up);
            flow.Move(Direction.Up);
            flow.TakeEvents();
            Assert.That(flow.Move(Direction.Up), Is.EqualTo(MoveResult.Rejected));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None));
        }

        [Test]
        public void Cursor_MovesInAFiveColumnGridAndStopsAtTheEdges()
        {
            var flow = Flow();
            flow.Submit();
            flow.PressDirection(Direction.Left);
            flow.PressDirection(Direction.Up);
            Assert.That(flow.SelectedLevel, Is.Zero);

            flow.PressDirection(Direction.Right);
            Assert.That(flow.SelectedLevel, Is.EqualTo(1));
            flow.PressDirection(Direction.Down);
            Assert.That(flow.SelectedLevel, Is.EqualTo(6));
            flow.PressDirection(Direction.Right);
            Assert.That(flow.SelectedLevel, Is.EqualTo(6), "7問しかないので右には進めない");
            flow.PressDirection(Direction.Down);
            Assert.That(flow.SelectedLevel, Is.EqualTo(6));
        }

        [Test]
        public void Cursor_DoesNotWrapToTheNextRow()
        {
            var flow = Flow();
            flow.Submit();
            for (var i = 0; i < 6; i++) { flow.PressDirection(Direction.Right); }
            Assert.That(flow.SelectedLevel, Is.EqualTo(HarborFlow.SelectColumns - 1));
        }

        [Test]
        public void PressDirection_MovesOnceThenRepeatsAfterTheDelay()
        {
            var flow = Playing();
            flow.PressDirection(Direction.Right);
            flow.Tick(Frame);
            Assert.That(flow.Game!.MoveCount, Is.EqualTo(1));

            flow.Tick(Delay * .9f);
            Assert.That(flow.Game.MoveCount, Is.EqualTo(1), "開始遅延の間は動かない");

            flow.Tick(Delay * .2f);
            Assert.That(flow.Game.MoveCount, Is.EqualTo(2));
        }

        [Test]
        public void PressDirection_InALongFrameMovesOnlyOnce()
        {
            var flow = new HarborFlow(LevelCatalog.Maps, .22f, .11f);
            flow.Submit();
            flow.Submit();

            flow.PressDirection(Direction.Right);
            flow.Tick(.25f);

            Assert.That(flow.Game!.MoveCount, Is.EqualTo(1), "押す前の経過時間でリピートしない");
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));

            flow.Tick(.25f);
            Assert.That(flow.Game.MoveCount, Is.EqualTo(2), "押した後に所定の時間が過ぎたらリピートする");
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Cleared));
        }

        [Test]
        public void Repeater_SwitchingKeysInALongFrameRestartsTheDelay()
        {
            var repeater = new MoveRepeater(Delay, Interval);
            repeater.Press(Direction.Up);
            repeater.Tick(Frame);

            repeater.Press(Direction.Left);
            Assert.That(repeater.Tick(LongFrame), Is.Null);
            Assert.That(repeater.Tick(Delay - .01f), Is.Null);
            Assert.That(repeater.Tick(.02f), Is.EqualTo(Direction.Left));
        }

        [Test]
        public void Repeater_ReleasingTheLatestKeyInALongFrameRestartsTheDelay()
        {
            var repeater = new MoveRepeater(Delay, Interval);
            repeater.Press(Direction.Up);
            repeater.Press(Direction.Left);
            repeater.Tick(Frame);

            repeater.Release(Direction.Left);
            Assert.That(repeater.Tick(LongFrame), Is.Null);
            Assert.That(repeater.Tick(Delay - .01f), Is.Null);
            Assert.That(repeater.Tick(.02f), Is.EqualTo(Direction.Up));
        }

        [Test]
        public void Repeater_ReleasingAnOlderKeyKeepsTheDelayRunning()
        {
            var repeater = new MoveRepeater(Delay, Interval);
            repeater.Press(Direction.Up);
            repeater.Press(Direction.Left);
            repeater.Tick(Frame);
            Assert.That(repeater.Tick(Delay - .01f), Is.Null);

            repeater.Release(Direction.Up);

            Assert.That(repeater.Tick(.02f), Is.EqualTo(Direction.Left));
        }

        [Test]
        public void Repeater_FiresAtTheIntervalAfterTheDelay()
        {
            var repeater = new MoveRepeater(Delay, Interval);
            repeater.Press(Direction.Up);
            repeater.Tick(Frame);
            Assert.That(repeater.Tick(Delay - .01f), Is.Null);
            Assert.That(repeater.Tick(.02f), Is.EqualTo(Direction.Up));
            Assert.That(repeater.Tick(Interval - .01f), Is.Null);
            Assert.That(repeater.Tick(.02f), Is.EqualTo(Direction.Up));
        }

        [Test]
        public void Repeater_FiresOnlyOncePerTickAfterAStall()
        {
            var repeater = new MoveRepeater(Delay, Interval);
            repeater.Press(Direction.Up);
            repeater.Tick(Frame);
            Assert.That(repeater.Tick(10f), Is.EqualTo(Direction.Up));
            Assert.That(repeater.Tick(.001f), Is.Null, "処理落ちの分をまとめて進めない");
        }

        [Test]
        public void Repeater_PrefersTheLatestKeyAndFallsBackOnRelease()
        {
            var repeater = new MoveRepeater(Delay, Interval);
            repeater.Press(Direction.Up);
            repeater.Press(Direction.Left);
            repeater.Tick(Frame);
            Assert.That(repeater.Tick(Delay + .01f), Is.EqualTo(Direction.Left));

            repeater.Release(Direction.Left);
            repeater.Tick(Frame);
            Assert.That(repeater.Tick(Delay - .01f), Is.Null, "残ったキーは開始遅延からやり直す");
            Assert.That(repeater.Tick(.02f), Is.EqualTo(Direction.Up));

            repeater.Release(Direction.Up);
            Assert.That(repeater.Tick(10f), Is.Null);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-1f)]
        [TestCase(0f)]
        public void Repeater_IgnoresInvalidTime(float delta)
        {
            var repeater = new MoveRepeater(Delay, Interval);
            repeater.Press(Direction.Up);
            repeater.Tick(Frame);
            Assert.That(repeater.Tick(delta), Is.Null);
            Assert.That(repeater.Tick(Delay - .01f), Is.Null, "不正な時間で残り時間が変わらない");
            Assert.That(repeater.Tick(.02f), Is.EqualTo(Direction.Up));
        }

        [Test]
        public void Repeat_StopsWhenTheLevelStarts()
        {
            var flow = Flow();
            flow.Submit();
            flow.PressDirection(Direction.Right);
            flow.Submit();
            var selected = flow.SelectedLevel;

            ElapseRepeat(flow);

            Assert.That(flow.Game!.MoveCount, Is.Zero, "面選択で押していたキーで動かない");
            Assert.That(flow.SelectedLevel, Is.EqualTo(selected));
        }

        [Test]
        public void Repeat_StopsWhenTheLevelIsCleared()
        {
            var flow = Playing();
            flow.PressDirection(Direction.Right);
            flow.PressDirection(Direction.Right);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Cleared));
            flow.Submit();

            ElapseRepeat(flow);

            Assert.That(flow.Game!.MoveCount, Is.Zero, "次の面へキーの押しっぱなしを持ち越さない");
        }

        [Test]
        public void Focus_LostRejectsEveryOperation()
        {
            var flow = Playing();
            flow.SetFocus(false);

            flow.PressDirection(Direction.Up);
            Assert.That(flow.Move(Direction.Up), Is.EqualTo(MoveResult.Rejected));
            Assert.That(flow.Undo(), Is.False);
            Assert.That(flow.Restart(), Is.False);
            flow.Submit();
            flow.Back();

            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game!.MoveCount, Is.Zero);
        }

        [Test]
        public void Focus_LostStopsTheRepeatEvenAfterReturning()
        {
            var flow = Playing();
            flow.PressDirection(Direction.Up);
            flow.SetFocus(false);
            flow.Tick(Delay + 1f);
            flow.SetFocus(true);
            ElapseRepeat(flow);
            Assert.That(flow.Game!.MoveCount, Is.EqualTo(1));
        }

        [Test]
        public void Focus_ReturnedAcceptsNewPresses()
        {
            var flow = Playing();
            flow.SetFocus(false);
            flow.SetFocus(true);
            flow.PressDirection(Direction.Up);
            Assert.That(flow.Game!.MoveCount, Is.EqualTo(1));
        }

        [Test]
        public void Catalog_CanBePlayedThroughTheFlow()
        {
            var flow = new HarborFlow(LevelCatalog.Maps, Delay, Interval);
            flow.Submit();
            flow.Submit();
            Assert.That(flow.LevelCount, Is.EqualTo(LevelCatalog.Count));
            flow.Move(Direction.Right);
            Assert.That(flow.Move(Direction.Right), Is.EqualTo(MoveResult.Cleared));
        }
    }
}
