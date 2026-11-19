using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class SwapInputTests
    {
        private static Point P(int x, int y) => new(x, y);

        [Test]
        public void ClickThenAdjacentClickSwaps()
        {
            var input = new SwapInput();

            Assert.That(input.PointerDown(1.5, 2.5), Is.Null);
            input.PointerUp();
            Assert.That(input.Selected, Is.EqualTo(P(1, 2)));
            Assert.That(input.PointerDown(1.5, 3.2), Is.EqualTo(new Swap(P(1, 2), P(1, 3))));
            input.PointerUp();
            Assert.That(input.Selected, Is.Null);
        }

        [Test]
        public void ClickOnFarCellMovesTheSelection()
        {
            var input = new SwapInput();
            input.PointerDown(1.5, 2.5);
            input.PointerUp();

            Assert.That(input.PointerDown(3.5, 3.5), Is.Null);
            input.PointerUp();
            Assert.That(input.Selected, Is.EqualTo(P(3, 3)));
        }

        [Test]
        public void ClickOnDiagonalCellMovesTheSelection()
        {
            var input = new SwapInput();
            input.PointerDown(1.5, 2.5);
            input.PointerUp();

            Assert.That(input.PointerDown(2.5, 3.5), Is.Null);
            Assert.That(input.Selected, Is.EqualTo(P(2, 3)));
        }

        [Test]
        public void ClickingTheSelectedCellAgainDeselectsOnRelease()
        {
            var input = new SwapInput();
            input.PointerDown(1.5, 2.5);
            input.PointerUp();
            input.PointerDown(1.5, 2.5);

            Assert.That(input.Selected, Is.EqualTo(P(1, 2)), "押している間はドラッグできるよう選択を残す");
            input.PointerUp();
            Assert.That(input.Selected, Is.Null);
        }

        [Test]
        public void ClickOutsideTheBoardClearsTheSelection()
        {
            var input = new SwapInput();
            input.PointerDown(1.5, 2.5);
            input.PointerUp();

            Assert.That(input.PointerDown(-0.5, 2.5), Is.Null);
            Assert.That(input.Selected, Is.Null);
            Assert.That(input.PointerDown(6.0, 2.5), Is.Null);
            Assert.That(input.PointerDown(2.5, double.NaN), Is.Null);
        }

        [Test]
        public void ShortDragDoesNothing()
        {
            var input = new SwapInput();
            input.PointerDown(1.5, 2.5);

            Assert.That(input.PointerMove(1.5 + SwapInput.DragThreshold - 0.01, 2.5), Is.Null);
            Assert.That(input.Selected, Is.EqualTo(P(1, 2)));
        }

        [TestCase(2.2, 2.5, 2, 2)]
        [TestCase(0.8, 2.5, 0, 2)]
        [TestCase(1.5, 3.2, 1, 3)]
        [TestCase(1.5, 1.8, 1, 1)]
        [TestCase(2.3, 2.9, 2, 2)]
        [TestCase(1.8, 3.4, 1, 3)]
        public void DragPastTheThresholdSwapsAlongTheDominantAxis(double x, double y, int tx, int ty)
        {
            var input = new SwapInput();
            input.PointerDown(1.5, 2.5);

            Assert.That(input.PointerMove(x, y), Is.EqualTo(new Swap(P(1, 2), P(tx, ty))));
            Assert.That(input.Selected, Is.Null);
            Assert.That(input.PointerMove(x + 1, y), Is.Null, "1回のドラッグで入れ替えるのは1回だけ");
        }

        [Test]
        public void DragOutwardAtTheEdgeAsksForAnOutsideSwap()
        {
            // 盤面の外との入れ替えは、ゲームが OutOfBoard として拒否する。
            var input = new SwapInput();
            input.PointerDown(0.5, 0.5);

            Assert.That(input.PointerMove(-0.3, 0.5), Is.EqualTo(new Swap(P(0, 0), P(-1, 0))));
        }

        [Test]
        public void MoveWithoutPressIsIgnored()
        {
            var input = new SwapInput();

            Assert.That(input.PointerMove(4.5, 4.5), Is.Null);
            input.PointerDown(1.5, 2.5);
            input.PointerUp();
            Assert.That(input.PointerMove(4.5, 2.5), Is.Null, "離した後のマウスの移動では入れ替えない");
        }

        [Test]
        public void ArrowsMoveTheCursorAndStopAtTheEdge()
        {
            var input = new SwapInput();
            for (var i = 0; i < 10; i++) { input.Move(-1, 0); }
            for (var i = 0; i < 10; i++) { input.Move(0, 1); }

            Assert.That(input.Cursor, Is.EqualTo(P(0, Board.Height - 1)));
            Assert.That(input.KeyboardActive, Is.True);
            Assert.That(input.Selected, Is.Null);
        }

        [Test]
        public void SpaceThenArrowSwapsAndCursorFollows()
        {
            var input = new SwapInput();
            var start = input.Cursor;
            input.ToggleSelect();

            Assert.That(input.Selected, Is.EqualTo(start));
            Assert.That(input.Move(1, 0), Is.EqualTo(new Swap(start, start.Offset(1, 0))));
            Assert.That(input.Selected, Is.Null);
            Assert.That(input.Cursor, Is.EqualTo(start.Offset(1, 0)));
        }

        [Test]
        public void SpaceTwiceDeselects()
        {
            var input = new SwapInput();
            input.ToggleSelect();
            input.ToggleSelect();

            Assert.That(input.Selected, Is.Null);
        }

        [Test]
        public void ClickSwitchesToMouseMode()
        {
            var input = new SwapInput();
            input.Move(1, 0);
            input.PointerDown(0.5, 0.5);

            Assert.That(input.KeyboardActive, Is.False);
            Assert.That(input.Cursor, Is.EqualTo(P(0, 0)));
        }

        [Test]
        public void DiagonalArrowIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new SwapInput().Move(1, 1));
            Assert.Throws<ArgumentException>(() => new SwapInput().Move(0, 0));
        }
    }
}
