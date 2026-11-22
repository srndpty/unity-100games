using System;
using System.Collections.Generic;
using TileMotion;

namespace Game.Core
{
    /// <summary>敵の動きの段階。</summary>
    public enum EnemyPhase
    {
        /// <summary>スライム・射手・突進兵のふだん。</summary>
        Idle,

        /// <summary>突進の前の構え（突進兵・ボス）。</summary>
        Windup,

        /// <summary>壁まで突進する（突進兵・ボス）。</summary>
        Charge,

        /// <summary>壁に当たって動けない（突進兵・ボス）。</summary>
        Stun,

        /// <summary>自機のほうへ歩く（ボス）。</summary>
        Walk,

        /// <summary>8 方向の弾を撃った後に止まっている（ボス）。</summary>
        Volley,
    }

    /// <summary>敵とボスの弾。まっすぐ進み、壁か部屋の外で消える。</summary>
    public sealed class Projectile
    {
        public const int Size = 6 * Units.Pixel;

        public Projectile(Box body, int velocityX, int velocityY)
        {
            Body = body;
            Previous = body;
            VelocityX = velocityX;
            VelocityY = velocityY;
        }

        public Box Body { get; private set; }

        /// <summary>この Tick の始めの位置（接触は Previous → Body の軌跡で判定する）。</summary>
        public Box Previous { get; private set; }

        public int VelocityX { get; }

        public int VelocityY { get; }

        public bool IsGone { get; private set; }

        public Projectile Clone() => (Projectile)MemberwiseClone();

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

    /// <summary>
    /// 敵（スライム・射手・突進兵・ボス）。部屋に入るたびに出現位置から満タンで始まる。剣が当たると体力が減り、押し戻され、
    /// 次の Tick から <see cref="VaultConfig.EnemyInvulnTicks"/> Tick 無敵になる。1 回の振りでは同じ敵に一度しか当たらない。
    /// 動きは種類ごとに次のとおり（スライムだけが乱数を使う）。
    /// <list type="bullet">
    /// <item>スライム：乱数で 4 方向か止まるを選び、乱数の Tick 数だけ続ける。壁に当たったら選び直す</item>
    /// <item>射手：動かない。自機と縦か横に並ぶ（体の帯が重なる）と、そちらを向いて弾を撃つ。撃った後は一定 Tick 撃たない</item>
    /// <item>突進兵：自機と縦か横に並び、間に固体がなければ構え、その向きに壁まで突進し、壁で気絶する</item>
    /// <item>ボス：歩く（自機のほうへ）→ 8 方向の弾 → 構え → 突進 → 気絶 を繰り返す（乱数なし）</item>
    /// </list>
    /// </summary>
    public sealed class Enemy
    {
        public const int FlashTicks = 8;

        private bool _hurtThisTick;
        private int _knockX;
        private int _knockY;
        private Countdown _invuln;
        private Countdown _knockback;
        private Countdown _flash;
        private int _moveX;
        private int _moveY;

        public Enemy(EnemyKind kind, Box body, int health)
        {
            if (!Enum.IsDefined(typeof(EnemyKind), kind)) { throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の敵です。"); }
            if (health <= 0) { throw new ArgumentOutOfRangeException(nameof(health), health, "体力は正の値にしてください。"); }
            Kind = kind;
            Body = body;
            Previous = body;
            Health = health;
            Facing = Facing.Down;
            Phase = kind == EnemyKind.Boss ? EnemyPhase.Walk : EnemyPhase.Idle;
            LastSwing = -1;
        }

        public EnemyKind Kind { get; }

        public Box Body { get; private set; }

        /// <summary>この Tick の始めの位置。</summary>
        public Box Previous { get; private set; }

        public int Health { get; private set; }

        public bool IsAlive => Health > 0;

        public Facing Facing { get; private set; }

        public EnemyPhase Phase { get; private set; }

        /// <summary>今の段階の残り Tick 数（射手は次に撃てるまで）。</summary>
        public int Timer { get; private set; }

        /// <summary>最後に当たった振りの通し番号。</summary>
        public int LastSwing { get; private set; }

        public bool IsInvulnerable => _invuln.IsActive;

        /// <summary>剣が当たった直後の点滅の残り Tick 数（描画用）。</summary>
        public int FlashRemaining => _flash.Remaining;

        public static int SizeOf(EnemyKind kind) => kind switch
        {
            EnemyKind.Slime => 12 * Units.Pixel,
            EnemyKind.Spitter => 12 * Units.Pixel,
            EnemyKind.Charger => 14 * Units.Pixel,
            EnemyKind.Boss => 28 * Units.Pixel,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の敵です。"),
        };

        public static int HealthOf(EnemyKind kind, VaultConfig config) => kind switch
        {
            EnemyKind.Slime => config.SlimeHealth,
            EnemyKind.Spitter => config.SpitterHealth,
            EnemyKind.Charger => config.ChargerHealth,
            EnemyKind.Boss => config.BossHealth,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の敵です。"),
        };

        /// <summary>出現位置に満タンで置いた敵。</summary>
        public static Enemy At(Spawn spawn, VaultConfig config)
        {
            var enemy = new Enemy(spawn.Kind, spawn.Cell.Centered(SizeOf(spawn.Kind)), HealthOf(spawn.Kind, config));
            if (spawn.Kind == EnemyKind.Boss) { enemy.Timer = config.BossWalkTicks; }
            return enemy;
        }

        public Enemy Clone() => (Enemy)MemberwiseClone();

        /// <summary>1 Tick 進める。撃った弾は <paramref name="projectiles"/> に足す。</summary>
        public void Step(ISolidGrid terrain, VaultConfig config, Box hero, SeededRandom random, List<Projectile> projectiles)
        {
            Previous = Body;
            _flash.Tick();
            if (!IsAlive) { return; }
            if (_knockback.IsActive)
            {
                Move(terrain, _knockX, _knockY);
                _knockback.Tick();
                return;
            }

            switch (Kind)
            {
                case EnemyKind.Slime:
                    StepSlime(terrain, config, random);
                    break;
                case EnemyKind.Spitter:
                    StepSpitter(config, hero, projectiles);
                    break;
                case EnemyKind.Charger:
                    StepCharger(terrain, config, hero);
                    break;
                case EnemyKind.Boss:
                    StepBoss(terrain, config, hero, projectiles);
                    break;
                default:
                    throw new InvalidOperationException("未定義の敵です。");
            }
        }

        /// <summary>
        /// 剣が当たった。倒れている・無敵・この振りで既に当たっていれば何もせず false。当たれば体力を減らし、
        /// <paramref name="from"/>（自機の向き）へ押し戻す（ボスは押し戻さない）。
        /// </summary>
        public bool Hit(int swingSerial, Facing from, int damage, VaultConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (damage <= 0) { throw new ArgumentOutOfRangeException(nameof(damage), damage, "ダメージは正の値にしてください。"); }
            if (!IsAlive || IsInvulnerable || swingSerial == LastSwing) { return false; }
            LastSwing = swingSerial;
            Health = Math.Max(0, Health - damage);
            _invuln.Arm(config.EnemyInvulnTicks);
            _hurtThisTick = true;
            _flash.Arm(FlashTicks);
            if (Kind != EnemyKind.Boss)
            {
                _knockX = Facings.Dx(from) * config.EnemyKnockbackSpeed;
                _knockY = Facings.Dy(from) * config.EnemyKnockbackSpeed;
                _knockback.Arm(config.EnemyKnockbackTicks);
            }

            return true;
        }

        /// <summary>Tick の終わり。無敵の残りを 1 減らす（剣を受けた Tick は減らさない）。</summary>
        public void EndTick()
        {
            if (!_hurtThisTick) { _invuln.Tick(); }
            _hurtThisTick = false;
        }

        public void MixInto(ref StateHash hash)
        {
            hash.Mix((int)Kind);
            hash.Mix(Body.X);
            hash.Mix(Body.Y);
            hash.Mix(Health);
            hash.Mix((int)Facing);
            hash.Mix((int)Phase);
            hash.Mix(Timer);
            hash.Mix(LastSwing);
            hash.Mix(_moveX);
            hash.Mix(_moveY);
            hash.Mix(_knockX);
            hash.Mix(_knockY);
            hash.Mix(_invuln.Remaining);
            hash.Mix(_knockback.Remaining);
            hash.Mix(_flash.Remaining);
        }

        /// <summary>自機と縦か横に並んでいれば、その向き。並んでいなければ null。</summary>
        public static Facing? Aligned(Box self, Box hero)
        {
            if (self.Bottom < hero.Top && hero.Bottom < self.Top) { return hero.CenterX >= self.CenterX ? Facing.Right : Facing.Left; }
            if (self.Left < hero.Right && hero.Left < self.Right) { return Boxes.CenterY(hero) >= Boxes.CenterY(self) ? Facing.Up : Facing.Down; }
            return null;
        }

        private void StepSlime(ISolidGrid terrain, VaultConfig config, SeededRandom random)
        {
            if (Timer <= 0)
            {
                var choice = random.Next(5);
                _moveX = choice == 0 ? 1 : choice == 2 ? -1 : 0;
                _moveY = choice == 1 ? 1 : choice == 3 ? -1 : 0;
                if (choice < 4) { Facing = (Facing)choice; }
                Timer = random.Range(config.SlimeMinTicks, config.SlimeMaxTicks);
            }

            Timer--;
            if (Move(terrain, _moveX * config.SlimeSpeed, _moveY * config.SlimeSpeed)) { Timer = 0; }
        }

        private void StepSpitter(VaultConfig config, Box hero, List<Projectile> projectiles)
        {
            if (Timer > 0)
            {
                Timer--;
                return;
            }

            if (Aligned(Body, hero) is Facing facing)
            {
                Facing = facing;
                Fire(projectiles, Facings.Dx(facing) * config.SpitterShotSpeed, Facings.Dy(facing) * config.SpitterShotSpeed);
                Timer = config.SpitterFireTicks;
            }
        }

        private void StepCharger(ISolidGrid terrain, VaultConfig config, Box hero)
        {
            switch (Phase)
            {
                case EnemyPhase.Idle:
                    if (Aligned(Body, hero) is Facing facing && ClearLine(terrain, hero, facing))
                    {
                        Facing = facing;
                        Phase = EnemyPhase.Windup;
                        Timer = config.ChargerWindupTicks;
                    }

                    break;
                case EnemyPhase.Windup:
                    if (--Timer <= 0) { Phase = EnemyPhase.Charge; }
                    break;
                case EnemyPhase.Charge:
                    if (Move(terrain, Facings.Dx(Facing) * config.ChargerSpeed, Facings.Dy(Facing) * config.ChargerSpeed))
                    {
                        Phase = EnemyPhase.Stun;
                        Timer = config.ChargerStunTicks;
                    }

                    break;
                case EnemyPhase.Stun:
                    if (--Timer <= 0) { Phase = EnemyPhase.Idle; }
                    break;
                default:
                    throw new InvalidOperationException($"突進兵に段階 {Phase} はありません。");
            }
        }

        private void StepBoss(ISolidGrid terrain, VaultConfig config, Box hero, List<Projectile> projectiles)
        {
            var toward = Facings.Toward((long)hero.CenterX - Body.CenterX, (long)Boxes.CenterY(hero) - Boxes.CenterY(Body), Facing);
            switch (Phase)
            {
                case EnemyPhase.Walk:
                    Facing = toward;
                    Move(terrain, Facings.Dx(toward) * config.BossWalkSpeed, Facings.Dy(toward) * config.BossWalkSpeed);
                    if (--Timer <= 0)
                    {
                        var diagonal = VaultConfig.DiagonalOf(config.BossShotSpeed);
                        var straight = config.BossShotSpeed;
                        for (var i = 0; i < 8; i++)
                        {
                            var (x, y) = _eight[i];
                            var speed = x != 0 && y != 0 ? diagonal : straight;
                            Fire(projectiles, x * speed, y * speed);
                        }

                        Phase = EnemyPhase.Volley;
                        Timer = config.BossVolleyTicks;
                    }

                    break;
                case EnemyPhase.Volley:
                    if (--Timer <= 0)
                    {
                        Facing = toward;
                        Phase = EnemyPhase.Windup;
                        Timer = config.BossWindupTicks;
                    }

                    break;
                case EnemyPhase.Windup:
                    if (--Timer <= 0) { Phase = EnemyPhase.Charge; }
                    break;
                case EnemyPhase.Charge:
                    if (Move(terrain, Facings.Dx(Facing) * config.BossChargeSpeed, Facings.Dy(Facing) * config.BossChargeSpeed))
                    {
                        Phase = EnemyPhase.Stun;
                        Timer = config.BossStunTicks;
                    }

                    break;
                case EnemyPhase.Stun:
                    if (--Timer <= 0)
                    {
                        Phase = EnemyPhase.Walk;
                        Timer = config.BossWalkTicks;
                    }

                    break;
                default:
                    throw new InvalidOperationException($"ボスに段階 {Phase} はありません。");
            }
        }

        private static readonly (int X, int Y)[] _eight = { (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1) };

        /// <summary>自機までの間（向きの軸に沿って体を動かした範囲）に固体がないか。</summary>
        private bool ClearLine(ISolidGrid terrain, Box hero, Facing facing)
        {
            var target = facing == Facing.Right || facing == Facing.Left ? Body.At(hero.CenterX - (Body.Width / 2), Body.Y) : Body.At(Body.X, Boxes.CenterY(hero) - (Body.Height / 2));
            return !SolidQuery.Overlaps(terrain, Box.Span(Body, target));
        }

        private void Fire(List<Projectile> projectiles, int vx, int vy)
        {
            var box = new Box(Body.CenterX - (Projectile.Size / 2), Boxes.CenterY(Body) - (Projectile.Size / 2), Projectile.Size, Projectile.Size);
            projectiles.Add(new Projectile(box, vx, vy));
        }

        /// <summary>地形と衝突を解決して動く。どちらかの軸で止まったら true。</summary>
        private bool Move(ISolidGrid terrain, int dx, int dy)
        {
            var body = Body;
            var blockedX = BodyMotion.MoveX(terrain, ref body, dx);
            var blockedY = BodyMotion.MoveY(terrain, ref body, dy);
            Body = body;
            return blockedX || blockedY;
        }
    }
}
