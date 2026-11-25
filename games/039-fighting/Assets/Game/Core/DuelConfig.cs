using System;

namespace Game.Core
{
    /// <summary>試合と入力の設定。公開フィールドは ScriptableObject から写す。</summary>
    [Serializable]
    public sealed class DuelConfig
    {
        public int TickRate = 60;

        public int HistoryTicks = 45;

        public int StepWindow = 12;

        public int ButtonWindow = 12;

        public int BufferTicks = 10;

        public int WallMin;

        public int WallMax = 480 * 256;

        public int StartLeft = 176 * 256;

        public int StartRight = 304 * 256;

        public int RoundTicks = 99 * 60;

        public int MaxRounds = 5;

        public int IntroTicks = 45;

        public int RoundEndTicks = 60;

        public int ThrowWhiffGap = 32 * 256;

        public int ThrowHold = 28 * 256;

        public DuelConfig Validate()
        {
            if (TickRate <= 0) { throw new ArgumentOutOfRangeException(nameof(TickRate)); }
            if (StepWindow < 1 || ButtonWindow < 1 || BufferTicks < 1) { throw new ArgumentOutOfRangeException(nameof(StepWindow)); }
            if (HistoryTicks < (StepWindow * 2) + ButtonWindow) { throw new ArgumentOutOfRangeException(nameof(HistoryTicks)); }
            if (WallMax <= WallMin) { throw new ArgumentOutOfRangeException(nameof(WallMax)); }
            if (StartLeft <= WallMin || StartRight >= WallMax || StartLeft >= StartRight)
            {
                throw new ArgumentOutOfRangeException(nameof(StartLeft));
            }

            if (RoundTicks < 1 || MaxRounds < 3 || IntroTicks < 1 || RoundEndTicks < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(RoundTicks));
            }

            if (ThrowWhiffGap <= 0 || ThrowHold <= 0) { throw new ArgumentOutOfRangeException(nameof(ThrowHold)); }
            return this;
        }

        public DuelConfig Copy() => new()
        {
            TickRate = TickRate,
            HistoryTicks = HistoryTicks,
            StepWindow = StepWindow,
            ButtonWindow = ButtonWindow,
            BufferTicks = BufferTicks,
            WallMin = WallMin,
            WallMax = WallMax,
            StartLeft = StartLeft,
            StartRight = StartRight,
            RoundTicks = RoundTicks,
            MaxRounds = MaxRounds,
            IntroTicks = IntroTicks,
            RoundEndTicks = RoundEndTicks,
            ThrowWhiffGap = ThrowWhiffGap,
            ThrowHold = ThrowHold,
        };
    }
}
