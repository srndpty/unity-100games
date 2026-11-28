using System;

namespace Game.Core
{
    /// <summary>
    /// 地を這うパスの速さの表。出す速さごとに、転がる球の速さが <see cref="SoccerConfig.PassArriveSpeed"/> に落ちるまでに進む距離を、
    /// <see cref="Ball.Step"/> と同じ整数の減り方（‰ を 0 に向けて切り捨て、遅くなったら止まる）で求めておく。
    /// 切り捨てがあるので、閉じた式（等比級数）より遠くへは届かない。表は設定から 1 回だけ作り、試合の複製で共有する（変えない）。
    /// </summary>
    public sealed class PassTable
    {
        private readonly long[] _reach;
        private readonly int _min;

        public PassTable(SoccerConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _min = config.MinPassSpeed;
            _reach = new long[config.MaxPassSpeed - config.MinPassSpeed + 1];
            for (int i = 0; i < _reach.Length; i++) { _reach[i] = Distance(_min + i, config); }
        }

        public int MinSpeed => _min;

        public int MaxSpeed => _min + _reach.Length - 1;

        /// <summary>速さ <paramref name="speed"/> で出した球が、受け手の速さまで落ちるまでに進む距離。</summary>
        public long Reach(int speed) => _reach[Math.Max(0, Math.Min(_reach.Length - 1, speed - _min))];

        /// <summary>距離 <paramref name="distance"/> の先で受け手の速さが残る、いちばん遅い速さ（届かなければ最大の速さ）。</summary>
        public int SpeedFor(long distance)
        {
            int lo = 0, hi = _reach.Length - 1;
            if (_reach[hi] < distance) { return MaxSpeed; }
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (_reach[mid] >= distance) { hi = mid; }
                else { lo = mid + 1; }
            }

            return _min + lo;
        }

        private static long Distance(int speed, SoccerConfig c)
        {
            long s = speed;
            long total = 0;
            for (int t = 0; t < 10000; t++)
            {
                s = s * (1000 - c.RollFrictionPerMille) / 1000;
                if (s <= c.StopSpeed) { s = 0; }
                total += s;
                if (s <= c.PassArriveSpeed) { break; }
            }

            return total;
        }
    }
}
