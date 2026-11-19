using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>連鎖全体を一度に解決した結果。</summary>
    public readonly struct ChainOutcome
    {
        public ChainOutcome(Board board, int chains, long score, int popped)
        {
            Board = board;
            Chains = chains;
            Score = score;
            Popped = popped;
        }

        public Board Board { get; }
        public int Chains { get; }
        public long Score { get; }

        /// <summary>消した色付きの胞子の数。</summary>
        public int Popped { get; }
    }

    /// <summary>消去と連鎖の解決。時間を持たない純粋な関数だけを置く。</summary>
    public static class ChainResolver
    {
        /// <summary>グループと、それに隣接する枯れ胞子を消した盤面を返す。落下はさせない。</summary>
        public static Board Pop(Board board, IReadOnlyList<SporeGroup> groups)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            if (groups == null) { throw new ArgumentNullException(nameof(groups)); }
            var cells = board.ToCells();
            for (var g = 0; g < groups.Count; g++)
            {
                var group = groups[g].Cells;
                for (var i = 0; i < group.Count; i++)
                {
                    var cell = group[i];
                    cells[Board.Index(cell.X, cell.Y)] = Cell.Empty;
                    ClearAsh(cells, cell.X - 1, cell.Y);
                    ClearAsh(cells, cell.X + 1, cell.Y);
                    ClearAsh(cells, cell.X, cell.Y - 1);
                    ClearAsh(cells, cell.X, cell.Y + 1);
                }
            }

            return Board.FromCells(cells);
        }

        /// <summary>落下・判定・消去を、消えるものがなくなるまで繰り返す。</summary>
        public static ChainOutcome ResolveAll(SporeConfig config, Board board)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var current = BoardGravity.Settle(board).Board;
            var chains = 0;
            var score = 0L;
            var popped = 0;
            while (true)
            {
                var groups = GroupFinder.Find(current);
                if (groups.Count == 0) { return new ChainOutcome(current, chains, score, popped); }
                chains++;
                score += ScoreRules.ChainScore(config, chains, groups);
                for (var i = 0; i < groups.Count; i++) { popped += groups[i].Cells.Count; }
                current = BoardGravity.Settle(Pop(current, groups)).Board;
            }
        }

        private static void ClearAsh(Cell[] cells, int x, int y)
        {
            if (Board.IsInside(x, y) && cells[Board.Index(x, y)] == Cell.Ash) { cells[Board.Index(x, y)] = Cell.Empty; }
        }
    }
}
