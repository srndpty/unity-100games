using System;

namespace Game.Core
{
    /// <summary>
    /// 盤面（石の配置）の 64 ビット Zobrist ハッシュの表。交点 × 色ごとの乱数の XOR で、石を置く・取るたびに差分で更新する。
    /// 表は対局のシードと関係ない固定のシードで作る（ハッシュの値は作品の中で常に同じ）。
    /// 空の盤は 0。手番は含めない（同型反復は石の配置だけで比べる）。
    /// </summary>
    public sealed class Zobrist
    {
        private const ulong FixedSeed = 0x055_60_0DD_C0FFEEUL;

        private readonly ulong[] _keys;

        private Zobrist(ulong[] keys)
        {
            _keys = keys;
        }

        /// <summary>作品の標準の表。</summary>
        public static Zobrist Standard { get; } = FromSeed(FixedSeed);

        /// <summary>SplitMix64 で表を作る。</summary>
        public static Zobrist FromSeed(ulong seed)
        {
            var keys = new ulong[Points.Count * 2];
            var state = seed;
            for (var i = 0; i < keys.Length; i++)
            {
                unchecked
                {
                    state += 0x9E3779B97F4A7C15UL;
                    var z = state;
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    keys[i] = z ^ (z >> 31);
                }
            }

            return new Zobrist(keys);
        }

        /// <summary>テスト用：わざと衝突させる表などを差し込む。長さは交点 × 2。</summary>
        internal static Zobrist FromKeys(ulong[] keys)
        {
            if (keys == null || keys.Length != Points.Count * 2) { throw new ArgumentException($"表の長さは {Points.Count * 2} です。", nameof(keys)); }
            return new Zobrist((ulong[])keys.Clone());
        }

        public ulong Key(int point, Stone stone)
        {
            if (!stone.IsColor()) { throw new ArgumentOutOfRangeException(nameof(stone), stone, "空点の鍵はありません。"); }
            return _keys[(point * 2) + (stone == Stone.Black ? 0 : 1)];
        }

        /// <summary>盤の全体から作り直す（差分更新の検算用）。</summary>
        public ulong Compute(Func<int, Stone> at)
        {
            ulong hash = 0;
            for (var p = 0; p < Points.Count; p++)
            {
                var s = at(p);
                if (s != Stone.Empty) { hash ^= Key(p, s); }
            }

            return hash;
        }
    }
}
