using System;
using System.Collections.Generic;

namespace Game.Core.Tests
{
    /// <summary>
    /// テスト側の素朴な参照実装。ビット演算を使わず、5×5 の bool 配列を 1 マスずつ書き換える。
    /// </summary>
    internal static class LightsTestKit
    {
        private const int Size = LightsBoard.Size;

        /// <summary>"#" を点灯、"." を消灯として 5 行から盤を作る。</summary>
        public static LightsBoard Parse(params string[] rows)
        {
            if (rows.Length != Size) { throw new ArgumentException("5 行で指定してください。", nameof(rows)); }
            uint bits = 0;
            for (var y = 0; y < Size; y++)
            {
                if (rows[y].Length != Size) { throw new ArgumentException("各行は 5 文字です。", nameof(rows)); }
                for (var x = 0; x < Size; x++)
                {
                    if (rows[y][x] == '#')
                    {
                        bits |= 1u << ((y * Size) + x);
                    }
                }
            }

            return new LightsBoard(bits);
        }

        public static bool[,] ToGrid(LightsBoard board)
        {
            var grid = new bool[Size, Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    grid[x, y] = (board.Bits & (1u << ((y * Size) + x))) != 0;
                }
            }

            return grid;
        }

        public static LightsBoard FromGrid(bool[,] grid)
        {
            uint bits = 0;
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    if (grid[x, y])
                    {
                        bits |= 1u << ((y * Size) + x);
                    }
                }
            }

            return new LightsBoard(bits);
        }

        /// <summary>素朴な押下：自分と、盤内にある上下左右の 4 マスを 1 つずつ反転する。</summary>
        public static void NaivePress(bool[,] grid, int x, int y)
        {
            Toggle(grid, x, y);
            Toggle(grid, x - 1, y);
            Toggle(grid, x + 1, y);
            Toggle(grid, x, y - 1);
            Toggle(grid, x, y + 1);
        }

        /// <summary>
        /// ライト追い：1 行目の押し方 32 通りを総当たりし、2 行目以降は「真上が点灯していたら押す」で一意に決める。
        /// 最後に全消灯になった押し方が、盤のすべての解。
        /// </summary>
        public static List<uint> AllSolutionsByChasing(LightsBoard board)
        {
            var solutions = new List<uint>();
            for (var firstRow = 0; firstRow < (1 << Size); firstRow++)
            {
                var grid = ToGrid(board);
                uint presses = 0;
                for (var x = 0; x < Size; x++)
                {
                    if ((firstRow & (1 << x)) != 0)
                    {
                        NaivePress(grid, x, 0);
                        presses |= 1u << x;
                    }
                }

                for (var y = 1; y < Size; y++)
                {
                    for (var x = 0; x < Size; x++)
                    {
                        if (grid[x, y - 1])
                        {
                            NaivePress(grid, x, y);
                            presses |= 1u << ((y * Size) + x);
                        }
                    }
                }

                if (FromGrid(grid).IsDark)
                {
                    solutions.Add(presses);
                }
            }

            return solutions;
        }

        public static int PopCount(uint value)
        {
            var count = 0;
            for (var bit = 0; bit < 32; bit++)
            {
                if ((value & (1u << bit)) != 0)
                {
                    count++;
                }
            }

            return count;
        }

        public static LightsBoard RandomBoard(SeededRandom random) => new(random.NextUInt() & LightsBoard.AllCells);

        private static void Toggle(bool[,] grid, int x, int y)
        {
            if (x < 0 || x >= Size || y < 0 || y >= Size)
            {
                return;
            }

            grid[x, y] = !grid[x, y];
        }
    }
}
