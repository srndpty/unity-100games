using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 試合の設定。速さは 1 Tick あたりの移動量で、1 タイルを <see cref="Units.Tile"/> 等分した整数。時間は Tick 数で持つ
    /// （浮動小数点を使わないので Mono と .NET で同じ結果になり、秒から Tick への丸めの境界も生まれない）。
    /// </summary>
    [Serializable]
    public sealed class VaultConfig
    {
        /// <summary>1 Tick の移動量の上限。1 タイル未満に抑える（壁を飛び越えない）。</summary>
        public const int MaxSpeed = Units.Tile - 1;

        public const int MaxStatusTicks = 600;
        public const int MaxHealthLimit = 20;
        public const int MaxEnemyHealth = 99;

        /// <summary>剣の届く距離・幅の上限（単位）。</summary>
        public const int MaxSwordSize = 3 * Units.Tile;

        /// <summary>自機の歩く速さ。斜めは各軸 181/256 倍。</summary>
        public int HeroSpeed = 22;

        /// <summary>剣を振っている間の歩く速さ。</summary>
        public int SwingMoveSpeed = 6;

        public int MaxHealth = 6;

        /// <summary>1 回の振りの長さ（Tick）。振った Tick が 1 Tick 目。</summary>
        public int SwingTicks = 16;

        /// <summary>剣が当たる最初の Tick（振りの何 Tick 目か）。</summary>
        public int SwingActiveFrom = 3;

        /// <summary>剣が当たる最後の Tick（振りの何 Tick 目か）。</summary>
        public int SwingActiveTo = 10;

        /// <summary>剣の届く距離（体の前の端から）。</summary>
        public int SwordReach = 14 * Units.Pixel;

        /// <summary>剣の当たりの幅（体の中央にそろえる）。</summary>
        public int SwordWidth = 20 * Units.Pixel;

        /// <summary>回避ロールの長さ（Tick）。始めた Tick が 1 Tick 目。</summary>
        public int RollTicks = 18;

        public int RollSpeed = 40;

        /// <summary>ロールの始めから何 Tick が無敵か。</summary>
        public int RollInvulnTicks = 12;

        /// <summary>ロールが終わった Tick を含めて何 Tick は次のロールができないか。</summary>
        public int RollCooldownTicks = 20;

        /// <summary>被弾の次の Tick から何 Tick が無敵か。</summary>
        public int InvulnTicks = 60;

        /// <summary>被弾で押し戻される Tick 数（その間は操作が効かない）。</summary>
        public int KnockbackTicks = 8;

        public int KnockbackSpeed = 40;

        /// <summary>敵が剣を受けた次の Tick から何 Tick が無敵か。</summary>
        public int EnemyInvulnTicks = 12;

        public int EnemyKnockbackTicks = 6;

        public int EnemyKnockbackSpeed = 40;

        /// <summary>敵・ボスに触れたときのダメージ。</summary>
        public int ContactDamage = 1;

        /// <summary>弾に当たったときのダメージ。</summary>
        public int ShotDamage = 1;

        /// <summary>剣 1 回のダメージ。</summary>
        public int SwordDamage = 1;

        public int SlimeHealth = 2;

        public int SlimeSpeed = 8;

        /// <summary>スライムが同じ向きに歩く（止まる）Tick 数の最小と最大。乱数でこの間から選ぶ。</summary>
        public int SlimeMinTicks = 20;

        public int SlimeMaxTicks = 60;

        public int SpitterHealth = 2;

        /// <summary>射手が撃ってから次を撃てるまでの Tick 数。</summary>
        public int SpitterFireTicks = 90;

        public int SpitterShotSpeed = 36;

        public int ChargerHealth = 3;

        /// <summary>突進兵が並んでから走り出すまでの構えの Tick 数。</summary>
        public int ChargerWindupTicks = 20;

        public int ChargerSpeed = 56;

        /// <summary>突進兵が壁に当たってから動けない Tick 数。</summary>
        public int ChargerStunTicks = 50;

        public int BossHealth = 12;

        /// <summary>ボスが自機のほうへ歩く Tick 数。</summary>
        public int BossWalkTicks = 120;

        public int BossWalkSpeed = 10;

        /// <summary>ボスが 8 方向の弾を撃った後に止まっている Tick 数。</summary>
        public int BossVolleyTicks = 50;

        public int BossShotSpeed = 32;

        public int BossWindupTicks = 30;

        public int BossChargeSpeed = 64;

        public int BossStunTicks = 70;

        /// <summary>部屋の切り替え（スクロール）の Tick 数。その間は入力・被弾・敵の動きがない。</summary>
        public int TransitionTicks = 30;

        /// <summary>斜めの移動量。各軸を 1/√2 倍（181/256）にした整数。正の速さは最低 1 にする（切り捨てで 0 にしない）。</summary>
        public static int DiagonalOf(int speed) => speed <= 0 ? 0 : Math.Max(1, speed * 181 / 256);

        /// <summary>範囲を確かめる。範囲外なら例外（fail-closed）。</summary>
        public void Validate()
        {
            Require(HeroSpeed, 1, MaxSpeed, nameof(HeroSpeed));
            Require(SwingMoveSpeed, 0, HeroSpeed, nameof(SwingMoveSpeed));
            Require(MaxHealth, 1, MaxHealthLimit, nameof(MaxHealth));
            Require(SwingTicks, 1, MaxStatusTicks, nameof(SwingTicks));
            Require(SwingActiveFrom, 1, SwingTicks, nameof(SwingActiveFrom));
            Require(SwingActiveTo, SwingActiveFrom, SwingTicks, nameof(SwingActiveTo));
            Require(SwordReach, 1, MaxSwordSize, nameof(SwordReach));
            Require(SwordWidth, 1, MaxSwordSize, nameof(SwordWidth));
            Require(RollTicks, 1, MaxStatusTicks, nameof(RollTicks));
            Require(RollSpeed, 1, MaxSpeed, nameof(RollSpeed));
            Require(RollInvulnTicks, 0, RollTicks, nameof(RollInvulnTicks));
            Require(RollCooldownTicks, 1, MaxStatusTicks, nameof(RollCooldownTicks));
            Require(InvulnTicks, 1, MaxStatusTicks, nameof(InvulnTicks));
            Require(KnockbackTicks, 0, InvulnTicks, nameof(KnockbackTicks));
            Require(KnockbackSpeed, 0, MaxSpeed, nameof(KnockbackSpeed));
            Require(EnemyInvulnTicks, 1, MaxStatusTicks, nameof(EnemyInvulnTicks));
            Require(EnemyKnockbackTicks, 0, EnemyInvulnTicks, nameof(EnemyKnockbackTicks));
            Require(EnemyKnockbackSpeed, 0, MaxSpeed, nameof(EnemyKnockbackSpeed));
            Require(ContactDamage, 1, MaxHealthLimit, nameof(ContactDamage));
            Require(ShotDamage, 1, MaxHealthLimit, nameof(ShotDamage));
            Require(SwordDamage, 1, MaxEnemyHealth, nameof(SwordDamage));
            Require(SlimeHealth, 1, MaxEnemyHealth, nameof(SlimeHealth));
            Require(SlimeSpeed, 1, MaxSpeed, nameof(SlimeSpeed));
            Require(SlimeMinTicks, 1, MaxStatusTicks, nameof(SlimeMinTicks));
            Require(SlimeMaxTicks, SlimeMinTicks, MaxStatusTicks, nameof(SlimeMaxTicks));
            Require(SpitterHealth, 1, MaxEnemyHealth, nameof(SpitterHealth));
            Require(SpitterFireTicks, 1, MaxStatusTicks, nameof(SpitterFireTicks));
            Require(SpitterShotSpeed, 1, MaxSpeed, nameof(SpitterShotSpeed));
            Require(ChargerHealth, 1, MaxEnemyHealth, nameof(ChargerHealth));
            Require(ChargerWindupTicks, 1, MaxStatusTicks, nameof(ChargerWindupTicks));
            Require(ChargerSpeed, 1, MaxSpeed, nameof(ChargerSpeed));
            Require(ChargerStunTicks, 1, MaxStatusTicks, nameof(ChargerStunTicks));
            Require(BossHealth, 1, MaxEnemyHealth, nameof(BossHealth));
            Require(BossWalkTicks, 1, MaxStatusTicks, nameof(BossWalkTicks));
            Require(BossWalkSpeed, 1, MaxSpeed, nameof(BossWalkSpeed));
            Require(BossVolleyTicks, 1, MaxStatusTicks, nameof(BossVolleyTicks));
            Require(BossShotSpeed, 1, MaxSpeed, nameof(BossShotSpeed));
            Require(BossWindupTicks, 1, MaxStatusTicks, nameof(BossWindupTicks));
            Require(BossChargeSpeed, 1, MaxSpeed, nameof(BossChargeSpeed));
            Require(BossStunTicks, 1, MaxStatusTicks, nameof(BossStunTicks));
            Require(TransitionTicks, 1, MaxStatusTicks, nameof(TransitionTicks));
        }

        /// <summary>進行中の試合が設定の書き換えの影響を受けないよう複製する。</summary>
        public VaultConfig Copy() => (VaultConfig)MemberwiseClone();

        private static void Require(int value, int min, int max, string name)
        {
            if (value < min || value > max) { throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。"); }
        }
    }
}
