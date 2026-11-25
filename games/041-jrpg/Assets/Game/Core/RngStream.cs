using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 状態（<see cref="State"/>）をそのまま保存・復元できる決定的な乱数系列（xorshift32）。
    /// 初期シードの撹拌は <see cref="SeededRandom"/> と同じ式を使う。
    /// </summary>
    public sealed class GameRng
    {
        private uint _state;

        public GameRng(int seed)
        {
            _state = new SeededRandom(seed).State;
        }

        private GameRng(uint state)
        {
            if (state == 0)
            {
                throw new ArgumentOutOfRangeException(nameof(state), "乱数の内部状態に 0 は指定できません。");
            }

            _state = state;
        }

        public uint State => _state;

        public static GameRng FromState(uint state) => new(state);

        /// <summary>試合シードと戦闘番号から、その戦闘専用の独立した乱数系列を派生する。</summary>
        public static GameRng ForBattle(int matchSeed, int battleNumber)
        {
            unchecked
            {
                var mixed = (uint)matchSeed ^ ((uint)(battleNumber + 1) * 0x9E3779B9u);
                return new GameRng((int)mixed);
            }
        }

        /// <summary>試合シードから遭遇専用の乱数系列を派生する。</summary>
        public static GameRng ForEncounter(int matchSeed)
        {
            unchecked
            {
                return new GameRng((int)((uint)matchSeed ^ 0xA5A5F00Du));
            }
        }

        public GameRng Clone() => new(_state);

        public uint NextUInt()
        {
            var x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>[0, count) の整数を返す。</summary>
        public int Next(int count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            return (int)(NextUInt() % (uint)count);
        }

        /// <summary>[min, max] の整数を返す。</summary>
        public int Range(int min, int max)
        {
            if (max < min)
            {
                throw new ArgumentOutOfRangeException(nameof(max));
            }

            return min + (int)(NextUInt() % (uint)(max - min + 1));
        }
    }
}
