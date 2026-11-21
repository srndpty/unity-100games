using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>海のセルを 4 連結の領域に分けた結果。</summary>
    public sealed class RegionMap
    {
        public const int None = -1;

        private readonly int[] _labels;
        private readonly int[] _sizes;

        internal RegionMap(int[] labels, int[] sizes)
        {
            _labels = labels;
            _sizes = sizes;
        }

        public int RegionCount => _sizes.Length;

        /// <summary>セルの領域番号。海でなければ <see cref="None"/>。</summary>
        public int RegionOf(int index) => _labels[index];

        public int SizeOf(int region) => _sizes[region];
    }

    /// <summary>領域の確定に使う Flood Fill。再帰せず明示的なスタックで塗る。</summary>
    public static class RegionFill
    {
        public static RegionMap Label(CellGrid grid)
        {
            if (grid == null) { throw new ArgumentNullException(nameof(grid)); }

            var labels = new int[grid.CellCount];
            for (var i = 0; i < labels.Length; i++)
            {
                labels[i] = RegionMap.None;
            }

            var sizes = new List<int>();
            var stack = new Stack<int>();
            for (var start = 0; start < labels.Length; start++)
            {
                if (grid[start] != Cell.Open || labels[start] != RegionMap.None)
                {
                    continue;
                }

                var region = sizes.Count;
                var size = 0;
                labels[start] = region;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    var index = stack.Pop();
                    size++;
                    var x = index % grid.Width;
                    var y = index / grid.Width;
                    Visit(grid, labels, stack, region, x - 1, y);
                    Visit(grid, labels, stack, region, x + 1, y);
                    Visit(grid, labels, stack, region, x, y - 1);
                    Visit(grid, labels, stack, region, x, y + 1);
                }

                sizes.Add(size);
            }

            return new RegionMap(labels, sizes.ToArray());
        }

        private static void Visit(CellGrid grid, int[] labels, Stack<int> stack, int region, int x, int y)
        {
            if (!grid.Contains(x, y))
            {
                return;
            }

            var index = grid.Index(x, y);
            if (grid[index] == Cell.Open && labels[index] == RegionMap.None)
            {
                labels[index] = region;
                stack.Push(index);
            }
        }
    }

    /// <summary>線を閉じたときの領域の確定。</summary>
    public static class Territory
    {
        /// <summary>
        /// 線を陸にし、海を領域に分け、<paramref name="keepCells"/>（敵が重なる海のセル）を含む領域だけを残して他を陸にする。
        /// 陸になったセルの数（線を含む）を返す。
        /// </summary>
        public static int Close(CellGrid grid, IReadOnlyCollection<int> keepCells)
        {
            if (grid == null) { throw new ArgumentNullException(nameof(grid)); }
            if (keepCells == null) { throw new ArgumentNullException(nameof(keepCells)); }

            foreach (var cell in keepCells)
            {
                if (cell < 0 || cell >= grid.CellCount || grid[cell] != Cell.Open)
                {
                    throw new ArgumentException($"残すセル {cell} が海ではありません。", nameof(keepCells));
                }
            }

            var claimed = 0;
            for (var i = 0; i < grid.CellCount; i++)
            {
                if (grid[i] == Cell.Trail)
                {
                    grid.Set(i, Cell.Claimed);
                    claimed++;
                }
            }

            var map = RegionFill.Label(grid);
            var keep = new bool[map.RegionCount];
            foreach (var cell in keepCells)
            {
                keep[map.RegionOf(cell)] = true;
            }

            for (var i = 0; i < grid.CellCount; i++)
            {
                var region = map.RegionOf(i);
                if (region != RegionMap.None && !keep[region])
                {
                    grid.Set(i, Cell.Claimed);
                    claimed++;
                }
            }

            return claimed;
        }
    }
}
