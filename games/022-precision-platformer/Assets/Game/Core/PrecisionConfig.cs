using System;

namespace Game.Core
{
    /// <summary>
    /// 試合の設定。速度は 1 Tick あたりの移動量、加速度は 1 Tick あたりの速度の変化で、
    /// どちらも 1 タイルを <see cref="Units.Tile"/> 等分した整数。時間は秒ではなく Tick 数で持つ
    /// （浮動小数点を使わないので Mono と .NET で同じ結果になり、秒から Tick への丸めの境界も生まれない）。
    /// </summary>
    [Serializable]
    public sealed class PrecisionConfig
    {
        /// <summary>1 Tick の移動量の上限。1 タイル未満に抑える。</summary>
        public const int MaxSpeed = Units.Tile - 1;

        /// <summary>接地猶予・入力バッファの上限（0.5 秒）。</summary>
        public const int MaxGraceTicks = 30;

        public const int MaxDashTicks = 60;
        public const int MaxLockTicks = 60;
        public const int MaxDeathTicks = 120;
        public const int MaxDashCount = 3;

        /// <summary>横の最高速。</summary>
        public int MoveSpeed = 36;

        /// <summary>接地中の加速。</summary>
        public int GroundAccel = 6;

        /// <summary>空中の加速。</summary>
        public int AirAccel = 4;

        /// <summary>接地中に入力がないとき、または最高速を超えているときの減速。</summary>
        public int GroundDecel = 6;

        /// <summary>空中で入力がないとき、または最高速を超えているときの減速。</summary>
        public int AirDecel = 2;

        public int Gravity = 4;

        public int MaxFallSpeed = 96;

        /// <summary>跳んだ瞬間の上向きの速さ。</summary>
        public int JumpSpeed = 84;

        /// <summary>上昇中にジャンプキーを離したとき、上向きの速さをこの値まで切る（可変ジャンプ）。</summary>
        public int JumpCutSpeed = 24;

        /// <summary>接地猶予：足場を離れてから何 Tick の間、地上のジャンプができるか。0 なら猶予なし。</summary>
        public int CoyoteTicks = 6;

        /// <summary>入力バッファ：ジャンプキーを押した Tick の後、何 Tick の間ジャンプの発火を待つか。0 なら押した Tick だけ。</summary>
        public int JumpBufferTicks = 6;

        /// <summary>壁蹴りできる壁までの隙間の上限。隙間がこれ未満なら蹴れる（0 は壁に接している）。</summary>
        public int WallReach = 3 * Units.Pixel;

        /// <summary>壁蹴りの横の速さ（壁と反対向き）。</summary>
        public int WallJumpSpeedX = 48;

        /// <summary>壁蹴りの上向きの速さ。</summary>
        public int WallJumpSpeedY = 80;

        /// <summary>壁蹴りの後、横入力を受け付けない Tick 数（壁へすぐ戻れないようにする）。</summary>
        public int WallJumpLockTicks = 10;

        /// <summary>壁へ向けて押して落ちているときの落下の最高速。</summary>
        public int WallSlideMaxFall = 32;

        /// <summary>ダッシュの速さ（縦横）。斜めは各軸 <see cref="DiagonalOf"/>。</summary>
        public int DashSpeed = 72;

        public int DashTicks = 10;

        /// <summary>ダッシュの終わりに残す速さ（各軸の上限）。</summary>
        public int DashEndSpeed = 36;

        /// <summary>空中で使えるダッシュの回数。接地で回復する。</summary>
        public int DashCount = 1;

        /// <summary>死亡してから部屋の開始位置に戻るまでの Tick 数。</summary>
        public int DeathTicks = 18;

        /// <summary>
        /// 斜めの移動量。各軸を 1/√2 倍（181/256）にした整数。正の速さは最低 1 にする
        /// （切り捨てで 0 になると、斜めのダッシュが回数だけ使って動かなくなる）。
        /// </summary>
        public static int DiagonalOf(int speed) => speed <= 0 ? 0 : Math.Max(1, speed * 181 / 256);

        public void Validate()
        {
            Require(MoveSpeed, 1, MaxSpeed, nameof(MoveSpeed));
            Require(GroundAccel, 1, MaxSpeed, nameof(GroundAccel));
            Require(AirAccel, 1, MaxSpeed, nameof(AirAccel));
            Require(GroundDecel, 1, MaxSpeed, nameof(GroundDecel));
            Require(AirDecel, 1, MaxSpeed, nameof(AirDecel));
            Require(Gravity, 1, MaxSpeed, nameof(Gravity));
            Require(MaxFallSpeed, Gravity, MaxSpeed, nameof(MaxFallSpeed));
            Require(JumpSpeed, Gravity, MaxSpeed, nameof(JumpSpeed));
            Require(JumpCutSpeed, 0, JumpSpeed - 1, nameof(JumpCutSpeed));
            Require(CoyoteTicks, 0, MaxGraceTicks, nameof(CoyoteTicks));
            Require(JumpBufferTicks, 0, MaxGraceTicks, nameof(JumpBufferTicks));
            Require(WallReach, 1, Units.Tile - 1, nameof(WallReach));
            Require(WallJumpSpeedX, 1, MaxSpeed, nameof(WallJumpSpeedX));
            Require(WallJumpSpeedY, Gravity, MaxSpeed, nameof(WallJumpSpeedY));
            Require(WallJumpLockTicks, 0, MaxLockTicks, nameof(WallJumpLockTicks));
            Require(WallSlideMaxFall, 1, MaxFallSpeed, nameof(WallSlideMaxFall));
            Require(DashSpeed, 1, MaxSpeed, nameof(DashSpeed));
            Require(DashTicks, 1, MaxDashTicks, nameof(DashTicks));
            Require(DashEndSpeed, 0, DashSpeed, nameof(DashEndSpeed));
            Require(DashCount, 1, MaxDashCount, nameof(DashCount));
            Require(DeathTicks, 1, MaxDeathTicks, nameof(DeathTicks));
        }

        /// <summary>進行中の試合が設定の書き換えの影響を受けないよう複製する。</summary>
        public PrecisionConfig Copy() => (PrecisionConfig)MemberwiseClone();

        private static void Require(int value, int min, int max, string name)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。");
            }
        }
    }
}
