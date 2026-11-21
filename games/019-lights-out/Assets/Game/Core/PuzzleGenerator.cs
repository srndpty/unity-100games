using System;

namespace Game.Core
{
    /// <summary>出題する盤と、それを作った既知の解（押下の集合）。</summary>
    public readonly struct Puzzle
    {
        public Puzzle(LightsBoard board, uint knownSolution)
        {
            Board = board;
            KnownSolution = knownSolution;
        }

        public LightsBoard Board { get; }

        /// <summary>全消灯の盤にこの押下を適用すると Board になる。同じ押下で Board は全消灯に戻る。</summary>
        public uint KnownSolution { get; }
    }

    /// <summary>
    /// 既知の解から逆向きに問題を作る。押下は GF(2) の加算なので、全消灯に解を適用した盤は同じ解で必ず全消灯に戻る。
    /// 点灯を直接乱数で決めると 4 分の 3 は解けない（可解な盤は 2^23 / 2^25）ため、そうしない。
    /// </summary>
    public static class PuzzleGenerator
    {
        /// <summary>
        /// 全消灯になった場合に引き直す上限。全消灯になるのは選んだ押下が核の非零の元（3 通り）に一致したときだけなので、
        /// 上限に達することは事実上ない（fail-closed のための保険）。
        /// </summary>
        public const int MaxAttempts = 64;

        public static Puzzle Generate(int seed, int presses)
        {
            LightsOutConfig.RequireScramblePresses(presses);
            var random = new SeededRandom(seed);
            var cells = new int[LightsBoard.CellCount];
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                for (var i = 0; i < cells.Length; i++)
                {
                    cells[i] = i;
                }

                // 部分 Fisher–Yates：先頭 presses 個を重複なく選ぶ。
                uint solution = 0;
                for (var i = 0; i < presses; i++)
                {
                    var pick = i + random.Next(cells.Length - i);
                    (cells[i], cells[pick]) = (cells[pick], cells[i]);
                    solution |= 1u << cells[i];
                }

                var board = LightsBoard.Dark.PressAll(solution);
                if (!board.IsDark)
                {
                    return new Puzzle(board, solution);
                }
            }

            throw new InvalidOperationException($"{MaxAttempts} 回引き直しても全消灯でない盤を作れませんでした。");
        }
    }
}
