using System;

namespace Game.Core
{
    /// <summary>1 Tick 分の入力。押している状態で、ジャンプの押し始めは自機が前の Tick との差で見分ける。</summary>
    public readonly struct PlayerInput : IEquatable<PlayerInput>
    {
        public PlayerInput(int horizontal, bool run, bool jump)
        {
            if (horizontal < -1 || horizontal > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(horizontal), horizontal, "-1・0・1 のいずれかにしてください。");
            }

            Horizontal = horizontal;
            Run = run;
            Jump = jump;
        }

        public static PlayerInput None => default;

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int Horizontal { get; }

        public bool Run { get; }

        public bool Jump { get; }

        public bool Equals(PlayerInput other) => Horizontal == other.Horizontal && Run == other.Run && Jump == other.Jump;

        public override bool Equals(object? obj) => obj is PlayerInput other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Horizontal, Run, Jump);

        public static bool operator ==(PlayerInput left, PlayerInput right) => left.Equals(right);

        public static bool operator !=(PlayerInput left, PlayerInput right) => !left.Equals(right);
    }

    /// <summary>
    /// 自機の速度ベースの制御。1 Tick の順序は「横の加減速 → ジャンプ（押し始め かつ 接地中）→ 可変ジャンプ → 重力 → 横移動 → 縦移動 → 接地検出」。
    /// </summary>
    public sealed class Player
    {
        public const int Width = 12 * Units.Pixel;
        public const int Height = 14 * Units.Pixel;

        private readonly PlatformerConfig _config;
        private bool _jumpWasHeld;
        private bool _rising;

        public Player(PlatformerConfig config, Box box)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (box.Width != Width || box.Height != Height)
            {
                throw new ArgumentException($"自機の大きさは {Width}×{Height} です。", nameof(box));
            }

            Body = box;
            Facing = 1;

            // 前の面から押したままのジャンプキーで、開始直後に跳ばないようにする（一度離すまで跳ばない）。
            _jumpWasHeld = true;
        }

        public Box Body { get; private set; }

        /// <summary>直前の Tick にジャンプキーを押していたか（押し始めの判定用）。</summary>
        public bool JumpWasHeld => _jumpWasHeld;

        public int VelocityX { get; private set; }

        public int VelocityY { get; private set; }

        /// <summary>直前の Tick の終わりに接地していたか。</summary>
        public bool Grounded { get; private set; }

        /// <summary>向き（右 1・左 -1）。</summary>
        public int Facing { get; private set; }

        /// <summary>同じ状態の複製（先読み用）。</summary>
        public Player Clone() => (Player)MemberwiseClone();

        /// <summary>1 Tick 進める。跳んだら true。</summary>
        public bool Step(TileMap map, PlayerInput input)
        {
            Grounded = VelocityY <= 0 && GroundProbe.IsGrounded(map, Body);
            if (input.Horizontal != 0)
            {
                Facing = input.Horizontal;
            }

            VelocityX = Horizontal(input);

            var jumped = false;
            var pressed = input.Jump && !_jumpWasHeld;
            _jumpWasHeld = input.Jump;
            if (pressed && Grounded)
            {
                VelocityY = _config.JumpSpeed;
                _rising = true;
                jumped = true;
            }

            if (_rising && !input.Jump && VelocityY > _config.JumpCutSpeed)
            {
                VelocityY = _config.JumpCutSpeed;
            }

            VelocityY = Math.Max(VelocityY - _config.Gravity, -_config.MaxFallSpeed);
            if (VelocityY <= 0)
            {
                _rising = false;
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
            Grounded = VelocityY <= 0 && GroundProbe.IsGrounded(map, Body);
            return jumped;
        }

        /// <summary>敵を踏んだ跳ね返り。空中でのジャンプにはならない（押し始めの扱いは変えない）。</summary>
        public void Bounce(bool jumpHeld)
        {
            VelocityY = jumpHeld ? _config.JumpSpeed : _config.StompBounce;
            _rising = true;
            Grounded = false;
        }

        private int Horizontal(PlayerInput input)
        {
            var vx = VelocityX;
            var max = input.Run ? _config.RunSpeed : _config.WalkSpeed;
            var dir = input.Horizontal;
            if (dir == 0)
            {
                return Grounded ? Toward(vx, 0, _config.Friction) : vx;
            }

            if (vx * dir < 0)
            {
                // 反対向きの入力：接地中は強めに減速し、0 を越えたら新しい向きへ加速し始める。
                var decel = Grounded ? _config.SkidDecel : _config.AirAccel;
                return Toward(vx, dir * max, decel);
            }

            if (Math.Abs(vx) > max)
            {
                // 走りをやめたときなど、最高速を超えていれば少しずつ落とす（空中では保つ）。
                return Grounded ? Toward(vx, dir * max, _config.Friction) : vx;
            }

            var accel = Grounded ? _config.GroundAccel : _config.AirAccel;
            return Toward(vx, dir * max, accel);
        }

        private static int Toward(int value, int target, int step)
        {
            return value < target ? Math.Min(value + step, target) : Math.Max(value - step, target);
        }
    }
}
