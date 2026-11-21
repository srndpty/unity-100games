using System;

namespace Game.Core
{
    public enum EnemyState
    {
        /// <summary>自機が近づくまで止まっている。</summary>
        Waiting,
        Active,

        /// <summary>踏まれて倒れた。</summary>
        Stomped,

        /// <summary>穴に落ちて消えた。</summary>
        Fell,
    }

    /// <summary>
    /// 敵。地形との衝突は自機と同じ <see cref="BodyMotion"/> で解く。敵どうしは触れ合わない。
    /// 自機との接触は <see cref="EnemyContact"/> が別に判定する。
    /// </summary>
    public sealed class Enemy
    {
        private readonly PlatformerConfig _config;
        private int _hopTimer;

        public Enemy(PlatformerConfig config, EnemyKind kind, Box body)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (!Enum.IsDefined(typeof(EnemyKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の敵です。");
            }

            Kind = kind;
            Body = body;
            Facing = -1;
        }

        public EnemyKind Kind { get; }

        public Box Body { get; private set; }

        public int VelocityY { get; private set; }

        /// <summary>向き（右 1・左 -1）。最初は左（自機のほう）を向く。</summary>
        public int Facing { get; private set; }

        public EnemyState State { get; private set; }

        public bool IsAlive => State == EnemyState.Waiting || State == EnemyState.Active;

        /// <summary>踏めるか。茨玉は踏めない。</summary>
        public bool Stompable => Kind != EnemyKind.Thorn;

        /// <summary>倒れた（踏まれた・落ちた）面の Tick。描画の演出用。</summary>
        public long EndedTick { get; private set; }

        public static Size SizeOf(EnemyKind kind)
        {
            return kind switch
            {
                EnemyKind.Walker => new Size(14 * Units.Pixel, 12 * Units.Pixel),
                EnemyKind.Hopper => new Size(12 * Units.Pixel, 12 * Units.Pixel),
                EnemyKind.Thorn => new Size(14 * Units.Pixel, 14 * Units.Pixel),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の敵です。"),
            };
        }

        /// <summary>同じ状態の複製（先読み用）。</summary>
        public Enemy Clone() => (Enemy)MemberwiseClone();

        /// <summary>1 Tick 進める。自機から起動距離内に入るまでは止まっている。</summary>
        public void Step(TileMap map, Box player, long tick)
        {
            if (State == EnemyState.Waiting)
            {
                if (Math.Abs((long)Body.CenterX - player.CenterX) > (long)_config.ActivationTiles * Units.Tile)
                {
                    return;
                }

                State = EnemyState.Active;
            }

            if (State != EnemyState.Active)
            {
                return;
            }

            var body = Body;
            var grounded = VelocityY <= 0 && GroundProbe.IsGrounded(map, body);
            switch (Kind)
            {
                case EnemyKind.Walker:
                    Walk(map, ref body, _config.WalkerSpeed, turnAtLedge: false, grounded);
                    break;
                case EnemyKind.Thorn:
                    Walk(map, ref body, _config.ThornSpeed, turnAtLedge: true, grounded);
                    break;
                case EnemyKind.Hopper:
                    if (grounded && ++_hopTimer >= _config.HopperInterval)
                    {
                        _hopTimer = 0;
                        VelocityY = _config.HopperJumpSpeed;
                    }

                    break;
            }

            VelocityY = Math.Max(VelocityY - _config.Gravity, -_config.MaxFallSpeed);
            if (BodyMotion.MoveY(map, ref body, VelocityY))
            {
                VelocityY = 0;
            }

            Body = body;
            if (Body.Top <= 0)
            {
                State = EnemyState.Fell;
                EndedTick = tick;
            }
        }

        public void Stomp(long tick)
        {
            if (!IsAlive)
            {
                throw new InvalidOperationException("倒れた敵は踏めません。");
            }

            State = EnemyState.Stomped;
            EndedTick = tick;
        }

        private void Walk(TileMap map, ref Box body, int speed, bool turnAtLedge, bool grounded)
        {
            if (turnAtLedge && grounded && !GroundProbe.HasGroundAhead(map, body, Facing, speed))
            {
                Facing = -Facing;
            }

            if (BodyMotion.MoveX(map, ref body, Facing * speed))
            {
                Facing = -Facing;
            }
        }
    }

    public readonly struct Size
    {
        public Size(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }

        public int Height { get; }
    }
}
