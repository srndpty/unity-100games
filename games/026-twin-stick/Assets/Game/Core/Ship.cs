using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 1 Tick の自機の操作。移動（<see cref="MoveX"/>・<see cref="MoveY"/>）と照準（<see cref="Aim"/>）は独立していて、
    /// 互いに影響しない。照準はアリーナ座標の点（マウスの位置）で受け取り、自機からその点への向きに撃つ。
    /// </summary>
    public readonly struct ShipCommand : IEquatable<ShipCommand>
    {
        public ShipCommand(int moveX, int moveY, bool fire, Vec2i aim)
        {
            if (moveX < -1 || moveX > 1) { throw new ArgumentOutOfRangeException(nameof(moveX), moveX, "-1〜1 で指定してください。"); }
            if (moveY < -1 || moveY > 1) { throw new ArgumentOutOfRangeException(nameof(moveY), moveY, "-1〜1 で指定してください。"); }
            MoveX = moveX;
            MoveY = moveY;
            Fire = fire;
            Aim = aim;
        }

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int MoveX { get; }

        /// <summary>下 -1・なし 0・上 1。</summary>
        public int MoveY { get; }

        /// <summary>撃つ（押している）。</summary>
        public bool Fire { get; }

        /// <summary>狙う点（アリーナ座標）。</summary>
        public Vec2i Aim { get; }

        public static bool operator ==(ShipCommand left, ShipCommand right) => left.Equals(right);

        public static bool operator !=(ShipCommand left, ShipCommand right) => !left.Equals(right);

        public bool Equals(ShipCommand other) => MoveX == other.MoveX && MoveY == other.MoveY && Fire == other.Fire && Aim == other.Aim;

        public override bool Equals(object? obj) => obj is ShipCommand other && Equals(other);

        public override int GetHashCode() => unchecked((((((MoveX * 3) + MoveY) * 397) ^ (Fire ? 1 : 0)) * 397) ^ Aim.GetHashCode());

        public override string ToString() => $"Move({MoveX}, {MoveY}) Fire={Fire} Aim={Aim}";
    }

    /// <summary>自機。</summary>
    public sealed class Ship
    {
        internal Countdown InvincibleTimer;
        internal Countdown FireTimer;

        internal Ship(Vec2i position, int lives, int bulletSpeed)
        {
            Position = position;
            Lives = lives;
            ShotVelocity = new Vec2i(bulletSpeed, 0);
        }

        public Vec2i Position { get; internal set; }

        /// <summary>次に撃つ弾の速度（照準の向き）。照準点が自機に重なる Tick は直前の向きを保つ。</summary>
        public Vec2i ShotVelocity { get; internal set; }

        public int Lives { get; internal set; }

        /// <summary>無敵か（被弾した Tick を含めて <see cref="SiegeConfig.InvincibleTicks"/> Tick）。</summary>
        public bool IsInvincible => InvincibleTimer.IsActive;

        /// <summary>無敵の残り Tick（この Tick を含む）。</summary>
        public int InvincibleRemaining => InvincibleTimer.Remaining;

        internal Ship Clone() => (Ship)MemberwiseClone();

        internal void Mix(ref StateHash hash)
        {
            hash.Mix(Position.X);
            hash.Mix(Position.Y);
            hash.Mix(ShotVelocity.X);
            hash.Mix(ShotVelocity.Y);
            hash.Mix(Lives);
            hash.Mix(InvincibleTimer.Remaining);
            hash.Mix(FireTimer.Remaining);
        }
    }

    /// <summary>自機の弾。直進し、寿命かアリーナの外で消える。</summary>
    public sealed class Bullet
    {
        internal Countdown Life;

        internal Bullet(int id, Vec2i position, Vec2i velocity, int lifeTicks)
        {
            Id = id;
            Position = position;
            Velocity = velocity;
            Life = new Countdown(lifeTicks);
        }

        /// <summary>試合の中で一意の番号（描画の対応づけ用）。</summary>
        public int Id { get; }

        public Vec2i Position { get; internal set; }

        public Vec2i Velocity { get; }

        internal Bullet Clone() => (Bullet)MemberwiseClone();

        internal void Mix(ref StateHash hash)
        {
            hash.Mix(Id);
            hash.Mix(Position.X);
            hash.Mix(Position.Y);
            hash.Mix(Velocity.X);
            hash.Mix(Velocity.Y);
            hash.Mix(Life.Remaining);
        }
    }
}
