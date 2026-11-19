using System;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class PieceDataTests
    {
        private static readonly PieceKind[] _kinds = { PieceKind.I, PieceKind.O, PieceKind.T, PieceKind.S, PieceKind.Z, PieceKind.J, PieceKind.L };
        private static readonly Rotation[] _rotations = { Rotation.North, Rotation.East, Rotation.South, Rotation.West };
        private static readonly PieceKind[] _turningKinds = _kinds.Where(kind => kind != PieceKind.O).ToArray();

        [Test]
        public void EveryShapeHasFourDistinctCellsInsideItsBox()
        {
            foreach (var kind in _kinds)
            {
                foreach (var rotation in _rotations)
                {
                    var cells = PieceShapes.Cells(kind, rotation);
                    var size = PieceShapes.BoxSize(kind);
                    Assert.That(cells.Count, Is.EqualTo(PieceShapes.CellCount));
                    Assert.That(cells.Distinct().Count(), Is.EqualTo(PieceShapes.CellCount));
                    Assert.That(cells.All(cell => cell.X >= 0 && cell.X < size && cell.Y >= 0 && cell.Y < size), Is.True, $"{kind} {rotation}");
                }
            }
        }

        [Test]
        public void LongPieceTurnsAroundTheCenterOfItsBox()
        {
            Assert.That(PieceShapes.Cells(PieceKind.I, Rotation.East).All(cell => cell.X == 2), Is.True);
            Assert.That(PieceShapes.Cells(PieceKind.I, Rotation.South).All(cell => cell.Y == 1), Is.True);
            Assert.That(PieceShapes.Cells(PieceKind.I, Rotation.West).All(cell => cell.X == 1), Is.True);
        }

        [Test]
        public void SquarePieceLooksTheSameInEveryRotation()
        {
            var north = PieceShapes.Cells(PieceKind.O, Rotation.North).OrderBy(cell => cell.X).ThenBy(cell => cell.Y);
            foreach (var rotation in _rotations)
            {
                Assert.That(PieceShapes.Cells(PieceKind.O, rotation).OrderBy(cell => cell.X).ThenBy(cell => cell.Y), Is.EqualTo(north));
            }
        }

        [Test]
        public void TurnWrapsAroundInBothDirections()
        {
            Assert.That(PieceShapes.Turn(Rotation.West, true), Is.EqualTo(Rotation.North));
            Assert.That(PieceShapes.Turn(Rotation.North, false), Is.EqualTo(Rotation.West));
        }

        [Test]
        public void UndefinedValuesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PieceShapes.Cells((PieceKind)7, Rotation.North));
            Assert.Throws<ArgumentOutOfRangeException>(() => PieceShapes.Cells(PieceKind.T, (Rotation)4));
            Assert.Throws<ArgumentOutOfRangeException>(() => KickTable.Offsets((PieceKind)(-1), Rotation.North, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => KickTable.Offsets(PieceKind.T, (Rotation)4, true));
        }

        [Test]
        public void EveryTurnHasFiveCandidatesStartingWithNoOffset()
        {
            foreach (var kind in _turningKinds)
            {
                foreach (var rotation in _rotations)
                {
                    foreach (var clockwise in new[] { true, false })
                    {
                        var offsets = KickTable.Offsets(kind, rotation, clockwise);
                        Assert.That(offsets.Count, Is.EqualTo(KickTable.CandidateCount));
                        Assert.That(offsets[0], Is.EqualTo(new Cell(0, 0)));
                    }
                }
            }
        }

        [Test]
        public void ReverseTurnUsesTheOppositeOffsets()
        {
            foreach (var kind in _turningKinds)
            {
                foreach (var rotation in _rotations)
                {
                    var forward = KickTable.Offsets(kind, rotation, true);
                    var backward = KickTable.Offsets(kind, PieceShapes.Turn(rotation, true), false);
                    Assert.That(backward, Is.EqualTo(forward.Select(cell => new Cell(-cell.X, -cell.Y))), $"{kind} {rotation}");
                }
            }
        }

        [Test]
        public void SquarePieceHasNoKicks()
        {
            Assert.That(KickTable.Offsets(PieceKind.O, Rotation.North, true), Is.Empty);
        }

        [Test]
        public void PublishedDataCannotBeWrittenThroughACast()
        {
            Assert.That(PieceShapes.Cells(PieceKind.T, Rotation.North), Is.Not.InstanceOf<Cell[]>());
            Assert.That(KickTable.Offsets(PieceKind.T, Rotation.North, true), Is.Not.InstanceOf<Cell[]>());
        }

        [TestCase(PieceKind.I, 3, 18)]
        [TestCase(PieceKind.O, 4, 20)]
        [TestCase(PieceKind.T, 3, 19)]
        public void SpawnPutsTheLowestCellOnTheFirstHiddenRow(PieceKind kind, int x, int y)
        {
            var piece = ActivePiece.Spawn(kind);
            Assert.That((piece.X, piece.Y), Is.EqualTo((x, y)));
            Assert.That(piece.LowestRow, Is.EqualTo(Board.VisibleHeight));
        }
    }
}
