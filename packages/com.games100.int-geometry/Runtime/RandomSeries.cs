using System;

namespace IntGeometry
{
    /// <summary>
    /// 状態（<see cref="State"/>）を保存・復元・比較できる決定的な乱数系列（xorshift32）。
    /// 初期シードの撹拌は <see cref="SeededRandom"/> と同じ式を使う。
    /// 試合シード・番号（節・階・戦闘）・用途から、場面ごとに独立した系列を派生できる（<see cref="Derive"/>）。
    /// 041・043・044 の GameRng から抽出した。
    /// </summary>
    public sealed class RandomSeries
    {
        private uint _state;

        public RandomSeries(int seed)
        {
            _state = new SeededRandom(seed).State;
        }

        private RandomSeries(uint state)
        {
            _state = state;
        }

        /// <summary>内部状態。保存とハッシュに使う。</summary>
        public uint State => _state;

        /// <summary>保存した内部状態から系列を復元する。xorshift は 0 で止まるので 0 は拒否する。</summary>
        public static RandomSeries FromState(uint state)
        {
            if (state == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(state), "乱数の内部状態に 0 は指定できません。");
            }

            return new RandomSeries(state);
        }

        /// <summary>
        /// 試合シード・番号・用途から、その場面専用の独立した系列を派生する。
        /// 用途 0 は「試合シードと番号だけ」の派生と同じ値になる。
        /// </summary>
        public static RandomSeries Derive(int matchSeed, int node, int purpose)
        {
            unchecked
            {
                var mixed = (uint)matchSeed;
                mixed ^= (uint)(node + 1) * 0x9E3779B9u;
                mixed ^= (uint)purpose * 0x85EBCA6Bu;
                return new RandomSeries((int)mixed);
            }
        }

        public RandomSeries Clone() => new(_state);

        public uint NextUInt()
        {
            var x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>[0, count) の整数。</summary>
        public int Next(int count)
        {
            if (count <= 0) { throw new ArgumentOutOfRangeException(nameof(count)); }
            return (int)(NextUInt() % (uint)count);
        }

        /// <summary>[min, max] の整数。</summary>
        public int Range(int min, int max)
        {
            if (max < min) { throw new ArgumentOutOfRangeException(nameof(max)); }
            return min + (int)(NextUInt() % (uint)(max - min + 1));
        }

        /// <summary>percent % の確率で true。</summary>
        public bool Chance(int percent) => Next(100) < percent;

        /// <summary>重みの表から番号を引く。重みは 0 以上で、合計は正かつ int に収まること（呼び出し側の検証で保証する）。</summary>
        public int Weighted(int[] weights)
        {
            var total = 0L;
            foreach (var w in weights)
            {
                if (w < 0) { throw new ArgumentOutOfRangeException(nameof(weights), "重みに負の値があります。"); }
                total += w;
            }

            if (total <= 0 || total > int.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(weights), "重みの合計は 1 以上 int の範囲内でなければなりません。");
            }

            var roll = Next((int)total);
            for (int i = 0; i < weights.Length; i++)
            {
                if (roll < weights[i]) { return i; }
                roll -= weights[i];
            }

            throw new InvalidOperationException("重みの合計が正しくありません。");
        }
    }
}
