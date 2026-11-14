using System;

namespace Game.Core
{
    [Serializable]
    public sealed class CpuConfig
    {
        /// <summary>CPU パドルの最高速度。プレイヤー（PongConfig.PaddleSpeed）より遅くして勝てるようにする。</summary>
        public float MaxSpeed = 5f;

        /// <summary>目標との差がこれ以下なら動かない。</summary>
        public float DeadZone = 0.1f;

        /// <summary>狙う位置のぶれ（パドル半分の長さに対する割合）。1 を超えると空振りしうる。</summary>
        public float AimError = 1.3f;

        public void Validate()
        {
            if (MaxSpeed <= 0f || DeadZone < 0f || AimError < 0f)
            {
                throw new ArgumentException("CpuConfig が不正です: 速度は正、不感帯とぶれは 0 以上");
            }
        }
    }

    /// <summary>
    /// CPU の操作。向かってくるボールの高さを追い、離れていく間は中央へ戻る。
    /// 狙いのぶれは専用の乱数で決め、試合（サーブ）の乱数とは混ぜない。
    /// </summary>
    public sealed class CpuController
    {
        private readonly CpuConfig _config;
        private readonly Side _side;
        private readonly SeededRandom _random;
        private bool _wasApproaching;
        private float _aimOffset;

        public CpuController(CpuConfig config, Side side, int seed)
        {
            config.Validate();
            _config = config;
            _side = side;
            _random = new SeededRandom(seed);
        }

        /// <summary>現在の試合状態から、このパドルへの入力（-1〜+1）を決める。</summary>
        public float Decide(MatchState match)
        {
            if (match.Phase == MatchPhase.Finished)
            {
                return 0f;
            }

            var paddle = match.PaddleOf(_side);
            var ball = match.Ball;
            var approaching = ball.InPlay && ball.Velocity.X * _side.Direction() > 0f;
            if (approaching && !_wasApproaching)
            {
                _aimOffset = _random.Range(-1f, 1f) * _config.AimError * (paddle.HalfHeight + ball.HalfSize);
            }

            _wasApproaching = approaching;

            var target = approaching ? ball.Position.Y - _aimOffset : 0f;
            var difference = target - paddle.Y;
            if (MathF.Abs(difference) <= _config.DeadZone)
            {
                return 0f;
            }

            // 1 Tick でちょうど目標に届く入力を、CPU の最高速度で頭打ちにする。
            var config = match.Config;
            var maxInput = MathF.Min(1f, _config.MaxSpeed / config.PaddleSpeed);
            var exactInput = difference / (config.PaddleSpeed * config.TickDuration);
            return Math.Clamp(exactInput, -maxInput, maxInput);
        }
    }
}
