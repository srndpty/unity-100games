using System;

namespace Game.Core
{
    [Serializable]
    public sealed class InvadersConfig
    {
        public const int TickRate = 60;
        public const float FieldHalfWidth = 10f;
        public const float PlayerY = -6f;
        public const float InvasionY = -5f;
        public const float EnemyWidth = 1f;
        public const float EnemyHeight = .7f;
        public const float PlayerWidth = 1f;
        public const float PlayerHeight = .6f;
        public const float ShieldCellSize = .25f;
        public int Columns = 8;
        public int Rows = 4;
        public int Lives = 3;
        public int PlayerBulletLimit = 3;
        public int EnemyBulletLimit = 12;
        public float PlayerSpeed = 8f;
        public float PlayerBulletSpeed = 20f;
        public float EnemyBulletSpeed = 6f;
        public float FormationSpeed = 1f;
        public float WaveSpeedIncrease = .35f;
        public float Descent = .5f;
        public float PlayerFireSeconds = .25f;
        public float EnemyFireSeconds = .9f;
        public float WaveFireMultiplier = .8f;
        public float TransitionSeconds = 1f;
        public float RespawnSeconds = 1f;
        public float InvincibleSeconds = 1f;

        public InvadersConfig Copy() => (InvadersConfig)MemberwiseClone();

        public void Validate()
        {
            if (Columns < 1 || Columns > 10 || Rows < 1 || Rows > 5 || Lives < 1 || Lives > 9
                || PlayerBulletLimit < 1 || PlayerBulletLimit > 8 || EnemyBulletLimit < 1 || EnemyBulletLimit > 24)
            {
                throw new ArgumentException("編隊・残機・弾数が許容範囲外です。");
            }
            Check(PlayerSpeed, .1f, 30f); Check(PlayerBulletSpeed, 1f, 120f); Check(EnemyBulletSpeed, 1f, 60f);
            Check(FormationSpeed, .1f, 5f); Check(WaveSpeedIncrease, 0f, 2f); Check(Descent, .1f, 1f);
            Check(PlayerFireSeconds, 1f / TickRate, 2f); Check(EnemyFireSeconds, .1f, 5f);
            Check(WaveFireMultiplier, .5f, 1f); Check(TransitionSeconds, .1f, 3f);
            Check(RespawnSeconds, .1f, 3f); Check(InvincibleSeconds, .1f, 3f);
        }

        private static void Check(float value, float min, float max)
        {
            if (!float.IsFinite(value) || value < min || value > max)
            {
                throw new ArgumentException("設定値は許容範囲内の有限値で指定してください。");
            }
        }

        public static int Ticks(float seconds)
        {
            var ticks = (double)seconds * TickRate;
            var nearest = Math.Round(ticks);
            // 整数Tickに対応するfloat表現との一致だけを認め、直上のfloatは切り上げる。
            // doubleへ戻して比較し、Monoの中間精度に残った値もfloat精度へ確定させる。
            var boundarySeconds = (double)(float)(nearest / TickRate);
            return (int)((double)seconds == boundarySeconds ? nearest : Math.Ceiling(ticks));
        }
    }
}
