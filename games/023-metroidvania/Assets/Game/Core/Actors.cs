using System;
using System.Collections.Generic;
using TileMotion;

namespace Game.Core
{
    /// <summary>自機の弾。向いている方向へ真横に進み、射程が尽きるか、固体・ひびの壁・敵に当たると消える。</summary>
    public sealed class Shot
    {
        public const int Width = 6 * Units.Pixel;
        public const int Height = 4 * Units.Pixel;

        public Shot(Box body, int direction, int ticks, bool breaking)
        {
            if (direction != 1 && direction != -1) { throw new ArgumentOutOfRangeException(nameof(direction), direction, "1 か -1 にしてください。"); }
            Body = body;
            Direction = direction;
            TicksLeft = ticks;
            Breaking = breaking;
        }

        public Box Body { get; private set; }

        public int Direction { get; }

        public int TicksLeft { get; private set; }

        /// <summary>撃った時点で破壊ショットを持っていたか。持っていればひびの壁を壊す。</summary>
        public bool Breaking { get; }

        /// <summary>
        /// 自機の体の中央の高さで、前の端の内側から出す弾。体の中から出すので、壁に密着していても壁の中に生まれない。
        /// </summary>
        public static Box SpawnBox(Box player, int facing)
        {
            var y = player.Y + ((player.Height - Height) / 2);
            var x = facing > 0 ? player.Right - Width : player.Left;
            return new Box(x, y, Width, Height);
        }

        public Shot Clone() => (Shot)MemberwiseClone();

        /// <summary>1 Tick 進める。地形に当たって止まったら、当たった列を <paramref name="wallColumn"/> に返して true。</summary>
        public bool Move(ISolidGrid terrain, int speed, out int wallColumn)
        {
            var body = Body;
            var stopped = BodyMotion.MoveX(terrain, ref body, Direction * speed);
            Body = body;
            TicksLeft--;
            wallColumn = Direction > 0 ? Units.FloorDiv(body.Right, Units.Tile) : Units.FloorDiv(body.Left, Units.Tile) - 1;
            return stopped;
        }

        public void MixInto(ref StateHash hash)
        {
            hash.Mix(Body.X);
            hash.Mix(Body.Y);
            hash.Mix(Direction);
            hash.Mix(TicksLeft);
            hash.Mix(Breaking);
        }
    }

    /// <summary>歩く敵。床の上を往復し、壁と足場の端で折り返す。自機の弾で体力が減り、0 で倒れる。</summary>
    public sealed class Walker
    {
        public const int Width = 14 * Units.Pixel;
        public const int Height = 12 * Units.Pixel;
        public const int FlashTicks = 8;

        private Countdown _flash;

        public Walker(Box body, int health)
        {
            if (health <= 0) { throw new ArgumentOutOfRangeException(nameof(health), health, "体力は正の値にしてください。"); }
            Body = body;
            Health = health;
            Facing = -1;
        }

        public Box Body { get; private set; }

        public int Facing { get; private set; }

        public int Health { get; private set; }

        public int VelocityY { get; private set; }

        public bool IsAlive => Health > 0;

        /// <summary>弾が当たった直後の点滅の残り Tick 数（描画用）。</summary>
        public int FlashRemaining => _flash.Remaining;

        public Walker Clone() => (Walker)MemberwiseClone();

        public void Step(ISolidGrid terrain, ExplorerConfig config)
        {
            _flash.Tick();
            if (!IsAlive) { return; }
            var body = Body;
            var grounded = VelocityY <= 0 && GroundProbe.IsGrounded(terrain, body);
            if (grounded && !HasGroundAhead(terrain, body, Facing, config.WalkerSpeed))
            {
                Facing = -Facing;
            }

            if (BodyMotion.MoveX(terrain, ref body, Facing * config.WalkerSpeed))
            {
                Facing = -Facing;
            }

            VelocityY = Math.Max(VelocityY - config.Gravity, -config.MaxFallSpeed);
            if (BodyMotion.MoveY(terrain, ref body, VelocityY)) { VelocityY = 0; }
            Body = body;
        }

        /// <summary>弾が当たった。倒れたら true。</summary>
        public bool Hit()
        {
            if (!IsAlive) { throw new InvalidOperationException("倒れた敵には当たりません。"); }
            Health--;
            _flash.Arm(FlashTicks);
            return !IsAlive;
        }

        public void MixInto(ref StateHash hash)
        {
            hash.Mix(Body.X);
            hash.Mix(Body.Y);
            hash.Mix(Facing);
            hash.Mix(Health);
            hash.Mix(VelocityY);
            hash.Mix(_flash.Remaining);
        }

        /// <summary>向き <paramref name="direction"/> に <paramref name="step"/> 進んだ先の足元に床があるか。</summary>
        private static bool HasGroundAhead(ISolidGrid terrain, Box box, int direction, int step)
        {
            var front = direction > 0 ? box.Right + step - 1 : box.Left - step;
            return terrain.IsSolid(Units.FloorDiv(front, Units.Tile), Units.FloorDiv(box.Bottom - 1, Units.Tile));
        }
    }

    public enum BossPhase
    {
        /// <summary>構え。終わりに自機のほうを向いて突進する。</summary>
        Idle,

        /// <summary>突進。壁に当たると気絶する。</summary>
        Charge,

        /// <summary>壁に当たって気絶している。終わりに自機のほうへ 3 方向の弾を撃って構えに戻る。</summary>
        Stunned,
    }

    /// <summary>
    /// ボス。乱数を使わず、「構え → 突進 → 気絶 → 3 方向の弾」を Tick で繰り返す。向きはその時点の自機の位置で決まる。
    /// 体力が 0 になると倒れ、試合に勝つ。
    /// </summary>
    public sealed class Boss
    {
        public const int Width = 28 * Units.Pixel;
        public const int Height = 28 * Units.Pixel;

        private Countdown _flash;

        public Boss(Box body, int health, int idleTicks)
        {
            if (health <= 0) { throw new ArgumentOutOfRangeException(nameof(health), health, "体力は正の値にしてください。"); }
            Body = body;
            Health = health;
            MaxHealth = health;
            Facing = -1;
            Phase = BossPhase.Idle;
            PhaseLeft = idleTicks;
        }

        public Box Body { get; private set; }

        public int Facing { get; private set; }

        public int Health { get; private set; }

        public int MaxHealth { get; }

        public BossPhase Phase { get; private set; }

        /// <summary>今の段階の残り Tick 数（突進中は使わない）。</summary>
        public int PhaseLeft { get; private set; }

        public int VelocityY { get; private set; }

        public bool IsAlive => Health > 0;

        public int FlashRemaining => _flash.Remaining;

        public Boss Clone() => (Boss)MemberwiseClone();

        /// <summary>1 Tick 進める。撃った弾を <paramref name="bullets"/> に足す。</summary>
        public void Step(ISolidGrid terrain, ExplorerConfig config, Box player, List<Bullet> bullets)
        {
            _flash.Tick();
            if (!IsAlive) { return; }
            var body = Body;
            switch (Phase)
            {
                case BossPhase.Idle:
                    if (--PhaseLeft <= 0)
                    {
                        Facing = Toward(player);
                        Phase = BossPhase.Charge;
                    }

                    break;
                case BossPhase.Charge:
                    if (BodyMotion.MoveX(terrain, ref body, Facing * config.BossChargeSpeed))
                    {
                        Phase = BossPhase.Stunned;
                        PhaseLeft = config.BossStunTicks;
                    }

                    break;
                case BossPhase.Stunned:
                    if (--PhaseLeft <= 0)
                    {
                        Facing = Toward(player);
                        Fire(config, bullets);
                        Phase = BossPhase.Idle;
                        PhaseLeft = config.BossIdleTicks;
                    }

                    break;
            }

            VelocityY = Math.Max(VelocityY - config.Gravity, -config.MaxFallSpeed);
            if (BodyMotion.MoveY(terrain, ref body, VelocityY)) { VelocityY = 0; }
            Body = body;
        }

        /// <summary>弾が当たった。倒れたら true。</summary>
        public bool Hit()
        {
            if (!IsAlive) { throw new InvalidOperationException("倒れたボスには当たりません。"); }
            Health--;
            _flash.Arm(Walker.FlashTicks);
            return !IsAlive;
        }

        public void MixInto(ref StateHash hash)
        {
            hash.Mix(Body.X);
            hash.Mix(Body.Y);
            hash.Mix(Facing);
            hash.Mix(Health);
            hash.Mix((int)Phase);
            hash.Mix(PhaseLeft);
            hash.Mix(VelocityY);
            hash.Mix(_flash.Remaining);
        }

        private int Toward(Box player) => player.CenterX >= Body.CenterX ? 1 : -1;

        private void Fire(ExplorerConfig config, List<Bullet> bullets)
        {
            var speed = config.BossBulletSpeed;
            var diagonal = ExplorerConfig.DiagonalOf(speed);
            var x = Facing > 0 ? Body.Right : Body.Left - Bullet.Size;
            var y = Body.Y + ((Body.Height - Bullet.Size) / 2);
            var box = new Box(x, y, Bullet.Size, Bullet.Size);
            bullets.Add(new Bullet(box, Facing * speed, 0));
            bullets.Add(new Bullet(box, Facing * diagonal, diagonal / 2));
            bullets.Add(new Bullet(box, Facing * diagonal, diagonal));
        }
    }

    /// <summary>ボスの弾。真っすぐ進み、固体に重なるか部屋の外へ出ると消える。自機に当たっても消える。</summary>
    public sealed class Bullet
    {
        public const int Size = 8 * Units.Pixel;

        public Bullet(Box body, int velocityX, int velocityY)
        {
            Body = body;
            Previous = body;
            VelocityX = velocityX;
            VelocityY = velocityY;
        }

        public Box Body { get; private set; }

        /// <summary>直前の Tick の始めの位置（自機との掃引に使う）。</summary>
        public Box Previous { get; private set; }

        public int VelocityX { get; }

        public int VelocityY { get; }

        /// <summary>この Tick の終わりに消える。</summary>
        public bool IsGone { get; private set; }

        public Bullet Clone() => (Bullet)MemberwiseClone();

        public void Move()
        {
            Previous = Body;
            Body = Body.Offset(VelocityX, VelocityY);
        }

        public void MarkGone() => IsGone = true;

        public void MixInto(ref StateHash hash)
        {
            hash.Mix(Body.X);
            hash.Mix(Body.Y);
            hash.Mix(VelocityX);
            hash.Mix(VelocityY);
        }
    }
}
