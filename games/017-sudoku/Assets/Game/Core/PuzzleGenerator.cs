using System;

namespace Game.Core
{
    /// <summary>問題と、その唯一の解。</summary>
    public sealed class Puzzle
    {
        public Puzzle(Grid givens, Grid solution)
        {
            Givens = givens ?? throw new ArgumentNullException(nameof(givens));
            Solution = solution ?? throw new ArgumentNullException(nameof(solution));
            if (!solution.IsComplete || solution.HasConflict) { throw new ArgumentException("解は重複のない完成盤にしてください。", nameof(solution)); }
            for (var i = 0; i < Board.CellCount; i++)
            {
                if (givens[i] != 0 && givens[i] != solution[i]) { throw new ArgumentException("与えが解と一致しません。", nameof(givens)); }
            }
        }

        public Grid Givens { get; }
        public Grid Solution { get; }
    }

    /// <summary>
    /// シードから一意解の問題を作る。
    /// ① 空の盤を数字の順を混ぜたバックトラックで解いて完成盤を作る。
    /// ② 点対称の 2 マス（中央は 1 マス）を混ぜた順に抜き、条件を満たすときだけ抜いたままにする。
    ///    条件は、やさしい・ふつうでは「制約伝播だけで最後まで解ける」（このとき解は必ず 1 つ）、むずかしいでは「解が 1 つ」。
    /// ③ 与えが目標数を下回る組は抜かない。
    /// 乱数は ① と ② で別の系列にする。難しさは ② の条件と目標数だけを変えるので、同じシードなら完成盤は同じ。
    /// </summary>
    public static class PuzzleGenerator
    {
        private const int SolutionStream = 1;
        private const int RemovalStream = 2;

        public static Puzzle Generate(int seed, SudokuConfig config, Difficulty difficulty)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            var target = config.GivensOf(difficulty);
            var logicOnly = SudokuConfig.IsLogicOnly(difficulty);

            var solution = Solver.Solve(Grid.Empty, StreamOf(seed, SolutionStream))
                ?? throw new InvalidOperationException("空の盤が解けませんでした。");

            var random = StreamOf(seed, RemovalStream);
            var order = new int[Board.CellCount / 2 + 1];
            for (var i = 0; i < order.Length; i++) { order[i] = i; }
            for (var i = order.Length - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            var digits = solution.ToDigits();
            var givens = Board.CellCount;
            foreach (var cell in order)
            {
                var mirror = Board.CellCount - 1 - cell;
                var size = cell == mirror ? 1 : 2;
                if (givens - size < target) { continue; }

                var kept = digits[cell];
                digits[cell] = 0;
                digits[mirror] = 0;
                var grid = Grid.FromDigits(digits);
                var accepted = logicOnly
                    ? Propagator.Run(grid).Status == PropagationStatus.Solved
                    : Solver.Count(grid, 2).Solutions == 1;
                if (accepted)
                {
                    givens -= size;
                }
                else
                {
                    digits[cell] = kept;
                    digits[mirror] = solution[mirror];
                }
            }

            return new Puzzle(Grid.FromDigits(digits), solution);
        }

        /// <summary>試合のシードから、場面ごとの乱数の系列を作る。</summary>
        private static SeededRandom StreamOf(int seed, int stream) => new(unchecked(seed * 31 + stream * 0x3C6EF372));
    }
}
