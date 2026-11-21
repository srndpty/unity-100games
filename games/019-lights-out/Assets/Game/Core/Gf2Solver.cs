using System;
using System.Collections.ObjectModel;

namespace Game.Core
{
    /// <summary>
    /// 盤を全消灯にする押下の集合を GF(2) の連立方程式として解く。
    /// 未知数 x_i はマス i を押すか（押す順序と 2 回押しは結果に影響しないので、各マス 0 回か 1 回で十分）。
    /// 式はマス j ごとに「j を反転させる押下の和 = 盤の j の点灯」。反転は対称なので、係数行列の行 j は PressMasks の j と同じ。
    /// 5×5 の係数行列は階数 23 で、核（押しても盤が変わらない押下の集合）は 2 次元。可解な盤には解が 4 通りある。
    /// </summary>
    public static class Gf2Solver
    {
        private const int Cells = LightsBoard.CellCount;
        private const uint RhsBit = 1u << Cells;

        private static readonly uint[] _kernelBasis = BuildKernel();

        /// <summary>核の基底。どの組み合わせを押しても盤は変わらない。</summary>
        public static ReadOnlyCollection<uint> Kernel { get; } = Array.AsReadOnly(_kernelBasis);

        /// <summary>全消灯にできるなら true と、その解の 1 つ（自由変数を 0 にしたもの）を返す。</summary>
        public static bool TrySolve(LightsBoard board, out uint solution)
        {
            var rows = new uint[Cells];
            for (var j = 0; j < Cells; j++)
            {
                rows[j] = PressMasks.Of(j) | (board.IsLit(j) ? RhsBit : 0);
            }

            var pivots = Eliminate(rows, out var rank);
            solution = 0;
            for (var r = rank; r < Cells; r++)
            {
                if ((rows[r] & RhsBit) != 0)
                {
                    return false;
                }
            }

            for (var r = 0; r < rank; r++)
            {
                if ((rows[r] & RhsBit) != 0)
                {
                    solution |= 1u << pivots[r];
                }
            }

            return true;
        }

        /// <summary>
        /// 全消灯にできるなら true と、押下数が最少の解を返す。最少が複数あれば数値が最小のものを選ぶ（決定的にするため）。
        /// </summary>
        public static bool TrySolveMinimal(LightsBoard board, out uint solution)
        {
            if (!TrySolve(board, out var particular))
            {
                solution = 0;
                return false;
            }

            solution = particular;
            var combinations = 1 << _kernelBasis.Length;
            for (var combination = 1; combination < combinations; combination++)
            {
                var candidate = particular;
                for (var k = 0; k < _kernelBasis.Length; k++)
                {
                    if ((combination & (1 << k)) != 0)
                    {
                        candidate ^= _kernelBasis[k];
                    }
                }

                var candidateCount = BitCount.Of(candidate);
                var bestCount = BitCount.Of(solution);
                if (candidateCount < bestCount || (candidateCount == bestCount && candidate < solution))
                {
                    solution = candidate;
                }
            }

            return true;
        }

        /// <summary>行を簡約行階段形にし、各行の主元の列を返す。行 rank 以降は係数がすべて 0。</summary>
        private static int[] Eliminate(uint[] rows, out int rank)
        {
            var pivots = new int[Cells];
            rank = 0;
            for (var column = 0; column < Cells && rank < Cells; column++)
            {
                var columnBit = 1u << column;
                var found = -1;
                for (var r = rank; r < Cells; r++)
                {
                    if ((rows[r] & columnBit) != 0)
                    {
                        found = r;
                        break;
                    }
                }

                if (found < 0)
                {
                    continue;
                }

                (rows[rank], rows[found]) = (rows[found], rows[rank]);
                for (var r = 0; r < Cells; r++)
                {
                    if (r != rank && (rows[r] & columnBit) != 0)
                    {
                        rows[r] ^= rows[rank];
                    }
                }

                pivots[rank] = column;
                rank++;
            }

            return pivots;
        }

        private static uint[] BuildKernel()
        {
            var rows = new uint[Cells];
            for (var j = 0; j < Cells; j++)
            {
                rows[j] = PressMasks.Of(j);
            }

            var pivots = Eliminate(rows, out var rank);
            uint pivotColumns = 0;
            for (var r = 0; r < rank; r++)
            {
                pivotColumns |= 1u << pivots[r];
            }

            var basis = new uint[Cells - rank];
            var next = 0;
            for (var free = 0; free < Cells; free++)
            {
                if ((pivotColumns & (1u << free)) != 0)
                {
                    continue;
                }

                // 自由変数 free を 1、他の自由変数を 0 とすると、主元の変数は各行の free 列の係数で決まる。
                var vector = 1u << free;
                for (var r = 0; r < rank; r++)
                {
                    if ((rows[r] & (1u << free)) != 0)
                    {
                        vector |= 1u << pivots[r];
                    }
                }

                basis[next++] = vector;
            }

            return basis;
        }
    }
}
