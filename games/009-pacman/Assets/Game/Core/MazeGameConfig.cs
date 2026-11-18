using System;

namespace Game.Core
{
    /// <summary>
    /// 遊びの調整値。速度は 0.1マス/秒 単位で、60Hz ではこの値が 1Tick の移動量（1マス=600）と一致する。
    /// 時間はすべて Tick（60Hz）で持つ。
    /// </summary>
    [Serializable]
    public sealed class MazeGameConfig
    {
        public const int TickRate = 60;
        public const int CatCount = 4;
        public const int SubstepsPerTick = 4;
        public const int MaxSpeed = 200;
        public const int MinContactRadius = 120;
        public const int MaxContactRadius = 450;

        public int PlayerSpeed = 75;
        public int CatSpeed = 70;
        public int FrightenedSpeed = 45;
        public int ReturningSpeed = 140;
        public int LeavingSpeed = 40;
        public int ContactRadius = 270;
        public int Lives = 3;
        public int ReadyTicks = 120;
        public int DyingTicks = 90;
        public int PowerTicks = 420;
        public int DotScore = 10;
        public int PowerScore = 50;
        public int[] CatScores = { 200, 400, 800, 1600 };

        /// <summary>巡回→追跡→巡回…の順の長さ。使い切ると以後は追跡を続ける。</summary>
        public int[] ModeTicks = { 420, 1200, 420, 1200, 300 };

        /// <summary>ラウンド開始から寝床を出るまでの Tick。先頭のトラは最初から外にいるので使わない。</summary>
        public int[] ReleaseTicks = { 0, 120, 300, 540 };

        public int AmbushLookahead = 4;
        public int PincerLookahead = 2;
        public int ShyRadius = 8;

        public void Validate()
        {
            RequireRange(PlayerSpeed, 1, MaxSpeed, nameof(PlayerSpeed));
            RequireRange(CatSpeed, 1, MaxSpeed, nameof(CatSpeed));
            RequireRange(FrightenedSpeed, 1, MaxSpeed, nameof(FrightenedSpeed));
            RequireRange(ReturningSpeed, 1, MaxSpeed, nameof(ReturningSpeed));
            RequireRange(LeavingSpeed, 1, MaxSpeed, nameof(LeavingSpeed));
            RequireRange(ContactRadius, MinContactRadius, MaxContactRadius, nameof(ContactRadius));
            RequireRange(Lives, 1, 9, nameof(Lives));
            RequireRange(ReadyTicks, 1, 600, nameof(ReadyTicks));
            RequireRange(DyingTicks, 1, 600, nameof(DyingTicks));
            RequireRange(PowerTicks, 1, 3600, nameof(PowerTicks));
            RequireRange(DotScore, 0, 10000, nameof(DotScore));
            RequireRange(PowerScore, 0, 10000, nameof(PowerScore));
            RequireRange(AmbushLookahead, 0, 16, nameof(AmbushLookahead));
            RequireRange(PincerLookahead, 0, 16, nameof(PincerLookahead));
            RequireRange(ShyRadius, 1, 32, nameof(ShyRadius));

            if (CatScores == null || CatScores.Length != CatCount) { throw new ArgumentException("猫の得点は4段階必要です。", nameof(CatScores)); }
            foreach (var score in CatScores) { RequireRange(score, 0, 100000, nameof(CatScores)); }
            if (ModeTicks == null || ModeTicks.Length > 16) { throw new ArgumentException("巡回と追跡の予定が不正です。", nameof(ModeTicks)); }
            foreach (var ticks in ModeTicks) { RequireRange(ticks, 1, 36000, nameof(ModeTicks)); }
            if (ReleaseTicks == null || ReleaseTicks.Length != CatCount) { throw new ArgumentException("退出時間は4匹分必要です。", nameof(ReleaseTicks)); }
            foreach (var ticks in ReleaseTicks) { RequireRange(ticks, 0, int.MaxValue, nameof(ReleaseTicks)); }
        }

        public MazeGameConfig Copy()
        {
            var copy = (MazeGameConfig)MemberwiseClone();
            copy.CatScores = (int[])CatScores.Clone();
            copy.ModeTicks = (int[])ModeTicks.Clone();
            copy.ReleaseTicks = (int[])ReleaseTicks.Clone();
            return copy;
        }

        private static void RequireRange(int value, int min, int max, string name)
        {
            if (value < min || value > max) { throw new ArgumentOutOfRangeException(name, value, "設定値が許容範囲外です。"); }
        }
    }
}
