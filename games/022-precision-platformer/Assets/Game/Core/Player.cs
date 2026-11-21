using System;

namespace Game.Core
{
    /// <summary>1 Tick 分の入力。押している状態で、ジャンプ・ダッシュの押し始めは自機が前の Tick との差で見分ける。</summary>
    public readonly struct PlayerInput : IEquatable<PlayerInput>
    {
        public PlayerInput(int horizontal, int vertical, bool jump, bool dash)
        {
            if (horizontal < -1 || horizontal > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(horizontal), horizontal, "-1・0・1 のいずれかにしてください。");
            }

            if (vertical < -1 || vertical > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(vertical), vertical, "-1・0・1 のいずれかにしてください。");
            }

            Horizontal = horizontal;
            Vertical = vertical;
            Jump = jump;
            Dash = dash;
        }

        public static PlayerInput None => default;

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int Horizontal { get; }

        /// <summary>下 -1・なし 0・上 1。ダッシュの向きにだけ使う。</summary>
        public int Vertical { get; }

        public bool Jump { get; }

        public bool Dash { get; }

        public bool Equals(PlayerInput other) => Horizontal == other.Horizontal && Vertical == other.Vertical && Jump == other.Jump && Dash == other.Dash;

        public override bool Equals(object? obj) => obj is PlayerInput other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Horizontal, Vertical, Jump, Dash);

        public override string ToString() => $"Input({Horizontal}, {Vertical}{(Jump ? ", J" : string.Empty)}{(Dash ? ", D" : string.Empty)})";

        public static bool operator ==(PlayerInput left, PlayerInput right) => left.Equals(right);

        public static bool operator !=(PlayerInput left, PlayerInput right) => !left.Equals(right);
    }

    /// <summary>1 Tick の間に自機がしたこと。</summary>
    [Flags]
    public enum PlayerActions
    {
        None = 0,
        Jumped = 1 << 0,
        WallJumped = 1 << 1,
        Dashed = 1 << 2,
    }

    /// <summary>
    /// 自機の制御。1 Tick の順序は
    /// 「接地の確認 → 押し始めの検出（ジャンプはバッファへ）→ 接地猶予 → ダッシュ開始 →
    /// ジャンプの解決（地上・猶予中 &gt; 壁蹴り）→ 横の加減速 → 可変ジャンプ → 重力（壁ずり）→ 横移動 → 縦移動 →
    /// ダッシュの終了 → 接地の確認とダッシュ回数の回復」。
    /// </summary>
    public sealed class Player
    {
        public const int Width = 10 * Units.Pixel;
        public const int Height = 14 * Units.Pixel;

        private readonly PrecisionConfig _config;
        private bool _jumpWasHeld;
        private bool _dashWasHeld;
        private bool _rising;
        private Countdown _coyote;
        private Countdown _buffer;
        private Countdown _wallLock;
        private int _dashLeft;
        private int _dashX;
        private int _dashY;

        public Player(PrecisionConfig config, Box box)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (box.Width != Width || box.Height != Height)
            {
                throw new ArgumentException($"自機の大きさは {Width}×{Height} です。", nameof(box));
            }

            Body = box;
            Facing = 1;
            DashesLeft = _config.DashCount;

            // 死亡前・前の部屋から押したままのキーで、開始直後に跳んだりダッシュしたりしない（一度離すまで受け付けない）。
            _jumpWasHeld = true;
            _dashWasHeld = true;
        }

        public Box Body { get; private set; }

        public int VelocityX { get; private set; }

        public int VelocityY { get; private set; }

        /// <summary>直前の Tick の終わりに接地していたか。</summary>
        public bool Grounded { get; private set; }

        /// <summary>向き（右 1・左 -1）。</summary>
        public int Facing { get; private set; }

        /// <summary>残りのダッシュ回数。</summary>
        public int DashesLeft { get; private set; }

        public bool IsDashing => _dashLeft > 0;

        /// <summary>直前の Tick に壁ずりで落下を抑えていたか。</summary>
        public bool WallSliding { get; private set; }

        /// <summary>接地猶予の残り Tick 数。</summary>
        public int CoyoteRemaining => _coyote.Remaining;

        /// <summary>入力バッファの残り Tick 数。</summary>
        public int BufferRemaining => _buffer.Remaining;

        /// <summary>同じ状態の複製。</summary>
        public Player Clone() => (Player)MemberwiseClone();

        /// <summary>
        /// 以後の動きを決める状態をすべて混ぜる（位置・速度・向き・回数に加え、猶予・バッファ・固定・ダッシュの残り Tick、
        /// 直前のキーの押下）。ハッシュが一致すれば、同じ入力に対して以後も同じに動く。
        /// </summary>
        public void MixInto(ref StateHash hash)
        {
            hash.Mix(Body.X);
            hash.Mix(Body.Y);
            hash.Mix(VelocityX);
            hash.Mix(VelocityY);
            hash.Mix(Grounded);
            hash.Mix(Facing);
            hash.Mix(DashesLeft);
            hash.Mix(WallSliding);
            hash.Mix(_jumpWasHeld);
            hash.Mix(_dashWasHeld);
            hash.Mix(_rising);
            hash.Mix(_coyote.Remaining);
            hash.Mix(_buffer.Remaining);
            hash.Mix(_wallLock.Remaining);
            hash.Mix(_dashLeft);
            hash.Mix(_dashX);
            hash.Mix(_dashY);
        }

        public uint Hash()
        {
            var hash = StateHash.Start();
            MixInto(ref hash);
            return hash.Value;
        }

        /// <summary>1 Tick 進める。</summary>
        public PlayerActions Step(TileMap map, PlayerInput input)
        {
            if (map == null) { throw new ArgumentNullException(nameof(map)); }
            var actions = PlayerActions.None;
            Grounded = VelocityY <= 0 && GroundProbe.IsGrounded(map, Body);

            var jumpPressed = input.Jump && !_jumpWasHeld;
            var dashPressed = input.Dash && !_dashWasHeld;
            _jumpWasHeld = input.Jump;
            _dashWasHeld = input.Dash;
            if (jumpPressed)
            {
                // 押した Tick と、その後の JumpBufferTicks Tick の間、ジャンプできる状態を待つ。
                _buffer.Arm(_config.JumpBufferTicks + 1);
            }

            // 接地中は猶予を張り直し、空中では 1 Tick ずつ減らす。空中の k Tick 目は k <= CoyoteTicks なら地上のジャンプができる。
            var canGroundJump = Grounded || _coyote.IsActive;
            if (Grounded)
            {
                _coyote.Arm(_config.CoyoteTicks);
            }
            else
            {
                _coyote.Tick();
            }

            if (dashPressed && DashesLeft > 0 && !IsDashing)
            {
                StartDash(input);
                actions |= PlayerActions.Dashed;
            }

            if (IsDashing)
            {
                // ダッシュ中は一定の速度で、重力も横入力も効かない。ジャンプの押下はバッファの中で待つ。
                var speed = _dashX != 0 && _dashY != 0 ? PrecisionConfig.DiagonalOf(_config.DashSpeed) : _config.DashSpeed;
                VelocityX = _dashX * speed;
                VelocityY = _dashY * speed;
                _buffer.Tick();
                WallSliding = false;
            }
            else
            {
                actions |= ResolveJump(map, canGroundJump);
                _buffer.Tick();
                Steer(input.Horizontal);
                ApplyGravity(map, input);
            }

            var body = Body;
            if (BodyMotion.MoveX(map, ref body, VelocityX))
            {
                VelocityX = 0;
            }

            if (BodyMotion.MoveY(map, ref body, VelocityY))
            {
                VelocityY = 0;
                _rising = false;
            }

            Body = body;
            if (IsDashing && --_dashLeft == 0)
            {
                EndDash();
            }

            Grounded = VelocityY <= 0 && GroundProbe.IsGrounded(map, Body);
            if (Grounded && !IsDashing)
            {
                DashesLeft = _config.DashCount;
            }

            return actions;
        }

        private void StartDash(PlayerInput input)
        {
            var dx = input.Horizontal;
            var dy = input.Vertical;
            if (dx == 0 && dy == 0)
            {
                dx = Facing;
            }

            if (dx != 0)
            {
                Facing = dx;
            }

            DashesLeft--;
            _dashLeft = _config.DashTicks;
            _dashX = dx;
            _dashY = dy;
            _rising = false;
            _coyote.Clear();
            _wallLock.Clear();
        }

        private void EndDash()
        {
            var end = _dashX != 0 && _dashY != 0 ? PrecisionConfig.DiagonalOf(_config.DashEndSpeed) : _config.DashEndSpeed;
            VelocityX = Math.Clamp(VelocityX, -end, end);
            VelocityY = Math.Clamp(VelocityY, -end, end);
        }

        private PlayerActions ResolveJump(TileMap map, bool canGroundJump)
        {
            if (!_buffer.IsActive)
            {
                return PlayerActions.None;
            }

            if (canGroundJump)
            {
                VelocityY = _config.JumpSpeed;
                Consume();
                return PlayerActions.Jumped;
            }

            var side = WallProbe.KickSide(map, Body, Facing, _config.WallReach);
            if (side == 0)
            {
                return PlayerActions.None;
            }

            VelocityX = -side * _config.WallJumpSpeedX;
            VelocityY = _config.WallJumpSpeedY;
            Facing = -side;
            _wallLock.Arm(_config.WallJumpLockTicks);
            Consume();
            return PlayerActions.WallJumped;
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
            if (_wallLock.IsActive)
            {
                // 壁蹴りの直後は横の速さを保ち、入力で壁へ戻れないようにする。
                _wallLock.Tick();
                return;
            }

            if (direction != 0)
            {
                Facing = direction;
            }

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
                // ダッシュや壁蹴りで最高速を超えていれば、少しずつ落とす。
                VelocityX = Toward(vx, direction * max, decel);
            }
            else
            {
                VelocityX = Toward(vx, direction * max, accel);
            }
        }

        private void ApplyGravity(TileMap map, PlayerInput input)
        {
            if (_rising && !input.Jump && VelocityY > _config.JumpCutSpeed)
            {
                VelocityY = _config.JumpCutSpeed;
            }

            WallSliding = !Grounded && VelocityY <= 0 && input.Horizontal != 0 && WallProbe.Near(map, Body, input.Horizontal, 1);
            var maxFall = WallSliding ? _config.WallSlideMaxFall : _config.MaxFallSpeed;
            VelocityY = Math.Max(VelocityY - _config.Gravity, -maxFall);
            if (VelocityY <= 0)
            {
                _rising = false;
            }
        }

        private static int Toward(int value, int target, int step)
        {
            return value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);
        }
    }
}
