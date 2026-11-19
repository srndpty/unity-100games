using System;
using Game.Core;

namespace Game.App
{
    /// <summary>合法手からシード付きの乱数で1手選ぶ操縦役。撮影台本とテストで共有する。</summary>
    public sealed class Autopilot
    {
        private readonly SeededRandom _random;

        public Autopilot(int seed) => _random = new SeededRandom(seed);

        /// <summary>入れ替えを受け付ける盤面には必ず合法手がある（なければゲームが作り直す）。</summary>
        public Swap Choose(Board board)
        {
            var moves = SwapRules.LegalMoves(board);
            if (moves.Count == 0) { throw new InvalidOperationException("合法手のない盤面です。"); }
            return moves[_random.Next(moves.Count)];
        }
    }
}
