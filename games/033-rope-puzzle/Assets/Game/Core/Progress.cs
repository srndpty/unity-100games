using System;

namespace Game.Core
{
    /// <summary>
    /// 問の解放と最少手数（起動中だけ持つ）。最初は 1 問目だけを選べ、解いた問の次の問が解放される。
    /// </summary>
    public sealed class Progress
    {
        private readonly int[] _best;

        public Progress(int count)
        {
            if (count < 1) { throw new ArgumentOutOfRangeException(nameof(count), count, "問の数は 1 以上にしてください。"); }
            _best = new int[count];
            for (var i = 0; i < count; i++) { _best[i] = -1; }
            Unlocked = 1;
        }

        public int Count => _best.Length;

        /// <summary>選べる問の数（先頭から）。</summary>
        public int Unlocked { get; private set; }

        public bool IsUnlocked(int index) => index >= 0 && index < Unlocked;

        public bool IsSolved(int index) => Best(index) >= 0;

        /// <summary>全ての問を解いたか。</summary>
        public bool AllSolved
        {
            get
            {
                for (var i = 0; i < Count; i++)
                {
                    if (_best[i] < 0) { return false; }
                }

                return true;
            }
        }

        /// <summary>解いた最少の手数（まだ解いていなければ -1）。</summary>
        public int Best(int index)
        {
            if ((uint)index >= (uint)Count) { throw new ArgumentOutOfRangeException(nameof(index), index, "問の番号が範囲外です。"); }
            return _best[index];
        }

        /// <summary>問 <paramref name="index"/> を <paramref name="moves"/> 手で解いた。最少手数を更新したら true。次の問を解放する。</summary>
        public bool Record(int index, int moves)
        {
            if (!IsUnlocked(index)) { throw new InvalidOperationException($"{index + 1} 問目はまだ解放されていません。"); }
            if (moves < 0) { throw new ArgumentOutOfRangeException(nameof(moves), moves, "手数は 0 以上にしてください。"); }
            if (index + 1 < Count && Unlocked < index + 2) { Unlocked = index + 2; }
            if (_best[index] >= 0 && _best[index] <= moves) { return false; }
            _best[index] = moves;
            return true;
        }
    }
}
