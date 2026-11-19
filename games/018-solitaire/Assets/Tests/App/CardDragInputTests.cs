using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class CardDragInputTests
    {
        private const double DoubleClick = .35;

        // t1: QW（伏せ）の上に 9M 8F。t2: TF。t3: 空。捨て札の上は AS。
        private static readonly Table _table = TableText.Parse(string.Join(
            "\n", "stock: *", "waste: AS", "t1: QW | 9M 8F", "t2: TF"));

        private static PileId T(int column) => PileId.Tableau(column - 1);

        private static TablePoint Center(PileId pile, int index) => TableLayout.CardCenter(_table, pile, index);

        /// <summary>札の上端近くの、上の札に隠れていない帯。</summary>
        private static TablePoint Strip(PileId pile, int index) => Center(pile, index) + new TablePoint(0, (TableLayout.CardHeight / 2) - .05);

        [Test]
        public void DraggingARunDropsItOnTheOverlappedPile()
        {
            var input = new CardDragInput(DoubleClick);
            var grab = Center(T(1), 1) + new TablePoint(.1, .55);
            Assert.That(input.PointerDown(_table, grab, 0), Is.EqualTo(PointerAction.None));
            Assert.That(input.Dragging, Is.Null, "動かすまではドラッグではない");

            var target = Center(T(2), 0) + new TablePoint(.1, .55 - .3);
            input.PointerMove(target);
            Assert.That(input.Dragging, Is.EqualTo(new PileRun(T(1), 2)));
            Assert.That(input.DragBaseCenter.X, Is.EqualTo(Center(T(2), 0).X).Within(1e-9), "掴んだ位置のずれを保つ");

            var action = input.PointerUp(_table, target, .5);
            Assert.That(action, Is.EqualTo(PointerAction.Move(Command.Move(T(1), T(2), 2))));
            Assert.That(input.Dragging, Is.Null);
            Assert.That(input.Selected, Is.Null);
        }

        [Test]
        public void DroppingOnNothingReturnsNoAction()
        {
            var input = new CardDragInput(DoubleClick);
            input.PointerDown(_table, Center(T(1), 2), 0);
            input.PointerMove(new TablePoint(8, -3));
            Assert.That(input.PointerUp(_table, new TablePoint(8, -3), .2), Is.EqualTo(PointerAction.None));
            Assert.That(input.IsPressed, Is.False);
        }

        [Test]
        public void SmallMovementIsStillAClick()
        {
            var input = new CardDragInput(DoubleClick);
            var point = Center(T(1), 2);
            input.PointerDown(_table, point, 0);
            input.PointerMove(point + new TablePoint(CardDragInput.DragThreshold * .5, 0));
            Assert.That(input.Dragging, Is.Null);
            input.PointerUp(_table, point, .1);
            Assert.That(input.Selected, Is.EqualTo(new PileRun(T(1), 1)));
        }

        [Test]
        public void ClickThenClickMovesTheSelection()
        {
            var input = new CardDragInput(DoubleClick);
            input.PointerDown(_table, Strip(T(1), 1), 0);
            input.PointerUp(_table, Strip(T(1), 1), .05);
            Assert.That(input.Selected, Is.EqualTo(new PileRun(T(1), 2)));

            var action = input.PointerDown(_table, Center(T(2), 0), 2);
            Assert.That(action, Is.EqualTo(PointerAction.Move(Command.Move(T(1), T(2), 2))));
            Assert.That(input.Selected, Is.Null);
            Assert.That(input.PointerUp(_table, Center(T(2), 0), 2.1), Is.EqualTo(PointerAction.None));
        }

        [Test]
        public void ClickOnAnEmptyPileMovesTheSelectionThere()
        {
            var input = new CardDragInput(DoubleClick);
            input.PointerDown(_table, Center(PileId.Waste, 0), 0);
            input.PointerUp(_table, Center(PileId.Waste, 0), .05);
            var action = input.PointerDown(_table, TableLayout.SlotCenter(PileId.Foundation(1)), 1);
            Assert.That(action, Is.EqualTo(PointerAction.Move(Command.Move(PileId.Waste, PileId.Foundation(1)))));
        }

        [Test]
        public void ClickingTheSelectedCardAgainDeselectsIt()
        {
            var input = new CardDragInput(DoubleClick);
            var point = Center(T(1), 2);
            input.PointerDown(_table, point, 0);
            input.PointerUp(_table, point, .05);
            input.PointerDown(_table, point, 5);
            input.PointerUp(_table, point, 5.05);
            Assert.That(input.Selected, Is.Null);
        }

        [Test]
        public void ClickingAnotherCardOfTheSamePileReselects()
        {
            var input = new CardDragInput(DoubleClick);
            input.PointerDown(_table, Center(T(1), 2), 0);
            input.PointerUp(_table, Center(T(1), 2), .05);
            input.PointerDown(_table, Strip(T(1), 1), 5);
            input.PointerUp(_table, Strip(T(1), 1), 5.05);
            Assert.That(input.Selected, Is.EqualTo(new PileRun(T(1), 2)));
        }

        [Test]
        public void DoubleClickOnTheTopCardSendsItToTheFoundation()
        {
            var input = new CardDragInput(DoubleClick);
            var point = Center(PileId.Waste, 0);
            input.PointerDown(_table, point, 1);
            input.PointerUp(_table, point, 1.05);
            Assert.That(input.PointerDown(_table, point, 1.2), Is.EqualTo(PointerAction.SendToFoundation(PileId.Waste)));
            Assert.That(input.Selected, Is.Null);
            Assert.That(input.PointerUp(_table, point, 1.25), Is.EqualTo(PointerAction.None));
        }

        [Test]
        public void SlowSecondClickOrLowerCardIsNotADoubleClick()
        {
            var input = new CardDragInput(DoubleClick);
            var top = Center(T(1), 2);
            input.PointerDown(_table, top, 1);
            input.PointerUp(_table, top, 1.05);
            Assert.That(input.PointerDown(_table, top, 1.05 + DoubleClick + .01), Is.EqualTo(PointerAction.None), "遅い");

            var lower = Strip(T(1), 1);
            input.Cancel();
            input.PointerDown(_table, lower, 3);
            input.PointerUp(_table, lower, 3.05);
            Assert.That(input.PointerDown(_table, lower, 3.1), Is.EqualTo(PointerAction.None), "一番上の札ではない");
        }

        [Test]
        public void ClickOnAnotherPileBetweenClicksCancelsTheDoubleClick()
        {
            var input = new CardDragInput(DoubleClick);
            var point = Center(PileId.Waste, 0);
            input.PointerDown(_table, point, 1);
            input.PointerUp(_table, point, 1.02);
            input.PointerDown(_table, TableLayout.SlotCenter(PileId.Tableau(2)), 1.04);
            Assert.That(input.PointerDown(_table, point, 1.1), Is.EqualTo(PointerAction.None));
        }

        [Test]
        public void StockClickAndFaceDownCards()
        {
            var input = new CardDragInput(DoubleClick);
            Assert.That(input.PointerDown(_table, TableLayout.SlotCenter(PileId.Stock), 0), Is.EqualTo(PointerAction.ClickStock));
            Assert.That(input.IsPressed, Is.False);

            var hidden = Center(T(1), 0) + new TablePoint(0, .6);
            Assert.That(input.PointerDown(_table, hidden, 1), Is.EqualTo(PointerAction.None));
            Assert.That(input.IsPressed, Is.False, "伏せ札は掴めない");
            Assert.That(input.PointerDown(_table, new TablePoint(9, -4), 2), Is.EqualTo(PointerAction.None));
        }

        [Test]
        public void ClickingEmptyBackgroundClearsTheSelection()
        {
            var input = new CardDragInput(DoubleClick);
            input.PointerDown(_table, Center(T(2), 0), 0);
            input.PointerUp(_table, Center(T(2), 0), .05);
            input.PointerDown(_table, new TablePoint(9, -4), 3);
            Assert.That(input.Selected, Is.Null);
        }

        [Test]
        public void UpWithoutDownAndCancelAreHarmless()
        {
            var input = new CardDragInput(DoubleClick);
            Assert.That(input.PointerUp(_table, Center(T(2), 0), 0), Is.EqualTo(PointerAction.None));
            input.PointerDown(_table, Center(T(2), 0), 0);
            input.PointerMove(new TablePoint(0, 0));
            input.Cancel();
            Assert.That(input.Dragging, Is.Null);
            Assert.That(input.PointerUp(_table, new TablePoint(0, 0), 1), Is.EqualTo(PointerAction.None));
            input.PointerMove(new TablePoint(double.NaN, 0));
        }

        [Test]
        public void InvalidDoubleClickIntervalThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new CardDragInput(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new CardDragInput(double.PositiveInfinity));
        }
    }
}
