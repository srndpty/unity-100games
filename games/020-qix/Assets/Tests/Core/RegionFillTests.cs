using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class RegionFillTests
    {
        [Test]
        public void EmptySeaIsOneRegion()
        {
            var grid = new CellGrid(9, 6);

            var map = RegionFill.Label(grid);

            Assert.That(map.RegionCount, Is.EqualTo(1));
            Assert.That(map.SizeOf(0), Is.EqualTo(7 * 4));
            Assert.That(map.RegionOf(grid.Index(0, 0)), Is.EqualTo(RegionMap.None));
        }

        [Test]
        public void WallSplitsSeaAndDiagonalsDoNotConnect()
        {
            var grid = new CellGrid(7, 7);

            // x = 3 の縦の壁で左右に分け、右側は斜めにしか接しない 1 マスを作る。
            for (var y = 1; y <= 5; y++)
            {
                grid.Set(3, y, Cell.Claimed);
            }

            grid.Set(5, 2, Cell.Claimed);
            grid.Set(4, 3, Cell.Claimed);
            grid.Set(5, 4, Cell.Claimed);
            grid.Set(4, 1, Cell.Claimed);
            grid.Set(4, 5, Cell.Claimed);

            var map = RegionFill.Label(grid);

            Assert.That(map.RegionCount, Is.EqualTo(6), "左の 2×5 と、右に残る 5 マスは斜めにしか接しないので別々の領域");
            var left = map.RegionOf(grid.Index(1, 1));
            Assert.That(map.SizeOf(left), Is.EqualTo(10));
            Assert.That(map.RegionOf(grid.Index(5, 3)), Is.Not.EqualTo(map.RegionOf(grid.Index(4, 2))));
        }

        [Test]
        public void TrailIsNotSea()
        {
            var grid = new CellGrid(7, 5);
            for (var y = 1; y <= 3; y++)
            {
                grid.Set(3, y, Cell.Trail);
            }

            Assert.That(RegionFill.Label(grid).RegionCount, Is.EqualTo(2));
        }

        [Test]
        public void MatchesNaiveLabelingOnRandomGrids([Range(0, 199)] int seed)
        {
            var random = new SeededRandom(seed);
            var grid = new CellGrid(5 + random.Next(20), 5 + random.Next(20));
            var density = 1 + random.Next(6);
            for (var y = 1; y < grid.Height - 1; y++)
            {
                for (var x = 1; x < grid.Width - 1; x++)
                {
                    if (random.Next(10) < density) { grid.Set(x, y, random.Next(2) == 0 ? Cell.Claimed : Cell.Trail); }
                }
            }

            var map = RegionFill.Label(grid);
            var naive = NaiveLabels(grid);

            // 2 つの分け方が一致する ⇔ 領域番号どうしの対応が双方向に一対一。
            var toNaive = new Dictionary<int, int>();
            var toFill = new Dictionary<int, int>();
            var sizes = new Dictionary<int, int>();
            for (var i = 0; i < grid.CellCount; i++)
            {
                var region = map.RegionOf(i);
                Assert.That(region == RegionMap.None, Is.EqualTo(naive[i] == RegionMap.None), $"セル {i}");
                if (region == RegionMap.None) { continue; }
                Assert.That(toNaive.TryGetValue(region, out var a) ? a : toNaive[region] = naive[i], Is.EqualTo(naive[i]), $"セル {i}");
                Assert.That(toFill.TryGetValue(naive[i], out var b) ? b : toFill[naive[i]] = region, Is.EqualTo(region), $"セル {i}");
                sizes[region] = sizes.TryGetValue(region, out var n) ? n + 1 : 1;
            }

            Assert.That(sizes.Count, Is.EqualTo(map.RegionCount));
            foreach (var pair in sizes)
            {
                Assert.That(map.SizeOf(pair.Key), Is.EqualTo(pair.Value));
            }
        }

        /// <summary>各海のセルに自分の番号を付け、4 近傍の最小値に寄せることを変化がなくなるまで繰り返す。</summary>
        private static int[] NaiveLabels(CellGrid grid)
        {
            var labels = new int[grid.CellCount];
            for (var i = 0; i < labels.Length; i++)
            {
                labels[i] = grid[i] == Cell.Open ? i : RegionMap.None;
            }

            var changed = true;
            while (changed)
            {
                changed = false;
                for (var y = 0; y < grid.Height; y++)
                {
                    for (var x = 0; x < grid.Width; x++)
                    {
                        var i = grid.Index(x, y);
                        if (labels[i] == RegionMap.None) { continue; }
                        foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                        {
                            if (!grid.Contains(nx, ny)) { continue; }
                            var n = labels[grid.Index(nx, ny)];
                            if (n != RegionMap.None && n < labels[i])
                            {
                                labels[i] = n;
                                changed = true;
                            }
                        }
                    }
                }
            }

            return labels;
        }
    }

    public sealed class TerritoryTests
    {
        [Test]
        public void KeepsOnlyTheRegionWithTheEnemy()
        {
            var grid = Split(out var left, out var right);

            var claimed = Territory.Close(grid, new[] { left });

            Assert.That(grid[left], Is.EqualTo(Cell.Open));
            Assert.That(grid[right], Is.EqualTo(Cell.Claimed));
            Assert.That(grid.OpenCount, Is.EqualTo(3 * 4), "左の 3×4 が残る");
            Assert.That(claimed, Is.EqualTo(4 + 3 * 4), "線 4 セル + 右の 3×4");
            Assert.That(grid.TrailCount, Is.Zero);
        }

        [Test]
        public void KeepsEveryRegionTheEnemyOverlaps()
        {
            var grid = Split(out var left, out var right);

            var claimed = Territory.Close(grid, new[] { left, right });

            Assert.That(claimed, Is.EqualTo(4));
            Assert.That(grid.OpenCount, Is.EqualTo(2 * 3 * 4));
        }

        [Test]
        public void ClaimsAllOtherRegionsWhenSplitIntoThree()
        {
            var grid = new CellGrid(11, 6);
            for (var y = 1; y <= 4; y++)
            {
                grid.Set(3, y, Cell.Trail);
                grid.Set(7, y, Cell.Trail);
            }

            var middle = grid.Index(5, 2);
            Territory.Close(grid, new[] { middle });

            Assert.That(grid.OpenCount, Is.EqualTo(3 * 4));
            Assert.That(grid[1, 1], Is.EqualTo(Cell.Claimed));
            Assert.That(grid[9, 4], Is.EqualTo(Cell.Claimed));
            Assert.That(grid[4, 1], Is.EqualTo(Cell.Open));
        }

        [Test]
        public void RejectsKeepCellThatIsNotSea()
        {
            var grid = Split(out _, out _);
            var snapshot = grid.Hash();

            Assert.Throws<ArgumentException>(() => Territory.Close(grid, new[] { grid.Index(0, 0) }));
            Assert.Throws<ArgumentNullException>(() => Territory.Close(grid, null!));
            Assert.That(grid.Hash(), Is.EqualTo(snapshot), "検証で止まったときは盤を変えない");
        }

        /// <summary>9×6 の盤を x = 4 の線で左右 3×4 ずつに分ける。</summary>
        private static CellGrid Split(out int left, out int right)
        {
            var grid = new CellGrid(9, 6);
            for (var y = 1; y <= 4; y++)
            {
                grid.Set(4, y, Cell.Trail);
            }

            left = grid.Index(2, 2);
            right = grid.Index(6, 2);
            return grid;
        }
    }
}
