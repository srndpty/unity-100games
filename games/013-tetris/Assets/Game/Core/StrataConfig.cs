using System;

namespace Game.Core
{
    /// <summary>ルールの設定。時間はすべて 60Hz の Tick 数で持ち、実行環境による丸めの差をなくす。</summary>
    [Serializable]
    public sealed class StrataConfig
    {
        public const int TickRate = 60;
        public const int ClearableLines = 4;

        private const int MaxLevels = 30;
        private const int MaxTicks = 600;
        private const int MaxLineScore = 100000;
        private const int MaxDropScore = 100;

        /// <summary>レベルごとの、1段落ちるのに要する Tick 数。要素数が最高レベルになる。</summary>
        public int[] GravityTicks = { 48, 40, 33, 27, 22, 18, 14, 11, 9, 7, 5, 4, 3, 2, 1 };

        public int SoftDropTicks = 2;
        public int LockDelayTicks = 30;
        public int MaxLockResets = 15;
        public int LineClearTicks = 18;
        public int LinesPerLevel = 10;

        /// <summary>同時に消した段数（1〜4）ごとの基礎点。</summary>
        public int[] LineScores = { 100, 300, 500, 800 };

        public int SoftDropScore = 1;
        public int HardDropScore = 2;

        public int MaxLevel => GravityTicks.Length;

        public void Validate()
        {
            if (GravityTicks == null || GravityTicks.Length == 0 || GravityTicks.Length > MaxLevels) { throw new ArgumentException($"重力表は1〜{MaxLevels}要素で指定してください。", nameof(GravityTicks)); }
            for (var i = 0; i < GravityTicks.Length; i++)
            {
                RequireRange(GravityTicks[i], 1, MaxTicks, nameof(GravityTicks));
                if (i > 0 && GravityTicks[i] > GravityTicks[i - 1]) { throw new ArgumentException("重力表はレベルが上がるほど速くしてください。", nameof(GravityTicks)); }
            }

            RequireRange(SoftDropTicks, 1, MaxTicks, nameof(SoftDropTicks));
            RequireRange(LockDelayTicks, 1, MaxTicks, nameof(LockDelayTicks));
            RequireRange(MaxLockResets, 0, MaxTicks, nameof(MaxLockResets));
            RequireRange(LineClearTicks, 0, MaxTicks, nameof(LineClearTicks));
            RequireRange(LinesPerLevel, 1, MaxTicks, nameof(LinesPerLevel));
            if (LineScores == null || LineScores.Length != ClearableLines) { throw new ArgumentException($"基礎点は{ClearableLines}要素で指定してください。", nameof(LineScores)); }
            foreach (var score in LineScores) { RequireRange(score, 0, MaxLineScore, nameof(LineScores)); }
            RequireRange(SoftDropScore, 0, MaxDropScore, nameof(SoftDropScore));
            RequireRange(HardDropScore, 0, MaxDropScore, nameof(HardDropScore));
        }

        /// <summary>配列まで複製する。進行中のゲームは設定の書き換えの影響を受けない。</summary>
        public StrataConfig Copy()
        {
            var copy = (StrataConfig)MemberwiseClone();
            copy.GravityTicks = (int[])GravityTicks.Clone();
            copy.LineScores = (int[])LineScores.Clone();
            return copy;
        }

        private static void RequireRange(int value, int min, int max, string name)
        {
            if (value < min || value > max) { throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。"); }
        }
    }
}
