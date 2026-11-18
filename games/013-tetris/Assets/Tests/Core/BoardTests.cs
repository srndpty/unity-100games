using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class BoardTests
    {
        private static readonly int[] _rowsOneAndFive = { 1, 5 };
        private static readonly int[] _notFullRow = { 1 };
        private static readonly int[] _duplicatedRow = { 0, 0 };

        [Test]
        public void OutsideTheBoardIsBlockedOnEverySide()
        {
            Assert.That(Board.Empty.IsBlocked(-1, 0), Is.True);
            Assert.That(Board.Empty.IsBlocked(Board.Width, 0), Is.True);
            Assert.That(Board.Empty.IsBlocked(0, -1), Is.True);
            Assert.That(Board.Empty.IsBlocked(0, Board.Height), Is.True);
            Assert.That(Board.Empty.IsBlocked(Board.Width - 1, Board.Height - 1), Is.False);
        }

        [Test]
        public void ChangesReturnANewBoardAndKeepTheOriginal()
        {
            var board = Board.Empty.With(2, 3, PieceKind.S);
            Assert.That(board.At(2, 3), Is.EqualTo(PieceKind.S));
            Assert.That(Board.Empty.At(2, 3), Is.Null);
        }

        [Test]
        public void LockWritesTheFourCellsOfThePiece()
        {
            var board = Board.Empty.Lock(new ActivePiece(PieceKind.O, Rotation.North, 4, 0));
            Assert.That(board.At(4, 0), Is.EqualTo(PieceKind.O));
            Assert.That(board.At(5, 1), Is.EqualTo(PieceKind.O));
            Assert.That(board.At(6, 0), Is.Null);
        }

        [Test]
        public void LockRejectsAPieceThatDoesNotFit()
        {
            var board = Board.Empty.With(4, 0, PieceKind.J);
            Assert.Throws<InvalidOperationException>(() => board.Lock(new ActivePiece(PieceKind.O, Rotation.North, 4, 0)));
            Assert.Throws<InvalidOperationException>(() => Board.Empty.Lock(new ActivePiece(PieceKind.O, Rotation.North, -1, 0)));
        }

        [Test]
        public void FullRowsAreListedFromTheBottom()
        {
            var board = StrataTestKit.FillRow(StrataTestKit.FillRow(Board.Empty, 5), 1);
            board = StrataTestKit.FillRow(board, 3, 9);
            Assert.That(board.FullRows(), Is.EqualTo(_rowsOneAndFive));
        }

        [Test]
        public void ClearingSeparatedRowsDropsEachRowByTheRowsRemovedBelowIt()
        {
            var board = StrataTestKit.FillRow(StrataTestKit.FillRow(Board.Empty, 0), 2)
                .With(3, 1, PieceKind.T)
                .With(4, 3, PieceKind.S);

            var cleared = board.ClearRows(board.FullRows());

            Assert.That(cleared.At(3, 0), Is.EqualTo(PieceKind.T));
            Assert.That(cleared.At(4, 1), Is.EqualTo(PieceKind.S));
            Assert.That(cleared.At(4, 3), Is.Null);
            Assert.That(cleared.FullRows(), Is.Empty);
        }

        [Test]
        public void HiddenRowsDropIntoTheVisibleArea()
        {
            var board = StrataTestKit.FillRow(Board.Empty, Board.VisibleHeight - 1).With(6, Board.VisibleHeight, PieceKind.L);
            var cleared = board.ClearRows(board.FullRows());
            Assert.That(cleared.At(6, Board.VisibleHeight - 1), Is.EqualTo(PieceKind.L));
        }

        [Test]
        public void TheTopRowCanBeClearedToo()
        {
            var board = StrataTestKit.FillRow(Board.Empty, Board.Height - 1);
            Assert.That(board.ClearRows(board.FullRows()), Is.EqualTo(Board.Empty));
        }

        [Test]
        public void ClearRejectsInvalidRows()
        {
            var board = StrataTestKit.FillRow(Board.Empty, 0);
            Assert.Throws<ArgumentException>(() => board.ClearRows(_notFullRow));
            Assert.Throws<ArgumentException>(() => board.ClearRows(_duplicatedRow));
            Assert.Throws<ArgumentOutOfRangeException>(() => board.ClearRows(new[] { Board.Height }));
            Assert.Throws<ArgumentNullException>(() => board.ClearRows(null!));
        }

        [Test]
        public void AccessOutsideTheBoardIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Board.Empty.At(Board.Width, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Board.Empty.With(0, Board.Height, PieceKind.I));
            Assert.Throws<ArgumentOutOfRangeException>(() => Board.Empty.With(0, 0, (PieceKind)7));
        }

        [Test]
        public void FingerprintFollowsTheContents()
        {
            var first = Board.Empty.With(1, 1, PieceKind.Z);
            var same = Board.Empty.With(1, 1, PieceKind.Z);
            var other = Board.Empty.With(1, 1, PieceKind.S);
            Assert.That(first.Fingerprint(), Is.EqualTo(same.Fingerprint()));
            Assert.That(first, Is.EqualTo(same));
            Assert.That(first.Fingerprint(), Is.Not.EqualTo(other.Fingerprint()));
            Assert.That(first, Is.Not.EqualTo(other));
        }
    }
}
