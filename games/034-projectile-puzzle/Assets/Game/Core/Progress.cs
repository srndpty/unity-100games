using System;

namespace Game.Core
{
    /// <summary>
    /// 面の解放と最高点・星（起動中だけ持つ）。最初は 1 面だけを選べ、勝った面の次の面が解放される。
    /// </summary>
    public sealed class Progress
    {
        private readonly int[] _best;
        private readonly int[] _stars;

        public Progress(int count)
        {
            if (count < 1) { throw new ArgumentOutOfRangeException(nameof(count), count, "面の数は 1 以上にしてください。"); }
            _best = new int[count];
            _stars = new int[count];
            for (var i = 0; i < count; i++) { _best[i] = -1; }
            Unlocked = 1;
        }

        public int Count => _best.Length;

        /// <summary>選べる面の数（先頭から）。</summary>
        public int Unlocked { get; private set; }

        public bool IsUnlocked(int index) => index >= 0 && index < Unlocked;

        public bool IsCleared(int index) => Best(index) >= 0;

        public bool AllCleared
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

        /// <summary>勝った最高点（まだ勝っていなければ -1）。</summary>
        public int Best(int index) => _best[Check(index)];

        /// <summary>勝った最多の星（まだ勝っていなければ 0）。</summary>
        public int Stars(int index) => _stars[Check(index)];

        /// <summary>全ての面の最高点の和（勝っていない面は 0）。</summary>
        public long Total
        {
            get
            {
                long total = 0;
                foreach (var best in _best) { total += Math.Max(0, best); }
                return total;
            }
        }

        /// <summary>面 <paramref name="index"/> に <paramref name="score"/> 点・星 <paramref name="stars"/> で勝った。最高点を更新したら true。次の面を解放する。</summary>
        public bool Record(int index, int score, int stars)
        {
            if (!IsUnlocked(index)) { throw new InvalidOperationException($"{index + 1} 面はまだ解放されていません。"); }
            if (score < 0) { throw new ArgumentOutOfRangeException(nameof(score), score, "得点は 0 以上にしてください。"); }
            if (stars < 1 || stars > 3) { throw new ArgumentOutOfRangeException(nameof(stars), stars, "勝ったときの星は 1〜3 です。"); }
            if (index + 1 < Count && Unlocked < index + 2) { Unlocked = index + 2; }
            _stars[index] = Math.Max(_stars[index], stars);
            if (_best[index] >= score) { return false; }
            _best[index] = score;
            return true;
        }

        private int Check(int index)
        {
            if ((uint)index >= (uint)Count) { throw new ArgumentOutOfRangeException(nameof(index), index, "面の番号が範囲外です。"); }
            return index;
        }
    }
}
