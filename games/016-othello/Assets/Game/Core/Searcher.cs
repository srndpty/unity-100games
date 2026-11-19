using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>探索の結果。</summary>
    public sealed class SearchResult
    {
        public SearchResult(Square move, int score, IReadOnlyList<Square> bestMoves, long nodes)
        {
            Move = move;
            Score = score;
            BestMoves = bestMoves;
            Nodes = nodes;
        }

        /// <summary>選んだ手。</summary>
        public Square Move { get; }

        /// <summary>選んだ手の評価値（手番の側から見た値）。</summary>
        public int Score { get; }

        /// <summary>最善の評価になった手すべて（探索順）。</summary>
        public IReadOnlyList<Square> BestMoves { get; }

        /// <summary>評価した局面の数。</summary>
        public long Nodes { get; }
    }

    /// <summary>
    /// Minimax（negamax 形式）と αβ 枝刈りによる着手の探索。
    /// 手は重みの大きい順に固定の順序で調べるので、乱数を渡さなければ同じ局面で必ず同じ手を返す。
    /// パスは深さを使わず、相手の手番として探索を続ける。
    /// </summary>
    public static class Searcher
    {
        /// <summary>どの評価値よりも大きい値。</summary>
        public const int Infinity = 1_000_000;

        /// <summary>
        /// 手番の側の手を depth 手先まで読んで選ぶ。最善の評価の手が複数あれば random で1つ選ぶ（null なら探索順で最初の手）。
        /// 手番の側に合法手がなければ例外。
        /// </summary>
        public static SearchResult Search(Position position, int depth, SeededRandom? random = null)
        {
            if (position == null) { throw new ArgumentNullException(nameof(position)); }
            if (depth < 1 || depth > OthelloConfig.MaxDepth) { throw new ArgumentOutOfRangeException(nameof(depth), depth, $"1〜{OthelloConfig.MaxDepth} で指定してください。"); }
            var moves = position.LegalMoves();
            if (moves == 0) { throw new InvalidOperationException("手番の側に合法手がありません。"); }

            var counter = new NodeCounter();
            var best = -Infinity;
            var bestMoves = new List<Square>();
            foreach (var index in Evaluator.MoveOrder)
            {
                var bit = 1UL << index;
                if ((moves & bit) == 0) { continue; }
                var square = Square.FromIndex(index);
                var child = position.Apply(bit, position.Flips(square));

                // 同点の手もすべて正確な値で知るため、窓の下限は「最善 - 1」にする（評価値は整数）。
                var score = -Negamax(child, depth - 1, -Infinity, -(best - 1), counter);
                if (score > best)
                {
                    best = score;
                    bestMoves.Clear();
                }

                if (score == best) { bestMoves.Add(square); }
            }

            var chosen = random == null ? bestMoves[0] : bestMoves[random.Next(bestMoves.Count)];
            return new SearchResult(chosen, best, bestMoves.AsReadOnly(), counter.Nodes);
        }

        private static int Negamax(Position position, int depth, int alpha, int beta, NodeCounter counter)
        {
            counter.Nodes++;
            if (depth == 0) { return Evaluator.Evaluate(position); }

            var moves = position.LegalMoves();
            if (moves == 0)
            {
                if (!position.HasLegalMove(position.ToMove.Opponent())) { return Evaluator.Terminal(position); }
                return -Negamax(position.Pass(), depth, -beta, -alpha, counter);
            }

            var best = -Infinity;
            foreach (var index in Evaluator.MoveOrder)
            {
                var bit = 1UL << index;
                if ((moves & bit) == 0) { continue; }
                var child = position.Apply(bit, position.Flips(Square.FromIndex(index)));
                var score = -Negamax(child, depth - 1, -beta, -alpha, counter);
                if (score > best) { best = score; }
                if (best > alpha) { alpha = best; }
                if (alpha >= beta) { break; }
            }

            return best;
        }

        private sealed class NodeCounter
        {
            public long Nodes;
        }
    }
}
