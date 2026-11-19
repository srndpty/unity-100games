using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class SolitaireFlowTests
    {
        private const double DoubleClick = .35;

        private static SolitaireFlow NewFlow(int seed = 10) => new(new SolitaireConfig(), seed, DoubleClick);

        private static SolitaireFlow FlowFrom(Table table)
        {
            var flow = NewFlow();
            flow.GameFactory = _ => new SolitaireGame(flow.Config, table);
            return flow;
        }

        private static TablePoint Center(SolitaireFlow flow, PileId pile, int index) => TableLayout.CardCenter(flow.Game!.Table, pile, index);

        private static void Click(SolitaireFlow flow, TablePoint point, double time)
        {
            flow.PointerDown(point, time);
            flow.PointerUp(point, time + .05);
        }

        private static void Drag(SolitaireFlow flow, TablePoint from, TablePoint to, double time)
        {
            flow.PointerDown(from, time);
            flow.PointerMove(new TablePoint((from.X + to.X) / 2, (from.Y + to.Y) / 2));
            flow.PointerMove(to);
            flow.PointerUp(to, time + .3);
        }

        [Test]
        public void TitleStartsAGameAndEachRestartUsesTheNextSeed()
        {
            var flow = NewFlow(seed: 7);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.MatchSeed, Is.EqualTo(7));
            var first = TableText.Format(flow.Game!.Table);

            flow.TogglePause();
            flow.Restart();
            Assert.That(flow.MatchSeed, Is.EqualTo(8));
            Assert.That(TableText.Format(flow.Game!.Table), Is.Not.EqualTo(first));
        }

        [Test]
        public void DragMovesCardsAndFlipsWithEvents()
        {
            var flow = FlowFrom(TableText.Parse("stock: *\nt1: QS | 9C\nt2: TD"));
            flow.Submit();
            flow.TakeEvents();
            Drag(flow, Center(flow, PileId.Tableau(0), 1), Center(flow, PileId.Tableau(1), 0) + new TablePoint(0, -.3), 1);
            Assert.That(flow.Game!.Moves, Is.EqualTo(1));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Moved | FlowEvents.Flipped));
            Assert.That(flow.Notice, Is.EqualTo(Notice.None));
        }

        [Test]
        public void IllegalDropIsRejectedWithTheReasonAndNothingChanges()
        {
            var flow = FlowFrom(TableText.Parse("stock: *\nt1: 9C\nt2: 9H"));
            flow.Submit();
            var before = TableText.Format(flow.Game!.Table);
            Drag(flow, Center(flow, PileId.Tableau(0), 0), Center(flow, PileId.Tableau(1), 0), 1);
            Assert.That(flow.Notice, Is.EqualTo(Notice.Rejected));
            Assert.That(flow.LastError, Is.EqualTo(MoveError.RankMismatch));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Rejected));
            Assert.That(TableText.Format(flow.Game!.Table), Is.EqualTo(before));
            Assert.That(flow.Game!.Moves, Is.EqualTo(0));
            Assert.That(flow.Input.Dragging, Is.Null);
        }

        [Test]
        public void StockClickDrawsAndUndoPutsItBack()
        {
            var flow = NewFlow();
            flow.Submit();
            flow.TakeEvents();
            var before = TableText.Format(flow.Game!.Table);
            Click(flow, TableLayout.SlotCenter(PileId.Stock), 1);
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Drawn));
            Assert.That(flow.Game!.Table.CountOf(PileId.Waste), Is.EqualTo(1));

            flow.Undo();
            Assert.That(flow.Notice, Is.EqualTo(Notice.Undone));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Undone));
            Assert.That(TableText.Format(flow.Game!.Table), Is.EqualTo(before));

            flow.Undo();
            Assert.That(flow.Notice, Is.EqualTo(Notice.NothingToUndo));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None));
        }

        [Test]
        public void UndoClearsTheSelection()
        {
            var flow = NewFlow();
            flow.Submit();
            Click(flow, TableLayout.SlotCenter(PileId.Stock), 1);
            Click(flow, Center(flow, PileId.Waste, 0), 2);
            Assert.That(flow.Input.Selected, Is.Not.Null);
            flow.Undo();
            Assert.That(flow.Input.Selected, Is.Null);
        }

        [Test]
        public void DoubleClickSendsToFoundationAndTheLastCardWins()
        {
            var flow = FlowFrom(DemoTables.OneMoveFromWin());
            flow.Submit();
            flow.TakeEvents();
            var king = Center(flow, PileId.Tableau(2), 0);
            Click(flow, king, 1);
            Click(flow, king, 1.2);
            Assert.That(flow.Game!.Phase, Is.EqualTo(GamePhase.Won));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.ToFoundation | FlowEvents.Won));
        }

        [Test]
        public void StuckMoveEndsTheGame()
        {
            var flow = FlowFrom(DemoTables.OneMoveFromStuck(1));
            flow.Submit();
            flow.TakeEvents();
            Drag(flow, Center(flow, PileId.Waste, 2), Center(flow, PileId.Tableau(6), 0) + new TablePoint(0, -.36), 1);
            Assert.That(flow.Game!.Phase, Is.EqualTo(GamePhase.Stuck));
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.TakeEvents() & FlowEvents.Stuck, Is.EqualTo(FlowEvents.Stuck));
        }

        [Test]
        public void ResignIsOnlyFromPause()
        {
            var flow = NewFlow();
            flow.Submit();
            flow.Resign();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing), "プレイ中は無視");
            flow.TogglePause();
            flow.Resign();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Game!.Phase, Is.EqualTo(GamePhase.Resigned));
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.Resigned));
            flow.Resign();
            Assert.That(flow.TakeEvents(), Is.EqualTo(FlowEvents.None), "2 回目は無視");
        }

        [Test]
        public void OperationsAfterTheGameEndsAreIgnored()
        {
            var finished = FlowFrom(DemoTables.OneMoveFromWin());
            finished.Submit();
            var king = Center(finished, PileId.Tableau(2), 0);
            Click(finished, king, 1);
            Click(finished, king, 1.2);
            finished.TakeEvents();

            var before = TableText.Format(finished.Game!.Table);
            Click(finished, TableLayout.SlotCenter(PileId.Stock), 3);
            finished.Undo();
            finished.TogglePause();
            Assert.That(finished.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(finished.TakeEvents(), Is.EqualTo(FlowEvents.None));
            Assert.That(TableText.Format(finished.Game!.Table), Is.EqualTo(before));
        }

        [Test]
        public void PausedFlowIgnoresPointersAndUndo()
        {
            var flow = NewFlow();
            flow.Submit();
            flow.TogglePause();
            Click(flow, TableLayout.SlotCenter(PileId.Stock), 1);
            flow.Undo();
            Assert.That(flow.Game!.Moves, Is.EqualTo(0));
            Assert.That(flow.Notice, Is.EqualTo(Notice.None));
        }

        [Test]
        public void LosingFocusPausesAndDropsTheDragAndSelection()
        {
            var flow = NewFlow();
            flow.Submit();
            var top = Center(flow, PileId.Tableau(6), 6);
            flow.PointerDown(top, 1);
            flow.PointerMove(top + new TablePoint(1, 0));
            Assert.That(flow.Input.Dragging, Is.Not.Null);

            flow.SetFocus(false);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused));
            Assert.That(flow.Input.Dragging, Is.Null);
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "フォーカスがない間は再開しない");

            flow.SetFocus(true);
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Paused), "戻っても自動では再開しない");
            flow.PointerUp(top + new TablePoint(1, 0), 2);
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(flow.Game!.Moves, Is.EqualTo(0), "離しても前のドラッグは落とさない");
        }

        [Test]
        public void ElapsedTimeCountsOnlyWhilePlaying()
        {
            var flow = NewFlow();
            flow.Advance(1);
            Assert.That(flow.ElapsedTicks, Is.EqualTo(0));
            flow.Submit();
            flow.Advance(1);
            Assert.That(flow.ElapsedTicks, Is.EqualTo(SolitaireFlow.TickRate));
            flow.TogglePause();
            flow.Advance(1);
            flow.StepTick();
            Assert.That(flow.ElapsedTicks, Is.EqualTo(SolitaireFlow.TickRate));
            flow.TogglePause();
            for (var i = 0; i < 10; i++) { flow.Advance(.1); }
            Assert.That(flow.ElapsedTicks, Is.EqualTo(2 * SolitaireFlow.TickRate), "0.1 秒刻みでも取りこぼさない");
            flow.Advance(double.NaN);
            flow.Advance(-1);
            Assert.That(flow.ElapsedTicks, Is.EqualTo(2 * SolitaireFlow.TickRate));
            flow.Advance(1e12);
            Assert.That(flow.ElapsedTicks, Is.EqualTo(SolitaireFlow.MaxElapsedTicks));
        }

        [Test]
        public void TitleAndResultTransitions()
        {
            var flow = NewFlow();
            flow.BackToTitle();
            flow.Restart();
            flow.TogglePause();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title), "タイトルでは無視");
            flow.Submit();
            flow.TogglePause();
            flow.BackToTitle();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(flow.Game, Is.Null);
        }

        [Test]
        public void StartingFromAStuckDealGoesStraightToTheResult()
        {
            var flow = FlowFrom(DemoTables.OneMoveFromStuck(1));
            flow.GameFactory = _ =>
            {
                var game = new SolitaireGame(flow.Config, DemoTables.OneMoveFromStuck(1));
                game.Apply(DemoTables.StuckMove);
                return new SolitaireGame(flow.Config, game.Table);
            };
            flow.Submit();
            Assert.That(flow.Screen, Is.EqualTo(AppScreen.Result));
            Assert.That(flow.Game!.Phase, Is.EqualTo(GamePhase.Stuck));
        }
    }
}
