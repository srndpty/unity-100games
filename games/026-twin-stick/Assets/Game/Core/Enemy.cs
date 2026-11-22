using System;
using TileMotion;

namespace Game.Core
{
    public enum EnemyPhase
    {
        /// <summary>出現の予告中。動かず、当たらない。</summary>
        Warning,

        Active,
    }

    /// <summary>Lancer の行動。歩く → 止まって狙う → 突進する、を繰り返す。</summary>
    public enum LancerMode
    {
        Rest,
        Aim,
        Dash,
    }

    /// <summary>敵 1 体。</summary>
    public sealed class Enemy
    {
        /// <summary>撃たれたときの点滅の Tick 数（表示用）。</summary>
        public const int FlashTicks = 6;

        internal Countdown WarnTimer;
        internal Countdown FlashTimer;

        internal Enemy(int id, EnemyKind kind, EnemyStats stats, Vec2i position, Vec2i velocity)
        {
            Id = id;
            Kind = kind;
            Radius = stats.Radius;
            Health = stats.Health;
            Position = position;
            Velocity = velocity;
        }

        /// <summary>試合の中で一意の番号（描画の対応づけ用）。</summary>
        public int Id { get; }

        public EnemyKind Kind { get; }

        public int Radius { get; }

        public EnemyPhase Phase { get; internal set; }

        public Vec2i Position { get; internal set; }

        /// <summary>直前の Tick の移動量（Bouncer は進む向き、Lancer は狙い・突進の向き）。</summary>
        public Vec2i Velocity { get; internal set; }

        public int Health { get; internal set; }

        public LancerMode Mode { get; internal set; }

        /// <summary>今の行動（<see cref="Mode"/>）に入ってから何 Tick 経ったか。</summary>
        public int ModeTicks { get; internal set; }

        /// <summary>画面外に続けて居る Tick 数。</summary>
        public int OffscreenTicks { get; internal set; }

        public bool IsActive => Phase == EnemyPhase.Active;

        /// <summary>予告の残り Tick（この Tick を含む。動いている敵は 0）。</summary>
        public int WarnRemaining => WarnTimer.Remaining;

        /// <summary>撃たれた直後の点滅中か（表示用）。</summary>
        public bool IsFlashing => FlashTimer.IsActive;

        internal Enemy Clone() => (Enemy)MemberwiseClone();

        /// <summary>1 Tick 動かす。自機の位置（この Tick の移動の後）へ向かう敵は、その位置を使う。</summary>
        internal void Move(SiegeConfig config, Vec2i ship)
        {
            if (!IsActive) { return; }
            var speed = config.StatsOf(Kind).Speed;
            switch (Kind)
            {
                case EnemyKind.Chaser:
                    Steer(ship, speed);
                    MoveClamped(config, false);
                    break;
                case EnemyKind.Bouncer:
                    MoveClamped(config, true);
                    break;
                case EnemyKind.Lancer:
                    MoveLancer(config, ship, speed);
                    break;
                default:
                    throw new InvalidOperationException($"未定義の敵です: {Kind}");
            }
        }

        internal void Mix(ref StateHash hash)
        {
            hash.Mix(Id);
            hash.Mix((int)Kind);
            hash.Mix((int)Phase);
            hash.Mix(Position.X);
            hash.Mix(Position.Y);
            hash.Mix(Velocity.X);
            hash.Mix(Velocity.Y);
            hash.Mix(Health);
            hash.Mix((int)Mode);
            hash.Mix(ModeTicks);
            hash.Mix(OffscreenTicks);
            hash.Mix(WarnTimer.Remaining);
            hash.Mix(FlashTimer.Remaining);
        }

        private void MoveLancer(SiegeConfig config, Vec2i ship, int speed)
        {
            // 各行動はちょうど設定の Tick 数だけ続く（その Tick 数を数え終えた次の Tick に切り替える）。
            if (Mode == LancerMode.Rest && ModeTicks >= config.LancerRestTicks)
            {
                Switch(LancerMode.Aim);
                var aim = IntMath.Scale(ship - Position, config.LancerDashSpeed);
                Velocity = aim.IsZero ? new Vec2i(config.LancerDashSpeed, 0) : aim;
            }
            else if (Mode == LancerMode.Aim && ModeTicks >= config.LancerAimTicks)
            {
                Switch(LancerMode.Dash);
            }
            else if (Mode == LancerMode.Dash && ModeTicks >= config.LancerDashTicks)
            {
                Switch(LancerMode.Rest);
            }

            switch (Mode)
            {
                case LancerMode.Rest:
                    Steer(ship, speed);
                    MoveClamped(config, false);
                    break;
                case LancerMode.Aim:
                    break;
                case LancerMode.Dash:
                    // 外周に着いたら突進をやめる。
                    if (MoveClamped(config, false)) { Switch(LancerMode.Rest); }
                    break;
            }

            ModeTicks++;
        }

        private void Switch(LancerMode mode)
        {
            Mode = mode;
            ModeTicks = 0;
        }

        /// <summary>自機へ向けて速度を決める（自機に重なっていれば直前の向き）。</summary>
        private void Steer(Vec2i ship, int speed)
        {
            var toward = IntMath.Scale(ship - Position, speed);
            if (!toward.IsZero) { Velocity = toward; }
        }

        /// <summary>
        /// 速度だけ進め、外周で止める（端に着いた時点で止まったとする。軌跡は始めから止まった位置への直線のまま）。
        /// <paramref name="reflect"/> なら外周に着いた軸の速度を反転する。外周に着いたら true。
        /// </summary>
        private bool MoveClamped(SiegeConfig config, bool reflect)
        {
            var next = Position + Velocity;
            var x = IntMath.Clamp(next.X, Radius, config.ArenaWidth - Radius);
            var y = IntMath.Clamp(next.Y, Radius, config.ArenaHeight - Radius);
            var hitX = x != next.X;
            var hitY = y != next.Y;
            Position = new Vec2i(x, y);
            if (reflect && (hitX || hitY))
            {
                Velocity = new Vec2i(hitX ? -Velocity.X : Velocity.X, hitY ? -Velocity.Y : Velocity.Y);
            }

            return hitX || hitY;
        }
    }
}
