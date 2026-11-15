using System;

namespace Game.Core
{
    [Serializable]
    public sealed class FlappyConfig
    {
        public const int TickRate = 60;
        public const int MaxCapacity = 8;
        public const float Width = 24;
        public const float Height = 14;
        public const float BirdX = -6;
        public float BirdRadius = .3f;
        public float Gravity = -18;
        public float FlapSpeed = 6;
        public float TerminalSpeed = 10;
        public float PipeWidth = 1.4f;
        public float Spacing = 6;
        public float Speed = 4;
        public float SpeedIncrease = .3f;
        public float Gap = 4.5f;
        public float GapDecrease = .2f;
        public float WallMargin = .8f;
        public float MaxCenterChange = 2;
        public int PointsPerLevel = 5;
        public int MaxLevel = 5;
        public int Capacity => (int)MathF.Ceiling((Width + PipeWidth) / Spacing) + 2;
        public FlappyConfig Copy() => (FlappyConfig)MemberwiseClone();
        public void Validate()
        {
            Range(BirdRadius, .1f, .6f); Range(Gravity, -30, -10); Range(FlapSpeed, 4, 9);
            Range(TerminalSpeed, 6, 16); Range(PipeWidth, .5f, 2); Range(Spacing, 5, 9);
            Range(Speed, 2, 5); Range(SpeedIncrease, 0, .4f); Range(Gap, 3.5f, 6);
            Range(GapDecrease, 0, .25f); Range(WallMargin, .5f, 1.5f); Range(MaxCenterChange, 0, 2);
            if (PointsPerLevel < 1 || PointsPerLevel > 100 || MaxLevel < 0 || MaxLevel > 5
                || (MaxLevel > 0 && (GapDecrease > (float)(((double)Gap - 3.5) / MaxLevel)
                    || SpeedIncrease > (float)((7.0 - Speed) / MaxLevel)))
                || Gap + WallMargin * 2 >= Height || Spacing <= PipeWidth + BirdRadius * 2)
            { throw new ArgumentException("難易度・隙間・間隔の設定が範囲外です。"); }
        }
        private static void Range(float value, float min, float max)
        {
            if (!float.IsFinite(value) || value < min || value > max) { throw new ArgumentException("設定は指定範囲の有限値で指定してください。"); }
        }
    }
}
