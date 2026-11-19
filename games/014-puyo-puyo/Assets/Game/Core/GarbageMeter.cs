using System;

namespace Game.Core
{
    /// <summary>攻撃量の換算と、受けた枯れ胞子の予告を管理する。</summary>
    public sealed class GarbageMeter
    {
        private readonly int _rate;
        private long _carry;

        public GarbageMeter(int rate)
        {
            if (rate <= 0) { throw new ArgumentOutOfRangeException(nameof(rate), rate, "1 以上を指定してください。"); }
            _rate = rate;
        }

        /// <summary>着弾を待っている枯れ胞子の数。</summary>
        public int Pending { get; private set; }

        /// <summary>得点を攻撃に換算する。先に自分の予告と相殺し、残りを相手へ送る数として返す。</summary>
        public int Attack(long score)
        {
            if (score < 0) { throw new ArgumentOutOfRangeException(nameof(score), score, "0 以上を指定してください。"); }
            var total = _carry + score;
            _carry = total % _rate;
            var attack = (int)Math.Min(total / _rate, int.MaxValue);
            var canceled = Math.Min(attack, Pending);
            Pending -= canceled;
            return attack - canceled;
        }

        /// <summary>相手からの攻撃を予告に積む。</summary>
        public void Receive(int count)
        {
            if (count < 0) { throw new ArgumentOutOfRangeException(nameof(count), count, "0 以上を指定してください。"); }
            Pending = (int)Math.Min((long)Pending + count, int.MaxValue);
        }

        /// <summary>今回着弾する数を予告から取り出す。</summary>
        public int TakeDrop(int max)
        {
            if (max <= 0) { throw new ArgumentOutOfRangeException(nameof(max), max, "1 以上を指定してください。"); }
            var drop = Math.Min(Pending, max);
            Pending -= drop;
            return drop;
        }
    }
}
