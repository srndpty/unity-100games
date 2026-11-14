using System;

namespace Game.Core
{
    /// <summary>上下にだけ動くパドル。</summary>
    public sealed class Paddle
    {
        private readonly float _speed;
        private readonly float _limit;

        public Paddle(PongConfig config, Side side)
        {
            Side = side;
            X = config.PaddleX(side);
            HalfWidth = config.PaddleWidth / 2f;
            HalfHeight = config.PaddleHeight / 2f;
            _speed = config.PaddleSpeed;
            _limit = (config.FieldHeight / 2f) - HalfHeight;
        }

        public Side Side { get; }

        public float X { get; }

        public float Y { get; private set; }

        public float HalfWidth { get; }

        public float HalfHeight { get; }

        /// <summary>ボールが当たる内側の面の x 座標。</summary>
        public float FaceX => X - (Side.Direction() * HalfWidth);

        /// <summary>入力（-1 = 下、+1 = 上）に従って 1 Tick 分動かす。範囲外や非数の入力は丸める。</summary>
        public void Move(float input, float deltaTime)
        {
            SetY(Y + (SanitizeInput(input) * _speed * deltaTime));
        }

        public void SetY(float y)
        {
            Y = Math.Clamp(y, -_limit, _limit);
        }

        public static float SanitizeInput(float input)
        {
            if (float.IsNaN(input) || float.IsInfinity(input))
            {
                return 0f;
            }

            return Math.Clamp(input, -1f, 1f);
        }
    }
}
