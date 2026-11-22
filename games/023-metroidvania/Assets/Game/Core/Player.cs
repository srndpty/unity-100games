using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>1 Tick 分の入力。押している状態で、ジャンプ・ショットの押し始めは自機が前の Tick との差で見分ける。</summary>
    public readonly struct PlayerInput : IEquatable<PlayerInput>
    {
        public PlayerInput(int horizontal, bool jump, bool shoot)
        {
            if (horizontal < -1 || horizontal > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(horizontal), horizontal, "-1・0・1 のいずれかにしてください。");
            }

            Horizontal = horizontal;
            Jump = jump;
            Shoot = shoot;
        }

        public static PlayerInput None => default;

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int Horizontal { get; }

        public bool Jump { get; }

        public bool Shoot { get; }

        public bool Equals(PlayerInput other) => Horizontal == other.Horizontal && Jump == other.Jump && Shoot == other.Shoot;

        public override bool Equals(object? obj) => obj is PlayerInput other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Horizontal, Jump, Shoot);

        public override string ToString() => $"Input({Horizontal}{(Jump ? ", J" : string.Empty)}{(Shoot ? ", S" : string.Empty)})";

        public static bool operator ==(PlayerInput left, PlayerInput right) => left.Equals(right);

        public static bool operator !=(PlayerInput left, PlayerInput right) => !left.Equals(right);
    }

    /// <summary>
    /// 1 Tick の移動の経路。地形の解決と同じく、横に動いてから（<see cref="Start"/> → <see cref="Corner"/>）
    /// 縦に動く（<see cref="Corner"/> → <see cref="End"/>）。各区間は 1 軸だけの移動で、地形で止まった位置まで。
    /// </summary>
    public readonly struct Motion
    {
        public Motion(Box start, Box corner, Box end)
        {
            Start = start;
            Corner = corner;
            End = end;
        }

        public Box Start { get; }

        public Box Corner { get; }

        public Box End { get; }

        /// <summary>横の区間で通る範囲。</summary>
        public Box HorizontalSweep => Box.Span(Start, Corner);

        /// <summary>縦の区間で通る範囲。</summary>
        public Box VerticalSweep => Box.Span(Corner, End);

        /// <summary>経路のどこかで <paramref name="target"/> に重なるか（止まっている物との判定）。</summary>
        public bool Touches(Box target) => HorizontalSweep.Overlaps(target) || VerticalSweep.Overlaps(target);
    }

    /// <summary>1 Tick の間に自機がしたこと。</summary>
    [Flags]
    public enum PlayerActions
    {
        None = 0,
        Jumped = 1 << 0,
        DoubleJumped = 1 << 1,

        /// <summary>弾を撃った（弾は部屋が作る）。</summary>
        Shot = 1 << 2,
    }

    /// <summary>
    /// 自機の制御。1 Tick の順序は
    /// 「接地の確認 → 押し始めの検出（ジャンプはバッファへ）→ 接地猶予 → ジャンプの解決（地上・猶予中 &gt; 二段ジャンプ）→
    /// 横の加減速（ノックバック中は効かない）→ 可変ジャンプ → 重力 → 横移動 → 縦移動 → 接地の確認と二段ジャンプの回復 → ショット → 無敵などの残り Tick を減らす」。
    /// 体力は部屋をまたいで持ち越す。
    /// </summary>
    public sealed class Player
    {
        public const int Width = 10 * Units.Pixel;
        public const int Height = 14 * Units.Pixel;

        private readonly ExplorerConfig _config;
        private bool _jumpWasHeld;
        private bool _shootWasHeld;
        private bool _rising;
        private bool _airJumpUsed;
        private Countdown _coyote;
        private Countdown _buffer;
        private Countdown _knockback;
        private Countdown _invincible;
        private Countdown _cooldown;

        public Player(ExplorerConfig config, Box box)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (box.Width != Width || box.Height != Height)
            {
                throw new ArgumentException($"自機の大きさは {Width}×{Height} です。", nameof(box));
            }

            Body = box;
            LastMotion = new Motion(box, box, box);
            Facing = 1;
            Health = _config.MaxHealth;

            // 開始・再開の前から押したままのキーで、すぐに跳んだり撃ったりしない（一度離すまで受け付けない）。
            _jumpWasHeld = true;
            _shootWasHeld = true;
        }

        public Box Body { get; private set; }

        /// <summary>直前の Tick の移動の経路（アイテム・扉・セーブ像の掃引に使う）。</summary>
        public Motion LastMotion { get; private set; }

        public int VelocityX { get; private set; }

        public int VelocityY { get; private set; }

        public bool Grounded { get; private set; }

        /// <summary>向き（右 1・左 -1）。弾はこの向きに飛ぶ。</summary>
        public int Facing { get; private set; }

        public int Health { get; private set; }

        public bool IsDown => Health <= 0;

        /// <summary>無敵の残り Tick 数（被弾の直後）。</summary>
        public int InvincibleRemaining => _invincible.Remaining;

        /// <summary>押し戻しの残り Tick 数（横入力が効かない間。描画では被弾の姿にする）。</summary>
        public int KnockbackRemaining => _knockback.Remaining;

        /// <summary>空中で二段ジャンプを使ったか（接地で戻る）。</summary>
        public bool AirJumpUsed => _airJumpUsed;

        /// <summary>直前の Tick にジャンプキーを押していたか（押し始めの検出に使う）。</summary>
        public bool JumpHeld => _jumpWasHeld;

        public int CoyoteRemaining => _coyote.Remaining;

        public int BufferRemaining => _buffer.Remaining;

        public Player Clone() => (Player)MemberwiseClone();

        /// <summary>以後の動きを決める状態をすべて混ぜる（残り Tick・直前のキーの押下を含む）。</summary>
        public void MixInto(ref StateHash hash)
        {
            hash.Mix(Body.X);
            hash.Mix(Body.Y);
            hash.Mix(VelocityX);
            hash.Mix(VelocityY);
            hash.Mix(Grounded);
            hash.Mix(Facing);
            hash.Mix(Health);
            hash.Mix(_jumpWasHeld);
            hash.Mix(_shootWasHeld);
            hash.Mix(_rising);
            hash.Mix(_airJumpUsed);
            hash.Mix(_coyote.Remaining);
            hash.Mix(_buffer.Remaining);
            hash.Mix(_knockback.Remaining);
            hash.Mix(_invincible.Remaining);
            hash.Mix(_cooldown.Remaining);
        }

        /// <summary>
        /// 1 Tick 進める。<paramref name="abilities"/> は今の能力、<paramref name="canShoot"/> は画面の弾が上限に達していないか。
        /// </summary>
        public PlayerActions Step(ISolidGrid terrain, PlayerInput input, Ability abilities, bool canShoot)
        {
            if (terrain == null) { throw new ArgumentNullException(nameof(terrain)); }
            var actions = PlayerActions.None;
            Grounded = VelocityY <= 0 && GroundProbe.IsGrounded(terrain, Body);

            var jumpPressed = input.Jump && !_jumpWasHeld;
            var shootPressed = input.Shoot && !_shootWasHeld;
            _jumpWasHeld = input.Jump;
            _shootWasHeld = input.Shoot;
            if (jumpPressed)
            {
                _buffer.Arm(_config.JumpBufferTicks + 1);
            }

            var canGroundJump = Grounded || _coyote.IsActive;
            if (Grounded)
            {
                _coyote.Arm(_config.CoyoteTicks);
            }
            else
            {
                _coyote.Tick();
            }

            actions |= ResolveJump(canGroundJump, (abilities & Ability.DoubleJump) != 0);
            _buffer.Tick();
            if (_knockback.IsActive)
            {
                _knockback.Tick();
            }
            else
            {
                Steer(input.Horizontal);
            }

            if (_rising && !input.Jump && VelocityY > _config.JumpCutSpeed)
            {
                VelocityY = _config.JumpCutSpeed;
            }

            VelocityY = Math.Max(VelocityY - _config.Gravity, -_config.MaxFallSpeed);
            if (VelocityY <= 0) { _rising = false; }

            var start = Body;
            var body = Body;
            if (BodyMotion.MoveX(terrain, ref body, VelocityX)) { VelocityX = 0; }
            var corner = body;
            if (BodyMotion.MoveY(terrain, ref body, VelocityY))
            {
                VelocityY = 0;
                _rising = false;
            }

            Body = body;
            LastMotion = new Motion(start, corner, body);
            Grounded = VelocityY <= 0 && GroundProbe.IsGrounded(terrain, Body);
            if (Grounded) { _airJumpUsed = false; }

            if (shootPressed && canShoot && !_cooldown.IsActive)
            {
                _cooldown.Arm(_config.ShotCooldownTicks);
                actions |= PlayerActions.Shot;
            }
            else
            {
                _cooldown.Tick();
            }

            _invincible.Tick();
            return actions;
        }

        /// <summary>
        /// ダメージを受ける。無敵中・倒れた後は何もせず false。<paramref name="away"/> は押し戻す向き（右 1・左 -1、0 なら向きの反対）。
        /// </summary>
        public bool Hurt(int damage, int away)
        {
            if (damage <= 0) { throw new ArgumentOutOfRangeException(nameof(damage), damage, "ダメージは正の値にしてください。"); }
            if (IsDown || _invincible.IsActive) { return false; }
            Health = Math.Max(0, Health - damage);
            // 被弾した Tick の次から数えて、無敵は InvincibleTicks、ノックバックは KnockbackTicks の Tick の間。
            // 無敵は Step の終わりで減るので 1 多く張る。ノックバックは Step の途中で確かめてから減らす。
            _invincible.Arm(_config.InvincibleTicks + 1);
            _knockback.Arm(_config.KnockbackTicks);
            var direction = away != 0 ? Math.Sign(away) : -Facing;
            VelocityX = direction * _config.KnockbackSpeedX;
            VelocityY = _config.KnockbackSpeedY;
            _rising = false;
            _buffer.Clear();
            _coyote.Clear();
            return true;
        }

        /// <summary>部屋の外へ落ちたなど、体力をすべて失う。</summary>
        public void Fall() => Health = 0;

        /// <summary>体力を戻す（最大まで）。戻った量を返す。</summary>
        public int Heal(int amount)
        {
            if (amount <= 0) { throw new ArgumentOutOfRangeException(nameof(amount), amount, "回復量は正の値にしてください。"); }
            var before = Health;
            Health = Math.Min(_config.MaxHealth, Health + amount);
            return Health - before;
        }

        /// <summary>
        /// 部屋を移るときに、到着位置へ置き直す。速度・猶予・バッファ・ノックバックは捨て、向き・体力・無敵・キーの押下は持ち越す
        /// （押したままのジャンプで移った先ですぐに跳ばない）。
        /// </summary>
        public void Teleport(Box box)
        {
            if (box.Width != Width || box.Height != Height)
            {
                throw new ArgumentException($"自機の大きさは {Width}×{Height} です。", nameof(box));
            }

            Body = box;
            LastMotion = new Motion(box, box, box);
            VelocityX = 0;
            VelocityY = 0;
            Grounded = false;
            _rising = false;
            _airJumpUsed = false;
            _coyote.Clear();
            _buffer.Clear();
            _knockback.Clear();
        }

        private PlayerActions ResolveJump(bool canGroundJump, bool hasDoubleJump)
        {
            if (!_buffer.IsActive) { return PlayerActions.None; }
            if (canGroundJump)
            {
                VelocityY = _config.JumpSpeed;
                Consume();
                return PlayerActions.Jumped;
            }

            if (!hasDoubleJump || _airJumpUsed)
            {
                // 二段ジャンプがなければ、押下はバッファの中で着地を待つ。
                return PlayerActions.None;
            }

            VelocityY = _config.DoubleJumpSpeed;
            _airJumpUsed = true;
            Consume();
            return PlayerActions.DoubleJumped;
        }

        private void Consume()
        {
            _rising = true;
            Grounded = false;
            _buffer.Clear();
            _coyote.Clear();
        }

        private void Steer(int direction)
        {
            if (direction != 0) { Facing = direction; }
            var vx = VelocityX;
            var max = _config.MoveSpeed;
            var accel = Grounded ? _config.GroundAccel : _config.AirAccel;
            var decel = Grounded ? _config.GroundDecel : _config.AirDecel;
            if (direction == 0)
            {
                VelocityX = Toward(vx, 0, decel);
            }
            else if (vx * direction > max)
            {
                VelocityX = Toward(vx, direction * max, decel);
            }
            else
            {
                VelocityX = Toward(vx, direction * max, accel);
            }
        }

        private static int Toward(int value, int target, int step)
        {
            return value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);
        }
    }
}
