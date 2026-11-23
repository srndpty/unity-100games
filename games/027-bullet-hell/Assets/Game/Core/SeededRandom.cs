using System;

namespace Game.Core
{
    /// <summary>
    /// シードから決定的に値を生成する乱数（xorshift32）。
    /// System.Random は実行環境（Mono / .NET）で系列が一致する保証がないため使わない。
    /// </summary>
    public sealed class SeededRandom
    {
        private uint _state;

        public SeededRandom(int seed)
        {
            _state = Scramble(unchecked((uint)seed));
            if (_state == 0)
            {
                _state = 0x9E3779B9u;
            }
        }

        /// <summary>内部状態（状態のハッシュ用）。</summary>
        public uint State => _state;

        public SeededRandom Clone() => (SeededRandom)MemberwiseClone();

        /// <summary>[min, max] の整数。</summary>
        public int Range(int min, int max)
        {
            if (max < min) { throw new ArgumentOutOfRangeException(nameof(max)); }
            return min + (int)(NextUInt() % (uint)(max - min + 1));
        }

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

        // 近いシード同士で序盤の系列が似ないよう、ビットを撹拌する（MurmurHash3 の finalizer）。
        private static uint Scramble(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x85EBCA6Bu;
                value ^= value >> 13;
                value *= 0xC2B2AE35u;
                value ^= value >> 16;
                return value;
            }
        }
    }
}
