using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>連鎖の1段：検出した並びを消し、落として、補充した結果。</summary>
    public sealed class CascadeStep
    {
        public CascadeStep(int chain, MatchResult match, Board cleared, Board after, IReadOnlyList<FallMove> falls, int fallRows, long score)
        {
            Chain = chain;
            Match = match;
            Cleared = cleared;
            After = after;
            Falls = falls;
            FallRows = fallRows;
            Score = score;
        }

        /// <summary>何段目の消去か（1 始まり）。</summary>
        public int Chain { get; }

        public MatchResult Match { get; }

        /// <summary>並びを消した直後の盤面（空きを含む）。</summary>
        public Board Cleared { get; }

        /// <summary>落下と補充を終えた盤面。</summary>
        public Board After { get; }

        /// <summary>残った結晶の落下と、補充した結晶の落下。</summary>
        public IReadOnlyList<FallMove> Falls { get; }

        /// <summary>最も長く落ちた段数。</summary>
        public int FallRows { get; }

        public long Score { get; }
    }

    /// <summary>連鎖全体を一度に解決した結果。</summary>
    public sealed class CascadeOutcome
    {
        public CascadeOutcome(Board board, int chains, long score, int cleared)
        {
            Board = board;
            Chains = chains;
            Score = score;
            Cleared = cleared;
        }

        public Board Board { get; }
        public int Chains { get; }
        public long Score { get; }
        public int Cleared { get; }
    }

    /// <summary>消去・落下・補充の組み立て。時間を持たない純粋な関数だけを置く。検出と補充はそれぞれ別の型に任せる。</summary>
    public static class CascadeResolver
    {
        /// <summary>1段の得点。消した数 × 1個の得点 × 連鎖段数。</summary>
        public static long StepScore(int cleared, int chain, int scorePerPiece) => (long)cleared * scorePerPiece * chain;

        /// <summary>上限で頭打ちにする加算。</summary>
        public static long AddScore(long total, long add) => total > long.MaxValue - add ? long.MaxValue : total + add;

        /// <summary>並びのマスを空きにした盤面を返す。</summary>
        public static Board Clear(Board board, MatchResult match)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            if (match == null) { throw new ArgumentNullException(nameof(match)); }
            var cells = board.ToCells();
            for (var i = 0; i < match.Cells.Count; i++) { cells[Board.Index(match.Cells[i].X, match.Cells[i].Y)] = Piece.Empty; }
            return Board.FromCells(cells);
        }

        /// <summary>1段を解決する。並びがなければ null。補充の乱数は並びがあるときだけ引く。</summary>
        public static CascadeStep? Step(Board board, int chain, SeededRandom refill, int colors, int scorePerPiece)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            if (chain < 1) { throw new ArgumentOutOfRangeException(nameof(chain), chain, "連鎖段数は1以上です。"); }
            var match = MatchFinder.Find(board);
            if (match.IsEmpty) { return null; }
            var cleared = Clear(board, match);
            var collapsed = BoardGravity.Collapse(cleared);
            var filled = Refiller.Fill(collapsed.Board, refill, colors);
            var falls = new List<FallMove>(collapsed.Moves.Count + filled.Moves.Count);
            falls.AddRange(collapsed.Moves);
            falls.AddRange(filled.Moves);
            var rows = Math.Max(collapsed.MaxDistance, filled.MaxDistance);
            return new CascadeStep(chain, match, cleared, filled.Board, falls.AsReadOnly(), rows, StepScore(match.Cells.Count, chain, scorePerPiece));
        }

        /// <summary>消える並びがなくなるまで繰り返す。</summary>
        public static CascadeOutcome ResolveAll(Board board, SeededRandom refill, int colors, int scorePerPiece)
        {
            var current = board ?? throw new ArgumentNullException(nameof(board));
            var chains = 0;
            var score = 0L;
            var cleared = 0;
            while (true)
            {
                var step = Step(current, chains + 1, refill, colors, scorePerPiece);
                if (step == null) { return new CascadeOutcome(current, chains, score, cleared); }
                chains++;
                score = AddScore(score, step.Score);
                cleared += step.Match.Cells.Count;
                current = step.After;
            }
        }
    }
}
