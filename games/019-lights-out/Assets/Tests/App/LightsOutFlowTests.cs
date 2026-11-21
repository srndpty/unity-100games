using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class LightsOutFlowTests
    {
        private static LightsOutFlow NewFlow(int seed = 100) => new(new LightsOutConfig(), seed);

        private static LightsOutFlow Playing(int seed = 100)
        {
            var flow = NewFlow(seed);
            flow.Apply(Intent.Of(IntentKind.Submit));
            return flow;
        }

        private static void Solve(LightsOutFlow flow)
        {
            for (var guard = 0; guard < LightsBoard.CellCount && flow.Screen == AppScreen.Playing; guard++)
            {
                flow.Apply(Intent.PressCell(Autopilot.NextPress(flow.Game!)));
            }
        }

        [Test]
        public void StartsAtTitleWithoutAGame()
        {
            var flow = NewFlow();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
        }

        [Test]
        public void TitleToPlayingToResultToReplay()
        {
            var flow = NewFlow(seed: 100);
            flow.Apply(Intent.Of(IntentKind.Submit));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game!.Seed, Is.EqualTo(100));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Started));

            Solve(flow);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Game.Status, Is.EqualTo(GameStatus.Cleared));
            Assert.That(flow.TakeEvents() & FlowEvents.Cleared, Is.EqualTo(FlowEvents.Cleared));

            flow.Apply(Intent.Of(IntentKind.Submit));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game.Seed, Is.EqualTo(101), "再戦は新しいシード");
            Assert.That(flow.Game.Moves, Is.Zero);
        }

        [Test]
        public void EveryReplayUsesANewSeed()
        {
            var flow = Playing(seed: int.MaxValue);
            var seeds = new System.Collections.Generic.HashSet<int> { flow.Game!.Seed };
            for (var match = 0; match < 5; match++)
            {
                flow.Apply(Intent.Of(IntentKind.TogglePause));
                flow.Apply(Intent.Of(IntentKind.NewPuzzle));
                Assert.That(seeds.Add(flow.Game!.Seed), Is.True);
            }
        }

        [Test]
        public void AutopilotClearsEveryMatchInOptimalMoves()
        {
            var flow = NewFlow(seed: 0);
            for (var match = 0; match < 200; match++)
            {
                flow.Apply(Intent.Of(IntentKind.Submit));
                Solve(flow);
                Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
                Assert.That(flow.Game!.Status, Is.EqualTo(GameStatus.Cleared));
                Assert.That(flow.Game.Moves, Is.EqualTo(flow.Game.OptimalMoves));
            }
        }

        [Test]
        public void KeyboardMovesTheCursorAndPressesTheCell()
        {
            var flow = Playing();
            Assert.That(flow.Cursor.Cell, Is.EqualTo(12), "開始時は中央");
            flow.Apply(Intent.Of(IntentKind.CursorLeft));
            flow.Apply(Intent.Of(IntentKind.CursorUp));
            Assert.That(flow.Cursor.Cell, Is.EqualTo(6));

            var before = flow.Game!.Board;
            flow.Apply(Intent.Of(IntentKind.PressCursor));
            Assert.That(flow.Game.Board, Is.EqualTo(before.Press(6)));
            Assert.That(flow.Game.Moves, Is.EqualTo(1));
            Assert.That(flow.TakeEvents() & FlowEvents.Pressed, Is.EqualTo(FlowEvents.Pressed));
        }

        [Test]
        public void ClickPressesTheCellAndMovesTheCursorThere()
        {
            var flow = Playing();
            var before = flow.Game!.Board;
            flow.Apply(Intent.PressCell(24));
            Assert.That(flow.Game.Board, Is.EqualTo(before.Press(24)));
            Assert.That(flow.Cursor.Cell, Is.EqualTo(24));

            flow.Apply(Intent.PointCell(3));
            Assert.That(flow.Cursor.Cell, Is.EqualTo(3));
            Assert.That(flow.Game.Moves, Is.EqualTo(1), "指すだけでは押さない");
        }

        [Test]
        public void CursorStopsAtTheEdges()
        {
            var cursor = new BoardCursor();
            for (var i = 0; i < 10; i++) { cursor.Move(-1, -1); }
            Assert.That((cursor.X, cursor.Y), Is.EqualTo((0, 0)));
            for (var i = 0; i < 10; i++) { cursor.Move(1, 1); }
            Assert.That((cursor.X, cursor.Y), Is.EqualTo((4, 4)));
            Assert.Throws<ArgumentOutOfRangeException>(() => cursor.Move(2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => cursor.MoveTo(25));
        }

        [Test]
        public void BoardOperationsAreIgnoredOutsidePlay()
        {
            var flow = NewFlow();
            flow.Apply(Intent.PressCell(0));
            flow.Apply(Intent.Of(IntentKind.CursorLeft));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);

            flow.Apply(Intent.Of(IntentKind.Submit));
            flow.Apply(Intent.Of(IntentKind.TogglePause));
            var board = flow.Game!.Board;
            flow.Apply(Intent.PressCell(0));
            flow.Apply(Intent.Of(IntentKind.PressCursor));
            flow.Apply(Intent.Of(IntentKind.CursorLeft));
            Assert.That(flow.Game.Board, Is.EqualTo(board), "一時停止中は押せない");
            Assert.That(flow.Cursor.Cell, Is.EqualTo(12));
            Assert.That(flow.Game.Moves, Is.Zero);
        }

        [Test]
        public void PressAfterClearIsIgnored()
        {
            var flow = Playing();
            Solve(flow);
            var moves = flow.Game!.Moves;
            flow.TakeEvents();

            flow.Apply(Intent.PressCell(0));
            flow.Apply(Intent.Of(IntentKind.PressCursor));
            flow.Apply(Intent.Of(IntentKind.GiveUp));
            flow.Apply(Intent.Of(IntentKind.Retry));
            Assert.That(flow.Game.Board.IsDark, Is.True);
            Assert.That(flow.Game.Moves, Is.EqualTo(moves));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None));
        }

        [Test]
        public void PauseAndResume()
        {
            var flow = Playing();
            flow.Apply(Intent.Of(IntentKind.TogglePause));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            flow.Apply(Intent.Of(IntentKind.TogglePause));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            flow.Apply(Intent.Of(IntentKind.TogglePause));
            flow.Apply(Intent.Of(IntentKind.Submit));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "決定でも再開する");
        }

        [Test]
        public void RetryRestoresThePuzzleOnlyWhilePaused()
        {
            var flow = Playing();
            flow.Apply(Intent.PressCell(0));
            flow.Apply(Intent.Of(IntentKind.Retry));
            Assert.That(flow.Game!.Moves, Is.EqualTo(1), "プレイ中は受け付けない");

            flow.Apply(Intent.Of(IntentKind.TogglePause));
            flow.Apply(Intent.Of(IntentKind.Retry));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game.Board, Is.EqualTo(flow.Game.Puzzle.Board));
            Assert.That(flow.Game.Moves, Is.Zero);
            Assert.That(flow.Game.Seed, Is.EqualTo(100), "同じ問題のまま");
            Assert.That(flow.TakeEvents() & FlowEvents.Retried, Is.EqualTo(FlowEvents.Retried));
        }

        [Test]
        public void GiveUpShowsTheAnswerForTheCurrentBoard()
        {
            var flow = Playing();
            flow.Apply(Intent.PressCell(0));
            flow.Apply(Intent.Of(IntentKind.GiveUp));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "プレイ中は受け付けない");

            flow.Apply(Intent.Of(IntentKind.TogglePause));
            flow.Apply(Intent.Of(IntentKind.GiveUp));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Game!.Status, Is.EqualTo(GameStatus.GaveUp));
            Assert.That(flow.Answer, Is.Not.Zero);
            Assert.That(flow.Game.Board.PressAll(flow.Answer).IsDark, Is.True);
            Assert.That(flow.TakeEvents() & FlowEvents.GaveUp, Is.EqualTo(FlowEvents.GaveUp));

            flow.Apply(Intent.Of(IntentKind.GiveUp));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None), "重ねてあきらめても何も起きない");

            flow.Apply(Intent.Of(IntentKind.Submit));
            Assert.That(flow.Answer, Is.Zero, "次の試合では解答を消す");
        }

        [Test]
        public void BackToTitleFromPauseAndResult()
        {
            var flow = Playing();
            flow.Apply(Intent.Of(IntentKind.BackToTitle));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "プレイ中は受け付けない");

            flow.Apply(Intent.Of(IntentKind.TogglePause));
            flow.Apply(Intent.Of(IntentKind.BackToTitle));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);

            flow.Apply(Intent.Of(IntentKind.Submit));
            Solve(flow);
            flow.Apply(Intent.Of(IntentKind.BackToTitle));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [Test]
        public void LosingFocusPausesAndBlocksInputUntilRestored()
        {
            var flow = Playing();
            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));

            flow.Apply(Intent.Of(IntentKind.TogglePause));
            flow.Apply(Intent.Of(IntentKind.GiveUp));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間は操作を受け付けない");

            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
            flow.Apply(Intent.Of(IntentKind.TogglePause));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
        }

        [Test]
        public void QuitDoesNotChangeTheFlow()
        {
            var flow = NewFlow();
            flow.Apply(Intent.Of(IntentKind.Quit));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
        }

        [Test]
        public void InvalidIntentsAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.PressCell(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Intent.PointCell(25));
            Assert.Throws<ArgumentOutOfRangeException>(() => NewFlow().Apply(Intent.Of((IntentKind)999)));
            Assert.Throws<ArgumentNullException>(() => _ = new LightsOutFlow(null!, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new LightsOutFlow(new LightsOutConfig { ScramblePresses = 0 }, 0));
        }

        [Test]
        public void FlowKeepsItsOwnCopyOfTheConfig()
        {
            var config = new LightsOutConfig { ScramblePresses = 4 };
            var flow = new LightsOutFlow(config, 0);
            config.ScramblePresses = 99;
            flow.Apply(Intent.Of(IntentKind.Submit));
            Assert.That(flow.Config.ScramblePresses, Is.EqualTo(4));
        }

        [Test]
        public void AutopilotReturnsMinusOneOnDarkBoard()
        {
            var flow = Playing();
            Solve(flow);
            Assert.That(Autopilot.NextPress(flow.Game!), Is.EqualTo(-1));
            Assert.Throws<ArgumentNullException>(() => Autopilot.NextPress(null!));
        }
    }
}
