using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>連鎖1段ぶんの得点計算。</summary>
    public static class ScoreRules
    {
        /// <param name="config">ルール設定。</param>
        /// <param name="chain">1 から数えた連鎖数。</param>
        /// <param name="groups">この段で消えるグループ。</param>
        public static long ChainScore(SporeConfig config, int chain, IReadOnlyList<SporeGroup> groups)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (groups == null) { throw new ArgumentNullException(nameof(groups)); }
            if (chain < 1) { throw new ArgumentOutOfRangeException(nameof(chain), chain, "連鎖数は1以上です。"); }

            var popped = 0;
            var sizeBonus = 0;
            var colors = 0;
            for (var i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                popped += group.Cells.Count;
                sizeBonus += group.Cells.Count - GroupFinder.MinGroupSize;
                colors |= 1 << (int)group.Color;
            }

            if (popped == 0) { return 0; }
            var chainBonus = config.ChainBonuses[Math.Min(chain, config.ChainBonuses.Length) - 1];
            var colorBonus = config.ColorBonus * (CountBits(colors) - 1);
            var multiplier = Math.Max(1, chainBonus + sizeBonus + colorBonus);
            return (long)popped * config.ScorePerSpore * multiplier;
        }

        private static int CountBits(int value)
        {
            var count = 0;
            for (; value != 0; value &= value - 1) { count++; }
            return count;
        }
    }
}
