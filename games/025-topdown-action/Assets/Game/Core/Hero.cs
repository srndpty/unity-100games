using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>1 Tick 分の入力。押している状態で、剣・ロールの押し始めは自機が前の Tick との差で見分ける。</summary>
    public readonly struct HeroInput : IEquatable<HeroInput>
    {
        public HeroInput(int x, int y, bool attack, bool roll)
        {
            if (x < -1 || x > 1) { throw new ArgumentOutOfRangeException(nameof(x), x, "-1・0・1 のいずれかにしてください。"); }
            if (y < -1 || y > 1) { throw new ArgumentOutOfRangeException(nameof(y), y, "-1・0・1 のいずれかにしてください。"); }
            X = x;
            Y = y;
            Attack = attack;
            Roll = roll;
        }

        public static HeroInput None => default;

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int X { get; }

        /// <summary>下 -1・なし 0・上 1。</summary>
        public int Y { get; }

        public bool Attack { get; }

        public bool Roll { get; }

        public bool Equals(HeroInput other) => X == other.X && Y == other.Y && Attack == other.Attack && Roll == other.Roll;

        public override bool Equals(object? obj) => obj is HeroInput other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, Attack, Roll);

        public override string ToString() => $"Input({X}, {Y}{(Attack ? ", A" : string.Empty)}{(Roll ? ", R" : string.Empty)})";

        public static bool operator ==(HeroInput left, HeroInput right) => left.Equals(right);

        public static bool operator !=(HeroInput left, HeroInput right) => !left.Equals(right);
    }

    /// <summary>1 Tick の間に自機がしたこと。</summary>
    [Flags]
    public enum HeroActions
    {
        None = 0,

        /// <summary>ロールを始めた。</summary>
        Rolled = 1 << 0,

        /// <summary>剣のキーを押し始め、振れる状態だった（振るか宝箱を開けるかは部屋が決める）。</summary>
        AttackReady = 1 << 1,
    }

    /// <summary>剣の当たりの箱。判定と描画で同じ関数を使う。</summary>
    public static class SwordHitbox
    {
        /// <summary>体 <paramref name="body"/> が <paramref name="facing"/> を向いたときの、体の前の剣の箱。</summary>
        public static Box For(Box body, Facing facing, VaultConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var reach = config.SwordReach;
            var width = config.SwordWidth;
            var x = body.CenterX - (width / 2);
            var y = Boxes.CenterY(body) - (width / 2);
            return facing switch
            {
                Facing.Right => new Box(body.Right, y, reach, width),
                Facing.Left => new Box(body.Left - reach, y, reach, width),
                Facing.Up => new Box(x, body.Top, width, reach),
                Facing.Down => new Box(x, body.Bottom - reach, width, reach),
                _ => throw new ArgumentOutOfRangeException(nameof(facing), facing, "未定義の向きです。"),
            };
        }
    }

    /// <summary>
    /// 自機。1 Tick の順序は「押し始めの検出 → 振り・ロールの Tick を進める → 移動（押し戻し ＞ ロール ＞ ロールの開始 ＞ 歩く）→
    /// 剣を振れるかを返す」。被弾は部屋が <see cref="Hurt"/> で伝え、Tick の終わりに <see cref="EndTick"/> で無敵の残りを減らす。
    /// 体力は部屋をまたいで持ち越す。
    /// </summary>
    public sealed class Hero
    {
        /// <summary>自機の幅と高さ（12 px）。</summary>
        public const int Size = 12 * Units.Pixel;

        private readonly VaultConfig _config;
        private bool _attackWasHeld;
        private bool _rollWasHeld;
        private bool _hurtThisTick;
        private int _rollX;
        private int _rollY;
        private int _knockX;
        private int _knockY;
        private Countdown _invuln;
        private Countdown _knockback;
        private Countdown _rollCooldown;

        public Hero(VaultConfig config, Box body)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (body.Width != Size || body.Height != Size) { throw new ArgumentException($"自機の大きさは {Size}×{Size} です。", nameof(body)); }
            Body = body;
            LastMotion = Motion.Still(body);
            Facing = Facing.Up;
            Health = config.MaxHealth;

            // 開始・再開の前から押したままのキーで、すぐに振ったり転がったりしない（一度離すまで受け付けない）。
            _attackWasHeld = true;
            _rollWasHeld = true;
        }

        public Box Body { get; private set; }

        /// <summary>直前の Tick の移動の経路。</summary>
        public Motion LastMotion { get; private set; }

        public Facing Facing { get; private set; }

        public int Health { get; private set; }

        public bool IsDown => Health <= 0;

        /// <summary>振りの何 Tick 目か（振っていなければ 0）。</summary>
        public int SwingTick { get; private set; }

        /// <summary>振りの通し番号。1 回の振りで同じ敵に二度当てないために使う。</summary>
        public int SwingSerial { get; private set; }

        /// <summary>ロールの何 Tick 目か（転がっていなければ 0）。</summary>
        public int RollTick { get; private set; }

        public bool IsSwinging => SwingTick > 0;

        public bool IsRolling => RollTick > 0;

        public bool IsKnockedBack => _knockback.IsActive;

        /// <summary>剣が当たる Tick か。</summary>
        public bool IsSwordActive => SwingTick >= _config.SwingActiveFrom && SwingTick <= _config.SwingActiveTo;

        /// <summary>この Tick の剣の箱（当たらない Tick は null）。</summary>
        public Box? Sword => IsSwordActive ? SwordHitbox.For(Body, Facing, _config) : (Box?)null;

        /// <summary>今の位置で <paramref name="facing"/> を向いたときの剣の箱（操縦役が届くかを調べる）。</summary>
        public Box SwordFacing(Facing facing) => SwordHitbox.For(Body, facing, _config);

        /// <summary>被弾の後の無敵の残り Tick 数（この Tick を含む）。</summary>
        public int InvulnRemaining => _invuln.Remaining;

        /// <summary>被弾の後の無敵か、ロールの始めの無敵か。</summary>
        public bool IsInvulnerable => _invuln.IsActive || (IsRolling && RollTick <= _config.RollInvulnTicks);

        /// <summary>次のロールができるまでの Tick 数。</summary>
        public int RollCooldownRemaining => _rollCooldown.Remaining;

        public Hero Clone() => (Hero)MemberwiseClone();

        /// <summary>
        /// 1 Tick 進める。<paramref name="terrain"/> で地形と衝突を解決する。剣のキーの押し始めで振れる状態なら <see cref="HeroActions.AttackReady"/> を返す
        /// （実際に振るのは <see cref="StartSwing"/>）。
        /// </summary>
        public HeroActions Step(ISolidGrid terrain, HeroInput input)
        {
            if (terrain == null) { throw new ArgumentNullException(nameof(terrain)); }
            var attackPressed = input.Attack && !_attackWasHeld;
            var rollPressed = input.Roll && !_rollWasHeld;
            _attackWasHeld = input.Attack;
            _rollWasHeld = input.Roll;
            var actions = HeroActions.None;

            if (IsSwinging && ++SwingTick > _config.SwingTicks) { SwingTick = 0; }
            _rollCooldown.Tick();
            if (IsRolling && ++RollTick > _config.RollTicks)
            {
                RollTick = 0;
                _rollCooldown.Arm(_config.RollCooldownTicks);
            }

            int dx, dy;
            if (_knockback.IsActive)
            {
                dx = _knockX;
                dy = _knockY;
                _knockback.Tick();
            }
            else if (IsRolling)
            {
                (dx, dy) = Velocity(_rollX, _rollY, _config.RollSpeed);
            }
            else if (rollPressed && !IsSwinging && !_rollCooldown.IsActive)
            {
                _rollX = input.X;
                _rollY = input.Y;
                if (_rollX == 0 && _rollY == 0)
                {
                    _rollX = Facings.Dx(Facing);
                    _rollY = Facings.Dy(Facing);
                }

                Turn(_rollX, _rollY);
                RollTick = 1;
                actions |= HeroActions.Rolled;
                (dx, dy) = Velocity(_rollX, _rollY, _config.RollSpeed);
            }
            else
            {
                if (!IsSwinging) { Turn(input.X, input.Y); }
                (dx, dy) = Velocity(input.X, input.Y, IsSwinging ? _config.SwingMoveSpeed : _config.HeroSpeed);
            }

            var start = Body;
            var body = start;
            BodyMotion.MoveX(terrain, ref body, dx);
            var corner = body;
            BodyMotion.MoveY(terrain, ref body, dy);
            Body = body;
            LastMotion = new Motion(start, corner, body);

            if (attackPressed && !IsSwinging && !IsRolling && !_knockback.IsActive) { actions |= HeroActions.AttackReady; }
            return actions;
        }

        /// <summary>剣を振り始める（この Tick が 1 Tick 目）。</summary>
        public void StartSwing()
        {
            if (IsSwinging || IsRolling) { throw new InvalidOperationException("振っている間・転がっている間は振れません。"); }
            SwingTick = 1;
            SwingSerial++;
        }

        /// <summary>
        /// ダメージを受ける。無敵なら何もせず false。受けたら体力を減らし、振り・ロールをやめて <paramref name="sourceX"/>, <paramref name="sourceY"/>
        /// （当たった物の中心）から遠ざかる向きに押し戻され、次の Tick から <see cref="VaultConfig.InvulnTicks"/> Tick 無敵になる。
        /// </summary>
        public bool Hurt(int damage, int sourceX, int sourceY)
        {
            if (damage <= 0) { throw new ArgumentOutOfRangeException(nameof(damage), damage, "ダメージは正の値にしてください。"); }
            if (IsInvulnerable) { return false; }
            Health = Math.Max(0, Health - damage);
            _invuln.Arm(_config.InvulnTicks);
            _hurtThisTick = true;
            SwingTick = 0;
            RollTick = 0;
            var away = Facings.Toward((long)Body.CenterX - sourceX, (long)Boxes.CenterY(Body) - sourceY, Opposite(Facing));
            _knockX = Facings.Dx(away) * _config.KnockbackSpeed;
            _knockY = Facings.Dy(away) * _config.KnockbackSpeed;
            _knockback.Arm(_config.KnockbackTicks);
            return true;
        }

        /// <summary>Tick の終わり。無敵の残りを 1 減らす（被弾した Tick は減らさないので、次の Tick から数える）。</summary>
        public void EndTick()
        {
            if (!_hurtThisTick) { _invuln.Tick(); }
            _hurtThisTick = false;
        }

        /// <summary>
        /// 隣の部屋の到着位置へ移る。振り・ロール・押し戻しは捨て、体力・無敵・向きは持ち越す。
        /// 押したままのキーで移った先ですぐに振らないよう、一度離すまで受け付けない。
        /// </summary>
        public void Arrive(Box body)
        {
            if (body.Width != Size || body.Height != Size) { throw new ArgumentException($"自機の大きさは {Size}×{Size} です。", nameof(body)); }
            Body = body;
            LastMotion = Motion.Still(body);
            SwingTick = 0;
            RollTick = 0;
            _knockback.Clear();
            _attackWasHeld = true;
            _rollWasHeld = true;
        }

        public void MixInto(ref StateHash hash)
        {
            hash.Mix(Body.X);
            hash.Mix(Body.Y);
            hash.Mix((int)Facing);
            hash.Mix(Health);
            hash.Mix(SwingTick);
            hash.Mix(SwingSerial);
            hash.Mix(RollTick);
            hash.Mix(_rollX);
            hash.Mix(_rollY);
            hash.Mix(_knockX);
            hash.Mix(_knockY);
            hash.Mix(_invuln.Remaining);
            hash.Mix(_knockback.Remaining);
            hash.Mix(_rollCooldown.Remaining);
            hash.Mix(_attackWasHeld);
            hash.Mix(_rollWasHeld);
        }

        /// <summary>入力の向きから向きを決める。今の向きの成分を押していれば変えない。両方の軸を押していれば横を優先する。</summary>
        private void Turn(int x, int y)
        {
            if (x == 0 && y == 0) { return; }
            if ((x != 0 && x == Facings.Dx(Facing)) || (y != 0 && y == Facings.Dy(Facing))) { return; }
            Facing = x != 0 ? (x > 0 ? Facing.Right : Facing.Left) : (y > 0 ? Facing.Up : Facing.Down);
        }

        private static (int, int) Velocity(int x, int y, int speed)
        {
            if (x != 0 && y != 0)
            {
                var diagonal = VaultConfig.DiagonalOf(speed);
                return (x * diagonal, y * diagonal);
            }

            return (x * speed, y * speed);
        }

        private static Facing Opposite(Facing facing) => facing switch
        {
            Facing.Right => Facing.Left,
            Facing.Left => Facing.Right,
            Facing.Up => Facing.Down,
            _ => Facing.Up,
        };
    }
}
