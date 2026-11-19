using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>入れ替えの判定結果。</summary>
    public enum SwapVerdict
    {
        /// <summary>並びができるので成立する。</summary>
        Legal,

        /// <summary>隣接しているが並びができない。入れ替えた後に元へ戻す。</summary>
        NoMatch,

        /// <summary>同じマスどうし。</summary>
        SameCell,

        /// <summary>上下左右に隣り合っていない。</summary>
        NotAdjacent,

        /// <summary>盤面の外を含む。</summary>
        OutOfBoard,

        /// <summary>ゲームが入れ替えを受け付けない（解決中・終了後）。ゲームだけが返す。</summary>
        Busy,
    }

    /// <summary>入れ替えの合法性の判定と、合法手の列挙。</summary>
    public static class SwapRules
    {
        public static SwapVerdict Check(Board board, Point a, Point b)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            if (!Board.IsInside(a) || !Board.IsInside(b)) { return SwapVerdict.OutOfBoard; }
            if (a == b) { return SwapVerdict.SameCell; }
            if (!a.IsAdjacentTo(b)) { return SwapVerdict.NotAdjacent; }
            if (board.At(a) == board.At(b)) { return SwapVerdict.NoMatch; }

            // 並びができるとすれば、必ず入れ替えた2マスのどちらかを含む。
            var swapped = board.Swapped(a, b);
            return MatchFinder.HasMatchAt(swapped, a) || MatchFinder.HasMatchAt(swapped, b) ? SwapVerdict.Legal : SwapVerdict.NoMatch;
        }

        /// <summary>成立する入れ替えをすべて返す。右隣と上隣への入れ替えを、盤面の添字順に並べる。</summary>
        public static IReadOnlyList<Swap> LegalMoves(Board board)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            var moves = new List<Swap>();
            for (var y = 0; y < Board.Height; y++)
            {
                for (var x = 0; x < Board.Width; x++)
                {
                    var from = new Point(x, y);
                    AddIfLegal(board, moves, from, from.Offset(1, 0));
                    AddIfLegal(board, moves, from, from.Offset(0, 1));
                }
            }

            return moves.AsReadOnly();
        }

        public static bool HasLegalMove(Board board) => LegalMoves(board).Count > 0;

        private static void AddIfLegal(Board board, List<Swap> moves, Point from, Point to)
        {
            if (Board.IsInside(to) && Check(board, from, to) == SwapVerdict.Legal) { moves.Add(new Swap(from, to)); }
        }
    }
}
