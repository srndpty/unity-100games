using System.Linq;
using NUnit.Framework;
using static Game.Core.Tests.Match3TestKit;

namespace Game.Core.Tests
{
    public sealed class MatchFinderTests
    {
        [Test]
        public void BoardWithoutRunsHasNoMatch()
        {
            var result = MatchFinder.Find(DeadBoard());

            Assert.That(result.IsEmpty, Is.True);
            Assert.That(result.Lines, Is.Zero);
        }

        [Test]
        public void HorizontalRunOfThreeIsFound()
        {
            var result = MatchFinder.Find(Parse(
                "LPLPLP",
                "PLPLPL",
                "LPLPLP",
                "PLPLPL",
                "LPLPLP",
                "CCCLPL"));

            Assert.That(result.Cells, Is.EqualTo(new[] { P(0, 0), P(1, 0), P(2, 0) }));
            Assert.That(result.Lines, Is.EqualTo(1));
        }

        [Test]
        public void VerticalRunAtTopEdgeIsFound()
        {
            var result = MatchFinder.Find(Parse(
                "LPLPLC",
                "PLPLPC",
                "LPLPLC",
                "PLPLPL",
                "LPLPLP",
                "PLPLPL"));

            Assert.That(result.Cells, Is.EqualTo(new[] { P(5, 3), P(5, 4), P(5, 5) }));
        }

        [TestCase("CCCCLP", 4)]
        [TestCase("CCCCCP", 5)]
        [TestCase("CCCCCC", 6)]
        public void LongRunIsOneLine(string bottom, int length)
        {
            var result = MatchFinder.Find(Parse(
                "LPLPLP",
                "PLPLPL",
                "LPLPLP",
                "PLPLPL",
                "LPLPLP",
                bottom));

            Assert.That(result.Cells.Count, Is.EqualTo(length));
            Assert.That(result.Lines, Is.EqualTo(1));
        }

        [Test]
        public void CrossCountsSharedCellOnce()
        {
            var result = MatchFinder.Find(Parse(
                "LPLPLP",
                "PLPLPL",
                "LPCPLP",
                "PCCCPL",
                "LPCPLP",
                "PLPLPL"));

            Assert.That(result.Lines, Is.EqualTo(2));
            Assert.That(result.Cells.Count, Is.EqualTo(5));
            Assert.That(result.Cells.Distinct().Count(), Is.EqualTo(5));
        }

        [Test]
        public void LShapeCountsCornerOnce()
        {
            var result = MatchFinder.Find(Parse(
                "LPLPLP",
                "PLPLPL",
                "LPLPLP",
                "CLPLPL",
                "CPLPLP",
                "CCCLPL"));

            Assert.That(result.Lines, Is.EqualTo(2));
            Assert.That(result.Cells, Is.EquivalentTo(new[] { P(0, 0), P(1, 0), P(2, 0), P(0, 1), P(0, 2) }));
        }

        [Test]
        public void TShapeCountsJointOnce()
        {
            var result = MatchFinder.Find(Parse(
                "LPLPLP",
                "PLPLPL",
                "LPLPLP",
                "PLCLPL",
                "LPCPLP",
                "PCCCPL"));

            Assert.That(result.Lines, Is.EqualTo(2));
            Assert.That(result.Cells.Count, Is.EqualTo(5));
        }

        [Test]
        public void SeparateRunsAreBothFound()
        {
            var result = MatchFinder.Find(Parse(
                "AAALPL",
                "PLPLPL",
                "LPLPLP",
                "PLPLPL",
                "LPLPLP",
                "PLPCCC"));

            Assert.That(result.Lines, Is.EqualTo(2));
            Assert.That(result.Cells.Count, Is.EqualTo(6));
        }

        [Test]
        public void EmptyCellsNeverMatch()
        {
            var result = MatchFinder.Find(Parse(
                "...LPL",
                "PLPLPL",
                "LPLPLP",
                "PLPLPL",
                "LPLPLP",
                "PLPLPL"));

            Assert.That(result.IsEmpty, Is.True);
        }

        [Test]
        public void HasMatchAtAgreesWithFind()
        {
            for (var seed = 0; seed < 200; seed++)
            {
                var board = RandomFullBoard(seed);
                var found = MatchFinder.Find(board).Cells;
                for (var y = 0; y < Board.Height; y++)
                {
                    for (var x = 0; x < Board.Width; x++)
                    {
                        Assert.That(MatchFinder.HasMatchAt(board, P(x, y)), Is.EqualTo(found.Contains(P(x, y))), $"seed {seed} ({x}, {y})");
                    }
                }
            }
        }

        // 並びを避けない、3色の一様な盤面。
        internal static Board RandomFullBoard(int seed)
        {
            var random = new SeededRandom(seed);
            var cells = new Piece[Board.CellCount];
            for (var i = 0; i < cells.Length; i++) { cells[i] = Pieces.Color(random.Next(3)); }
            return Board.FromCells(cells);
        }
    }
}
