using System;

namespace Game.App
{
    /// <summary>起動中だけ保持するクリア記録。ファイルには保存しない。</summary>
    public sealed class SessionRecords
    {
        private readonly int?[] _bestMoves;

        public SessionRecords(int levelCount)
        {
            if (levelCount <= 0) { throw new ArgumentOutOfRangeException(nameof(levelCount), levelCount, "面の数は1以上です。"); }
            _bestMoves = new int?[levelCount];
        }

        public int ClearedCount { get; private set; }
        public bool IsAllCleared => ClearedCount == _bestMoves.Length;

        public bool IsCleared(int level) => _bestMoves[level] != null;

        /// <summary>その面の最少手数。未クリアなら null。</summary>
        public int? BestMoves(int level) => _bestMoves[level];

        public void Record(int level, int moves)
        {
            if (moves <= 0) { throw new ArgumentOutOfRangeException(nameof(moves), moves, "手数は1以上です。"); }
            var best = _bestMoves[level];
            if (best == null) { ClearedCount++; }
            if (best == null || moves < best) { _bestMoves[level] = moves; }
        }
    }
}
