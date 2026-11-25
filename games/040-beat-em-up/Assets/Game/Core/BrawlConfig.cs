using System;

namespace Game.Core
{
    /// <summary>座標の単位。1 px = 256。</summary>
    public static class Px
    {
        public const int One = 256;
    }

    /// <summary>面と規則の設定。公開フィールドは ScriptableObject から写す。</summary>
    [Serializable]
    public sealed class BrawlConfig
    {
        /// <summary>敵の置き場の数の上限（配列の容量）。</summary>
        public const int MaxCapacity = 32;

        public int TickRate = 60;

        /// <summary>画面の横幅（単位）。</summary>
        public int ViewWidth = 384 * Px.One;

        /// <summary>奥行きの帯。0 が手前。</summary>
        public int ZMin;

        public int ZMax = 96 * Px.One;

        /// <summary>画面の縦位置 = z × DepthNum / DepthDen + y。</summary>
        public int DepthNum = 1;

        public int DepthDen = 2;

        /// <summary>プレイヤーが画面の左右の端から離れる距離。</summary>
        public int EdgeMargin = 14 * Px.One;

        /// <summary>敵が画面の外に出られる距離（出現位置もここ）。</summary>
        public int SpawnMargin = 28 * Px.One;

        public int BufferTicks = 8;

        /// <summary>敵の置き場の数。画面に出ている敵の上限はこれ以下。</summary>
        public int Capacity = 8;

        public int MaxActiveEnemies = 4;

        /// <summary>同時に攻撃に入れる雑魚の数。ボスは別枠で 1。</summary>
        public int MaxAttackers = 2;

        /// <summary>攻撃を終えた敵が次に攻撃権を求めるまでの Tick。</summary>
        public int TokenCooldown = 45;

        /// <summary>攻撃権を持ったまま攻撃に入れない敵が権利を返すまでの Tick。</summary>
        public int TokenTimeout = 150;

        /// <summary>掴める奥行きの差。</summary>
        public int GrabDepth = 6 * Px.One;

        /// <summary>押し合いの接触から掴める横の余裕。</summary>
        public int GrabReach = 2 * Px.One;

        public int GrabTicks = 120;

        /// <summary>掴んだ相手を置く横の距離。</summary>
        public int GrabOffset = 18 * Px.One;

        public int MaxKnees = 2;

        /// <summary>掴みから離されたときの怯み。</summary>
        public int ReleaseStun = 14;

        public int Lives = 3;

        public int RespawnInvuln = 120;

        public int WakeupInvuln = 40;

        public int DeadTicks = 45;

        public int LandTicks = 4;

        /// <summary>空中で受けた打撃・体力 0 になった打撃の吹き飛び（ダウン技でないとき）。</summary>
        public int FallLaunchX = 384;

        public int FallLaunchY = 768;

        public int FallGravity = 96;

        /// <summary>区画を片付けた後に画面が 1 Tick に進む上限（単位）。プレイヤーの位置へ一気に飛ばない。</summary>
        public int ScrollSpeed = 3 * Px.One;

        /// <summary>回復アイテムで戻る体力。</summary>
        public int HealAmount = 200;

        /// <summary>回復アイテムの横幅と奥行きの半幅（プレイヤーの喰らい箱と重なれば拾う）。</summary>
        public int ItemWidth = 16 * Px.One;

        public int ItemDepth = 6 * Px.One;

        public BrawlConfig Validate()
        {
            if (TickRate <= 0) { throw new ArgumentOutOfRangeException(nameof(TickRate)); }
            if (ViewWidth < 64 * Px.One || ViewWidth > 4096 * Px.One) { throw new ArgumentOutOfRangeException(nameof(ViewWidth)); }
            if (ZMax - ZMin < 16 * Px.One || ZMin < 0 || ZMax > 1024 * Px.One) { throw new ArgumentOutOfRangeException(nameof(ZMax)); }
            if (DepthNum < 0 || DepthDen < 1 || DepthNum > DepthDen) { throw new ArgumentOutOfRangeException(nameof(DepthNum)); }
            if (EdgeMargin < 0 || EdgeMargin * 4 > ViewWidth) { throw new ArgumentOutOfRangeException(nameof(EdgeMargin)); }
            if (SpawnMargin < 1 || SpawnMargin > ViewWidth) { throw new ArgumentOutOfRangeException(nameof(SpawnMargin)); }
            if (BufferTicks < 1 || BufferTicks > 60) { throw new ArgumentOutOfRangeException(nameof(BufferTicks)); }
            if (Capacity < 1 || Capacity > MaxCapacity) { throw new ArgumentOutOfRangeException(nameof(Capacity)); }
            if (MaxActiveEnemies < 1 || MaxActiveEnemies > Capacity) { throw new ArgumentOutOfRangeException(nameof(MaxActiveEnemies)); }
            if (MaxAttackers < 1 || MaxAttackers > MaxActiveEnemies) { throw new ArgumentOutOfRangeException(nameof(MaxAttackers)); }
            if (TokenCooldown < 0 || TokenTimeout < 1) { throw new ArgumentOutOfRangeException(nameof(TokenTimeout)); }
            if (GrabDepth < 1 || GrabReach < 0 || GrabTicks < 1 || GrabOffset < 1 || MaxKnees < 0 || ReleaseStun < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(GrabTicks));
            }

            if (Lives < 1 || Lives > 9 || RespawnInvuln < 1 || WakeupInvuln < 0 || DeadTicks < 1 || LandTicks < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(Lives));
            }

            if (FallLaunchX < 0 || FallLaunchY < 1 || FallGravity < 1) { throw new ArgumentOutOfRangeException(nameof(FallLaunchY)); }
            if (ScrollSpeed < 1 || ScrollSpeed > ViewWidth) { throw new ArgumentOutOfRangeException(nameof(ScrollSpeed)); }
            if (HealAmount < 1 || ItemWidth < 1 || ItemDepth < 1 || ItemWidth > ViewWidth) { throw new ArgumentOutOfRangeException(nameof(HealAmount)); }
            return this;
        }

        public BrawlConfig Copy() => (BrawlConfig)MemberwiseClone();
    }

    /// <summary>
    /// プレイヤーの 1 Tick の入力。X は右が正、Z は奥が正。Attack・Jump・Special はその Tick の終わりに押しているか、
    /// *Pressed はその Tick の間に押下が起きたか（押して同じ Tick に離した、離して押し直した、を取りこぼさない）。
    /// 押下は「Pressed か、前の Tick に押していなくて今押している」で決める。
    /// </summary>
    public readonly struct Pad : IEquatable<Pad>
    {
        public Pad(int x, int z, bool attack, bool jump, bool special)
            : this(x, z, attack, jump, special, false, false, false)
        {
        }

        public Pad(int x, int z, bool attack, bool jump, bool special, bool attackPressed, bool jumpPressed, bool specialPressed)
        {
            if (x < -1 || x > 1 || z < -1 || z > 1) { throw new ArgumentOutOfRangeException(nameof(x), "方向は -1, 0, 1 です。"); }
            X = x;
            Z = z;
            Attack = attack;
            Jump = jump;
            Special = special;
            AttackPressed = attackPressed;
            JumpPressed = jumpPressed;
            SpecialPressed = specialPressed;
        }

        public static Pad Neutral => default;

        public int X { get; }

        public int Z { get; }

        public bool Attack { get; }

        public bool Jump { get; }

        public bool Special { get; }

        public bool AttackPressed { get; }

        public bool JumpPressed { get; }

        public bool SpecialPressed { get; }

        public bool Equals(Pad other) =>
            X == other.X && Z == other.Z && Attack == other.Attack && Jump == other.Jump && Special == other.Special
            && AttackPressed == other.AttackPressed && JumpPressed == other.JumpPressed && SpecialPressed == other.SpecialPressed;

        public override bool Equals(object? obj) => obj is Pad other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Z, Attack, Jump, Special, AttackPressed, JumpPressed, SpecialPressed);

        public static bool operator ==(Pad left, Pad right) => left.Equals(right);

        public static bool operator !=(Pad left, Pad right) => !left.Equals(right);
    }
}
