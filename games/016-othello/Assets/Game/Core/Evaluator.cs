using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Core
{
    /// <summary>
    /// 局面の評価。値は手番の側から見た有利さ。
    /// 終局は石差を大きく拡大して、途中の局面の評価より必ず優先させる。途中は位置の重みと合法手の数の差で見る。
    /// </summary>
    public static class Evaluator
    {
        /// <summary>終局の石差1個あたりの値。途中の局面の評価の絶対値より大きい。</summary>
        public const int TerminalScale = 10_000;

        /// <summary>合法手1つあたりの値。</summary>
        public const int MobilityWeight = 8;

        // 隅は大きく、隅に隣接するマス（X打ち・C打ち）は隅を渡しやすいので負にする。上下左右対称。
        private static readonly int[] _weights =
        {
            120, -20, 20, 5, 5, 20, -20, 120,
            -20, -40, -5, -5, -5, -5, -40, -20,
            20, -5, 15, 3, 3, 15, -5, 20,
            5, -5, 3, 3, 3, 3, -5, 5,
            5, -5, 3, 3, 3, 3, -5, 5,
            20, -5, 15, 3, 3, 15, -5, 20,
            -20, -40, -5, -5, -5, -5, -40, -20,
            120, -20, 20, 5, 5, 20, -20, 120,
        };

        /// <summary>重みの大きい順（同じならマス番号の小さい順）に並べたマス番号。探索の手の順序に使う。</summary>
        internal static IReadOnlyList<int> MoveOrder { get; } = Array.AsReadOnly(
            Enumerable.Range(0, Square.Count).OrderByDescending(i => _weights[i]).ThenBy(i => i).ToArray());

        public static int Weight(Square square) => _weights[square.Index];

        /// <summary>手番の側から見た評価値。</summary>
        public static int Evaluate(Position position)
        {
            if (position == null) { throw new ArgumentNullException(nameof(position)); }
            var me = position.ToMove;
            var myMoves = Bits.PopCount(position.LegalMoves(me));
            var theirMoves = Bits.PopCount(position.LegalMoves(me.Opponent()));
            if (myMoves == 0 && theirMoves == 0) { return Terminal(position); }

            var own = position.Stones(me);
            var other = position.Stones(me.Opponent());
            var score = 0;
            for (var i = 0; i < Square.Count; i++)
            {
                var bit = 1UL << i;
                if ((own & bit) != 0) { score += _weights[i]; }
                else if ((other & bit) != 0) { score -= _weights[i]; }
            }

            return score + (MobilityWeight * (myMoves - theirMoves));
        }

        /// <summary>終局の評価値（手番の側から見た石差 × <see cref="TerminalScale"/>）。</summary>
        public static int Terminal(Position position)
        {
            if (position == null) { throw new ArgumentNullException(nameof(position)); }
            var me = position.ToMove;
            return (position.Count(me) - position.Count(me.Opponent())) * TerminalScale;
        }
    }
}
