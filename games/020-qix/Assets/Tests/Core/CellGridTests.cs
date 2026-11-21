using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class CellGridTests
    {
        [Test]
        public void NewGridHasLandRingAndOpenSea()
        {
            var grid = new CellGrid(7, 5);

            Assert.That(grid.OpenCount, Is.EqualTo(5 * 3));
            Assert.That(grid.TrailCount, Is.Zero);
            for (var y = 0; y < 5; y++)
            {
                for (var x = 0; x < 7; x++)
                {
                    var edge = x == 0 || y == 0 || x == 6 || y == 4;
                    Assert.That(grid[x, y], Is.EqualTo(edge ? Cell.Claimed : Cell.Open), $"({x}, {y})");
                }
            }
        }

        [Test]
        public void BoundaryIsLandTouchingSeaIncludingDiagonals()
        {
            var grid = new CellGrid(7, 5);

            Assert.That(grid.IsBoundary(0, 0), Is.True, "角は斜めの海に接する");
            Assert.That(grid.IsBoundary(3, 0), Is.True);
            Assert.That(grid.IsBoundary(3, 2), Is.False, "海は縁ではない");

            // 左の 2 列を陸にすると、(0, 2) の周りに海がなくなる。
            for (var y = 1; y <= 3; y++)
            {
                grid.Set(1, y, Cell.Claimed);
            }

            Assert.That(grid.IsBoundary(0, 2), Is.False);
            Assert.That(grid.IsBoundary(1, 2), Is.True);
        }

        [Test]
        public void SetKeepsCountsInSync()
        {
            var grid = new CellGrid(6, 6);
            grid.Set(2, 2, Cell.Trail);
            grid.Set(2, 3, Cell.Trail);
            grid.Set(2, 3, Cell.Trail);
            grid.Set(3, 3, Cell.Claimed);

            Assert.That(grid.TrailCount, Is.EqualTo(2));
            Assert.That(grid.OpenCount, Is.EqualTo(16 - 3));

            grid.Set(2, 2, Cell.Open);
            Assert.That(grid.TrailCount, Is.EqualTo(1));
            Assert.That(grid.OpenCount, Is.EqualTo(16 - 2));
        }

        [Test]
        public void VersionChangesOnlyWhenContentChanges()
        {
            var grid = new CellGrid(6, 6);
            var version = grid.Version;

            grid.Set(2, 2, Cell.Open);
            Assert.That(grid.Version, Is.EqualTo(version), "同じ値の書き込みでは変わらない");

            grid.Set(2, 2, Cell.Trail);
            Assert.That(grid.Version, Is.Not.EqualTo(version));
        }

        [Test]
        public void RejectsInvalidArguments()
        {
            var grid = new CellGrid(6, 6);

            Assert.Throws<ArgumentOutOfRangeException>(() => grid.Set(1, 1, (Cell)7));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.Index(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.Index(6, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = grid.IsBoundary(0, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new CellGrid(CellGrid.MinSize - 1, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new CellGrid(10, CellGrid.MaxSize + 1));
            Assert.DoesNotThrow(() => _ = new CellGrid(CellGrid.MinSize, CellGrid.MaxSize));
        }
    }
}
