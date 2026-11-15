using System;

namespace Game.Core
{
    [Serializable]
    public sealed class AsteroidsConfig
    {
        public const int TickRate = 60;
        public const float Width = 32;
        public const float Height = 18;
        public const float ShipRadius = .35f;
        public const float BulletRadius = .08f;
        public const int AsteroidCapacity = 32;
        public int WaveCount = 3;
        public int FirstWaveCount = 3;
        public int WaveIncrease = 1;
        public int Lives = 3;
        public int BulletLimit = 8;
        public float Acceleration = 8;
        public float MaxSpeed = 8;
        public float RotationDegrees = 240;
        public float BulletSpeed = 18;
        public float BulletSeconds = 1.2f;
        public float FireSeconds = .2f;
        public float LargeRadius = 1.2f;
        public float MediumRadius = .65f;
        public float SmallRadius = .35f;
        public float AsteroidMinSpeed = 1.2f;
        public float AsteroidMaxSpeed = 2;
        public float SplitSpeed = .8f;
        public float RespawnSeconds = 1;
        public float TransitionSeconds = 1;
        public float InvincibleSeconds = 2;

        public AsteroidsConfig Copy() => (AsteroidsConfig)MemberwiseClone();
        public float Radius(int size) => size == 2 ? LargeRadius : size == 1 ? MediumRadius : SmallRadius;
        public static int Ticks(float seconds)
        {
            if (!float.IsFinite(seconds) || seconds < 0 || seconds > 60) { throw new ArgumentOutOfRangeException(nameof(seconds)); }
            var ticks = (double)seconds * TickRate; var nearest = Math.Round(ticks);
            return (int)((double)seconds == (double)(float)(nearest / TickRate) ? nearest : Math.Ceiling(ticks));
        }
        public void Validate()
        {
            if (WaveCount < 1 || WaveCount > 5 || FirstWaveCount < 1 || FirstWaveCount > 8 || WaveIncrease < 0 || WaveIncrease > 7
                || FirstWaveCount + (WaveCount - 1) * WaveIncrease > 8 || Lives < 1 || Lives > 9 || BulletLimit < 1 || BulletLimit > 16)
            { throw new ArgumentException("Wave・残機・弾数が許容範囲外です。"); }
            Check(Acceleration, .1f, 30); Check(MaxSpeed, 1, 16); Check(RotationDegrees, 30, 720);
            Check(BulletSpeed, 1, 120); Check(BulletSeconds, 1f / TickRate, 5); Check(FireSeconds, 1f / TickRate, 2);
            Check(LargeRadius, .8f, 1.5f); Check(MediumRadius, .4f, .75f); Check(SmallRadius, .15f, .39f);
            Check(AsteroidMinSpeed, .1f, 4); Check(AsteroidMaxSpeed, AsteroidMinSpeed, 6); Check(SplitSpeed, .1f, 3);
            Check(RespawnSeconds, 1f / TickRate, 3); Check(TransitionSeconds, 1f / TickRate, 3); Check(InvincibleSeconds, 1f / TickRate, 5);
            // 最速弾136、最速小惑星12。相対変位148/60+最大接触半径<18。
        }
        private static void Check(float value, float min, float max)
        {
            if (!float.IsFinite(value) || value < min || value > max) { throw new ArgumentException("設定は許容範囲内の有限値で指定してください。"); }
        }
    }
}
