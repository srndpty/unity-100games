using System;

namespace Game.Core
{
    /// <summary>
    /// 試合の設定。速度は 1 Tick あたりの移動量、加速度は 1 Tick あたりの速度の変化で、
    /// どちらも 1 タイルを <see cref="Units.Tile"/> 等分した整数（浮動小数点を使わないので Mono と .NET で同じ結果になる）。
    /// </summary>
    [Serializable]
    public sealed class PlatformerConfig
    {
        /// <summary>1 Tick の移動量の上限。1 タイル未満に抑え、敵と自機の大きさより小さく保つ。</summary>
        public const int MaxSpeed = Units.Tile - 1;
        public const int MaxLives = 9;
        public const int MinTimeLimitSeconds = 10;
        public const int MaxTimeLimitSeconds = 999;
        public const int MaxActivationTiles = 1000;
        public const int MaxScorePerEvent = 100000;
        public const int MaxHopperInterval = 60 * Units.TickRate;

        /// <summary>歩きの最高速。</summary>
        public int WalkSpeed = 24;

        /// <summary>走りキーを押しているときの最高速。</summary>
        public int RunSpeed = 40;

        /// <summary>接地中の加速。</summary>
        public int GroundAccel = 2;

        /// <summary>空中の加速（反対向きの入力も同じ）。</summary>
        public int AirAccel = 1;

        /// <summary>接地中に入力がないとき、または最高速を超えているときの減速。</summary>
        public int Friction = 2;

        /// <summary>接地中に進行方向と反対へ入力したときの減速。</summary>
        public int SkidDecel = 4;

        /// <summary>跳んだ瞬間の上向きの速さ。</summary>
        public int JumpSpeed = 96;

        /// <summary>上昇中にジャンプキーを離したとき、上向きの速さをこの値まで切る（可変ジャンプ）。</summary>
        public int JumpCutSpeed = 32;

        public int Gravity = 4;

        public int MaxFallSpeed = 112;

        /// <summary>踏みつけた後の跳ね返り（ジャンプキーを押していれば <see cref="JumpSpeed"/>）。</summary>
        public int StompBounce = 56;

        public int WalkerSpeed = 8;

        public int ThornSpeed = 6;

        /// <summary>跳ね虫が着地してから次に跳ぶまでの Tick 数。</summary>
        public int HopperInterval = 80;

        public int HopperJumpSpeed = 72;

        /// <summary>自機から何タイル以内に入った敵を動かし始めるか（カメラには依存しない）。</summary>
        public int ActivationTiles = 18;

        public int Lives = 3;

        /// <summary>1 面の制限時間（秒）。</summary>
        public int TimeLimitSeconds = 150;

        public int StompScore = 100;

        /// <summary>面クリア時、残り時間 1 秒あたりの得点。</summary>
        public int TimeBonusPerSecond = 10;

        public int TimeLimitTicks => TimeLimitSeconds * Units.TickRate;

        public void Validate()
        {
            Require(WalkSpeed, 1, MaxSpeed, nameof(WalkSpeed));
            Require(RunSpeed, WalkSpeed, MaxSpeed, nameof(RunSpeed));
            Require(GroundAccel, 1, MaxSpeed, nameof(GroundAccel));
            Require(AirAccel, 1, MaxSpeed, nameof(AirAccel));
            Require(Friction, 1, MaxSpeed, nameof(Friction));
            Require(SkidDecel, 1, MaxSpeed, nameof(SkidDecel));
            Require(Gravity, 1, MaxSpeed, nameof(Gravity));
            Require(JumpSpeed, Gravity, MaxSpeed, nameof(JumpSpeed));
            Require(JumpCutSpeed, 0, JumpSpeed - 1, nameof(JumpCutSpeed));
            Require(MaxFallSpeed, Gravity, MaxSpeed, nameof(MaxFallSpeed));
            Require(StompBounce, 1, JumpSpeed, nameof(StompBounce));
            Require(WalkerSpeed, 1, MaxSpeed, nameof(WalkerSpeed));
            Require(ThornSpeed, 1, MaxSpeed, nameof(ThornSpeed));
            Require(HopperInterval, 1, MaxHopperInterval, nameof(HopperInterval));
            Require(HopperJumpSpeed, Gravity, MaxSpeed, nameof(HopperJumpSpeed));
            Require(ActivationTiles, 1, MaxActivationTiles, nameof(ActivationTiles));
            Require(Lives, 1, MaxLives, nameof(Lives));
            Require(TimeLimitSeconds, MinTimeLimitSeconds, MaxTimeLimitSeconds, nameof(TimeLimitSeconds));
            Require(StompScore, 0, MaxScorePerEvent, nameof(StompScore));
            Require(TimeBonusPerSecond, 0, MaxScorePerEvent, nameof(TimeBonusPerSecond));
        }

        /// <summary>進行中の試合が設定の書き換えの影響を受けないよう複製する。</summary>
        public PlatformerConfig Copy() => (PlatformerConfig)MemberwiseClone();

        private static void Require(int value, int min, int max, string name)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。");
            }
        }
    }
}
