using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class BoardStateTests
    {
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(-2)]
        [TestCase(4096)]
        public void FromValues_RejectsInvalidTileValue(int value)
        {
            var values = new int[BoardState.CellCount];
            values[5] = value;
            Assert.Throws<ArgumentOutOfRangeException>(() => BoardState.FromValues(values));
        }

        [TestCase(15)]
        [TestCase(17)]
        public void FromValues_RejectsWrongLength(int length)
        {
            Assert.Throws<ArgumentException>(() => BoardState.FromValues(new int[length]));
        }

        [Test]
        public void FromValues_RejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => BoardState.FromValues(null!));
        }

        [Test]
        public void FromValues_CopiesTheInput()
        {
            var values = new int[BoardState.CellCount];
            values[0] = 2;
            var board = BoardState.FromValues(values);
            values[0] = 4;
            Assert.That(board[0], Is.EqualTo(2));
        }

        [Test]
        public void ToArray_ReturnsACopy()
        {
            var board = BoardState.Empty.With(0, 2);
            board.ToArray()[0] = 4;
            Assert.That(board[0], Is.EqualTo(2));
        }

        [Test]
        public void With_ReturnsNewBoardAndKeepsOriginal()
        {
            var board = BoardState.Empty.With(3, 4);
            Assert.That(board[3], Is.EqualTo(4));
            Assert.That(BoardState.Empty.TileCount, Is.Zero);
        }

        [Test]
        public void With_RejectsOccupiedCellAndInvalidArguments()
        {
            var board = BoardState.Empty.With(3, 4);
            Assert.Throws<InvalidOperationException>(() => board.With(3, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => board.With(-1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => board.With(BoardState.CellCount, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => board.With(0, 3));
        }

        [TestCase(-1, 0)]
        [TestCase(4, 0)]
        [TestCase(0, -1)]
        [TestCase(0, 4)]
        public void Index_RejectsOutsideBoard(int x, int y)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BoardState.Index(x, y));
        }

        [Test]
        public void Equality_ComparesCellValues()
        {
            Assert.That(BoardState.Empty.With(1, 2), Is.EqualTo(BoardState.Empty.With(1, 2)));
            Assert.That(BoardState.Empty.With(1, 2).GetHashCode(), Is.EqualTo(BoardState.Empty.With(1, 2).GetHashCode()));
            Assert.That(BoardState.Empty.With(1, 2), Is.Not.EqualTo(BoardState.Empty.With(2, 2)));
        }
    }
}
