using System;

namespace Game.Core
{
    /// <summary>ルールの設定。時間はすべて 60Hz の Tick 数で持ち、実行環境による丸めの差をなくす。</summary>
    [Serializable]
    public sealed class SporeConfig
    {
        public const int TickRate = 60;

        private const int MaxTicks = 3600;
        private const int MaxTableLength = 30;
        private const int MaxBonus = 10000;

        /// <summary>重力の段階ごとの、1段落ちるのに要する Tick 数。</summary>
        public int[] GravityTicks = { 40, 32, 26, 20, 15, 11, 8, 6 };

        /// <summary>重力の段階が1つ上がるまでの Tick 数。</summary>
        public int TicksPerGravityStep = 1800;

        public int SoftDropTicks = 2;
        public int LockDelayTicks = 30;
        public int MaxLockResets = 8;

        /// <summary>分離落下と着弾で1段落ちるのに要する Tick 数。</summary>
        public int SettleTicksPerRow = 2;

        public int PopTicks = 24;
        public int SpawnDelayTicks = 6;

        public int ScorePerSpore = 10;

        /// <summary>連鎖数ごとの倍率。表より長い連鎖は最後の値を使う。</summary>
        public int[] ChainBonuses = { 0, 4, 8, 16, 32, 64, 96, 128 };

        /// <summary>同時に消した色が1色増えるごとの倍率。</summary>
        public int ColorBonus = 3;

        /// <summary>枯れ胞子1個に換算する得点。</summary>
        public int GarbageRate = 70;

        /// <summary>1回に着弾する枯れ胞子の上限。</summary>
        public int MaxGarbagePerDrop = 30;

        public void Validate()
        {
            RequireTable(GravityTicks, 1, MaxTicks, nameof(GravityTicks));
            for (var i = 1; i < GravityTicks.Length; i++)
            {
                if (GravityTicks[i] > GravityTicks[i - 1]) { throw new ArgumentException("重力表は段階が上がるほど速くしてください。", nameof(GravityTicks)); }
            }

            RequireRange(TicksPerGravityStep, 1, int.MaxValue, nameof(TicksPerGravityStep));
            RequireRange(SoftDropTicks, 1, MaxTicks, nameof(SoftDropTicks));
            RequireRange(LockDelayTicks, 1, MaxTicks, nameof(LockDelayTicks));
            RequireRange(MaxLockResets, 0, MaxTicks, nameof(MaxLockResets));
            RequireRange(SettleTicksPerRow, 0, MaxTicks, nameof(SettleTicksPerRow));
            RequireRange(PopTicks, 0, MaxTicks, nameof(PopTicks));
            RequireRange(SpawnDelayTicks, 0, MaxTicks, nameof(SpawnDelayTicks));
            RequireRange(ScorePerSpore, 1, MaxBonus, nameof(ScorePerSpore));
            RequireTable(ChainBonuses, 0, MaxBonus, nameof(ChainBonuses));
            RequireRange(ColorBonus, 0, MaxBonus, nameof(ColorBonus));
            RequireRange(GarbageRate, 1, MaxBonus, nameof(GarbageRate));
            RequireRange(MaxGarbagePerDrop, 1, Board.Width * Board.Height, nameof(MaxGarbagePerDrop));
        }

        /// <summary>配列まで複製する。進行中の試合は設定の書き換えの影響を受けない。</summary>
        public SporeConfig Copy()
        {
            var copy = (SporeConfig)MemberwiseClone();
            copy.GravityTicks = (int[])GravityTicks.Clone();
            copy.ChainBonuses = (int[])ChainBonuses.Clone();
            return copy;
        }

        private static void RequireTable(int[] table, int min, int max, string name)
        {
            if (table == null || table.Length == 0 || table.Length > MaxTableLength) { throw new ArgumentException($"1〜{MaxTableLength}要素で指定してください。", name); }
            foreach (var value in table) { RequireRange(value, min, max, name); }
        }

        private static void RequireRange(int value, int min, int max, string name)
        {
            if (value < min || value > max) { throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。"); }
        }
    }
}
