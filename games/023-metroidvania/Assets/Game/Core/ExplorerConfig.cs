using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 試合の設定。速度は 1 Tick あたりの移動量、加速度は 1 Tick あたりの速度の変化で、
    /// どちらも 1 タイルを <see cref="Units.Tile"/> 等分した整数。時間は秒ではなく Tick 数で持つ
    /// （浮動小数点を使わないので Mono と .NET で同じ結果になり、秒から Tick への丸めの境界も生まれない）。
    /// </summary>
    [Serializable]
    public sealed class ExplorerConfig
    {
        /// <summary>1 Tick の移動量の上限。1 タイル未満に抑える。</summary>
        public const int MaxSpeed = Units.Tile - 1;

        public const int MaxGraceTicks = 30;
        public const int MaxStatusTicks = 240;
        public const int MaxHealthLimit = 20;
        public const int MaxShotsLimit = 4;
        public const int MaxBossHealth = 99;

        /// <summary>横の最高速。</summary>
        public int MoveSpeed = 36;

        public int GroundAccel = 6;

        public int AirAccel = 4;

        public int GroundDecel = 6;

        public int AirDecel = 2;

        public int Gravity = 4;

        public int MaxFallSpeed = 96;

        /// <summary>地上（と接地猶予中）のジャンプの上向きの速さ。</summary>
        public int JumpSpeed = 84;

        /// <summary>二段ジャンプの上向きの速さ。</summary>
        public int DoubleJumpSpeed = 72;

        /// <summary>上昇中にジャンプキーを離したとき、上向きの速さをこの値まで切る（可変ジャンプ）。</summary>
        public int JumpCutSpeed = 24;

        /// <summary>接地猶予：足場を離れてから何 Tick の間、地上のジャンプができるか。</summary>
        public int CoyoteTicks = 6;

        /// <summary>入力バッファ：ジャンプキーを押した Tick の後、何 Tick の間ジャンプの発火を待つか。</summary>
        public int JumpBufferTicks = 6;

        /// <summary>体力の最大値。開始・セーブ像・再開で最大まで戻る。</summary>
        public int MaxHealth = 5;

        /// <summary>被弾してから次に被弾するまでの無敵の Tick 数。</summary>
        public int InvincibleTicks = 60;

        /// <summary>被弾で押し戻される横の速さ（敵と反対向き）。</summary>
        public int KnockbackSpeedX = 32;

        /// <summary>被弾で跳ね上がる上向きの速さ。</summary>
        public int KnockbackSpeedY = 40;

        /// <summary>被弾の後、横入力を受け付けない Tick 数。</summary>
        public int KnockbackTicks = 12;

        public int ShotSpeed = 96;

        /// <summary>弾が消えるまでの Tick 数（射程）。</summary>
        public int ShotRangeTicks = 40;

        /// <summary>撃ってから次を撃てるまでの Tick 数。</summary>
        public int ShotCooldownTicks = 10;

        /// <summary>同時に画面にある自機の弾の上限。</summary>
        public int MaxShots = 2;

        /// <summary>回復の実で戻る体力。</summary>
        public int FruitHeal = 2;

        public int WalkerSpeed = 8;

        public int WalkerHealth = 2;

        /// <summary>敵・ボスに触れたときのダメージ。</summary>
        public int ContactDamage = 1;

        public int BossHealth = 20;

        /// <summary>ボスが向きを決めて構える Tick 数。</summary>
        public int BossIdleTicks = 60;

        public int BossChargeSpeed = 48;

        /// <summary>突進で壁に当たってから動けない Tick 数。</summary>
        public int BossStunTicks = 40;

        /// <summary>ボスの弾の速さ（真横の成分）。</summary>
        public int BossBulletSpeed = 56;

        public int BulletDamage = 1;

        /// <summary>
        /// 斜めの移動量。各軸を 1/√2 倍（181/256）にした整数。正の速さは最低 1 にする（切り捨てで 0 にしない）。
        /// </summary>
        public static int DiagonalOf(int speed) => speed <= 0 ? 0 : Math.Max(1, speed * 181 / 256);

        /// <summary>
        /// 範囲を確かめ、さらに能力のゲートが成り立つこと（二段ジャンプなしでは高い段に届かず、ありなら届く。
        /// 段差の小さい足場は 1 回のジャンプで届く）と、地図の段差と隙間を横の距離まで含めて越えられること（<see cref="Rooms.Crossings"/>）を、
        /// 自機を実際に動かして確かめる（<see cref="JumpReach"/>・<see cref="Traversal"/>）。
        /// </summary>
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
            Require(DoubleJumpSpeed, Gravity, MaxSpeed, nameof(DoubleJumpSpeed));
            Require(JumpCutSpeed, 0, Math.Min(JumpSpeed, DoubleJumpSpeed) - 1, nameof(JumpCutSpeed));
            Require(CoyoteTicks, 0, MaxGraceTicks, nameof(CoyoteTicks));
            Require(JumpBufferTicks, 0, MaxGraceTicks, nameof(JumpBufferTicks));
            Require(MaxHealth, 1, MaxHealthLimit, nameof(MaxHealth));
            Require(InvincibleTicks, 1, MaxStatusTicks, nameof(InvincibleTicks));
            Require(KnockbackSpeedX, 0, MaxSpeed, nameof(KnockbackSpeedX));
            Require(KnockbackSpeedY, 0, MaxSpeed, nameof(KnockbackSpeedY));
            Require(KnockbackTicks, 0, InvincibleTicks, nameof(KnockbackTicks));
            Require(ShotSpeed, 1, MaxSpeed, nameof(ShotSpeed));
            Require(ShotRangeTicks, 1, MaxStatusTicks, nameof(ShotRangeTicks));
            Require(ShotCooldownTicks, 1, MaxStatusTicks, nameof(ShotCooldownTicks));
            Require(MaxShots, 1, MaxShotsLimit, nameof(MaxShots));
            Require(FruitHeal, 1, MaxHealthLimit, nameof(FruitHeal));
            Require(WalkerSpeed, 1, MaxSpeed, nameof(WalkerSpeed));
            Require(WalkerHealth, 1, MaxBossHealth, nameof(WalkerHealth));
            Require(ContactDamage, 1, MaxHealthLimit, nameof(ContactDamage));
            Require(BossHealth, 1, MaxBossHealth, nameof(BossHealth));
            Require(BossIdleTicks, 1, MaxStatusTicks, nameof(BossIdleTicks));
            Require(BossChargeSpeed, 1, MaxSpeed, nameof(BossChargeSpeed));
            Require(BossStunTicks, 1, MaxStatusTicks, nameof(BossStunTicks));
            Require(BossBulletSpeed, 1, MaxSpeed, nameof(BossBulletSpeed));
            Require(BulletDamage, 1, MaxHealthLimit, nameof(BulletDamage));

            var single = JumpReach.Measure(this, doubleJump: false);
            var both = JumpReach.Measure(this, doubleJump: true);
            if (single < Rooms.StepRise * Units.Tile)
            {
                throw new ArgumentException($"1 回のジャンプで {Rooms.StepRise} タイルの足場に届きません（{single} 単位）。ジャンプの速さか重力を見直してください。");
            }

            if (single >= Rooms.GateRise * Units.Tile)
            {
                throw new ArgumentException($"二段ジャンプなしで {Rooms.GateRise} タイルの高い段に届いてしまいます（{single} 単位）。ゲートが成り立ちません。");
            }

            if (both < Rooms.GateRise * Units.Tile)
            {
                throw new ArgumentException($"二段ジャンプでも {Rooms.GateRise} タイルの高い段に届きません（{both} 単位）。");
            }

            // 高さが足りても、横の速さが小さいと足場の間を越えられない。地図で越える必要がある段差と隙間を、自機を動かして確かめる。
            foreach (var crossing in Rooms.Crossings)
            {
                if (!Traversal.CanCross(this, crossing))
                {
                    throw new ArgumentException($"{crossing} を設定の自機で越えられません。横の速さ・加速かジャンプを見直してください。");
                }
            }
        }

        /// <summary>進行中の試合が設定の書き換えの影響を受けないよう複製する。</summary>
        public ExplorerConfig Copy() => (ExplorerConfig)MemberwiseClone();

        private static void Require(int value, int min, int max, string name)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。");
            }
        }
    }
}
