using System;

namespace Game.Core
{
    /// <summary>解の探索の結果。</summary>
    public sealed class SolveResult
    {
        internal SolveResult(int solutions, Grid? first, int branches)
        {
            Solutions = solutions;
            First = first;
            Branches = branches;
        }

        /// <summary>見つけた解の数。探索の上限で打ち切ったときは上限と同じ値。</summary>
        public int Solutions { get; }

        /// <summary>最初に見つけた解。解がなければ null。</summary>
        public Grid? First { get; }

        /// <summary>候補を仮に置いて試した回数。伝播だけで解けたら 0。</summary>
        public int Branches { get; }
    }

    /// <summary>
    /// バックトラックだけを行う。各節点では推論を <see cref="Propagator"/> に任せ、
    /// 伝播で決まらなければ候補の最も少ないマスで分岐する。
    /// </summary>
    public static class Solver
    {
        /// <summary>解を limit 個まで数える。一意かは limit = 2 で調べる。</summary>
        public static SolveResult Count(Grid grid, int limit)
        {
            if (grid == null) { throw new ArgumentNullException(nameof(grid)); }
            if (limit < 1) { throw new ArgumentOutOfRangeException(nameof(limit), limit, "1 以上を指定してください。"); }
            var search = new Search(limit, null);
            search.Run(new CandidateState(grid));
            return new SolveResult(search.Solutions, search.First, search.Branches);
        }

        /// <summary>
        /// 解を 1 つ求める。分岐で試す数字の順を random で混ぜるので、空の盤からはシードごとに異なる完成盤ができる。
        /// </summary>
        public static Grid? Solve(Grid grid, SeededRandom? random = null)
        {
            if (grid == null) { throw new ArgumentNullException(nameof(grid)); }
            var search = new Search(1, random);
            search.Run(new CandidateState(grid));
            return search.First;
        }

        private sealed class Search
        {
            private readonly int _limit;
            private readonly SeededRandom? _random;

            public Search(int limit, SeededRandom? random)
            {
                _limit = limit;
                _random = random;
            }

            public int Solutions { get; private set; }
            public Grid? First { get; private set; }
            public int Branches { get; private set; }

            public void Run(CandidateState state)
            {
                var status = Propagator.Run(state, null);
                if (status == PropagationStatus.Contradiction) { return; }
                if (status == PropagationStatus.Solved)
                {
                    Solutions++;
                    First ??= state.ToGrid();
                    return;
                }

                var cell = MostConstrainedCell(state);
                var digits = DigitOrder(state.Mask(cell));
                foreach (var digit in digits)
                {
                    if (Solutions >= _limit) { return; }
                    Branches++;
                    var next = state.Copy();
                    next.Place(cell, digit);
                    Run(next);
                }
            }

            private int[] DigitOrder(int mask)
            {
                var digits = new int[Digits.Count(mask)];
                var count = 0;
                for (var digit = 1; digit <= Board.Size; digit++)
                {
                    if (Digits.Has(mask, digit)) { digits[count++] = digit; }
                }

                if (_random != null)
                {
                    for (var i = digits.Length - 1; i > 0; i--)
                    {
                        var j = _random.Next(i + 1);
                        (digits[i], digits[j]) = (digits[j], digits[i]);
                    }
                }

                return digits;
            }

            private static int MostConstrainedCell(CandidateState state)
            {
                var best = -1;
                var bestCount = int.MaxValue;
                for (var cell = 0; cell < Board.CellCount; cell++)
                {
                    if (state.Value(cell) != 0) { continue; }
                    var count = Digits.Count(state.Mask(cell));
                    if (count < bestCount)
                    {
                        best = cell;
                        bestCount = count;
                    }
                }

                return best;
            }
        }
    }
}
