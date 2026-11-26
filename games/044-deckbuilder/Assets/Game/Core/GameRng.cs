using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>乱数を使う場面。場面ごとに系列を分け、ある場面の使用回数が他の場面の結果を変えないようにする。</summary>
    public enum RngPurpose
    {
        /// <summary>ルート（各節の遭遇の候補）。</summary>
        Route = 1,

        /// <summary>報酬のカードの候補。</summary>
        Reward = 2,

        /// <summary>戦闘の山札の混ぜ直し。</summary>
        Shuffle = 3,

        /// <summary>敵の行動の抽選。</summary>
        EnemyAi = 4,

        /// <summary>カードの効果の乱択（無作為の対象・無作為の廃棄）。</summary>
        Effect = 5,
    }

    /// <summary>
    /// 状態（<see cref="State"/>）を保存・比較できる決定的な乱数系列（xorshift32）。
    /// 初期シードの撹拌は <see cref="SeededRandom"/> と同じ式を使う（041・043 の GameRng を写した）。
    /// </summary>
    public sealed class GameRng
    {
        private uint _state;

        public GameRng(int seed)
        {
            _state = new SeededRandom(seed).State;
        }

        public uint State => _state;

        /// <summary>試合シード・節（階）・場面から、その場面専用の独立した系列を派生する。</summary>
        public static GameRng For(int matchSeed, int node, RngPurpose purpose)
        {
            unchecked
            {
                var mixed = (uint)matchSeed;
                mixed ^= (uint)(node + 1) * 0x9E3779B9u;
                mixed ^= (uint)purpose * 0x85EBCA6Bu;
                return new GameRng((int)mixed);
            }
        }

        public GameRng Clone() => (GameRng)MemberwiseClone();

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

        /// <summary>重みの表から番号を引く。重みの合計は正で int に収まること（設定の検証で保証する）。</summary>
        public int Weighted(int[] weights)
        {
            var total = 0;
            foreach (var w in weights) { total += w; }
            var roll = Next(total);
            for (int i = 0; i < weights.Length; i++)
            {
                if (roll < weights[i]) { return i; }
                roll -= weights[i];
            }

            throw new InvalidOperationException("重みの合計が正しくありません。");
        }
    }
}
