using System;

namespace Game.Core
{
    /// <summary>上下にだけ動くパドル。</summary>
    public sealed class Paddle
    {
        private readonly float _speed;
        private readonly float _limit;

        // この Tick で入力が求めた移動量（端で止まる前の値）。
        private float _requestedDelta;

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

        /// <summary>直前の <see cref="Move"/> を始めた時点（Tick 開始時）の y。ボールとの衝突判定で、Tick 中の位置を補間するのに使う。</summary>
        public float PreviousY { get; private set; }

        public float HalfWidth { get; }

        public float HalfHeight { get; }

        /// <summary>ボールが当たる内側の面の x 座標。</summary>
        public float FaceX => X - (Side.Direction() * HalfWidth);

        /// <summary>入力（-1 = 下、+1 = 上）に従って 1 Tick 分動かす。範囲外や非数の入力は丸める。</summary>
        public void Move(float input, float deltaTime)
        {
            PreviousY = Y;
            _requestedDelta = SanitizeInput(input) * _speed * deltaTime;
            Y = YAt(1f);
        }

        /// <summary>瞬間移動させる（Tick 中の移動として扱わない）。</summary>
        public void SetY(float y)
        {
            Y = Clamp(y);
            PreviousY = Y;
            _requestedDelta = 0f;
        }

        /// <summary>
        /// Tick 中の時刻 <paramref name="fraction"/>（0 = 開始、1 = 終了）での y。Tick 内は等速で動き、
        /// 途中でコートの端に着いたらそこで止まったとみなす（端で止まる Tick も、止まるまでは等速）。
        /// </summary>
        public float YAt(float fraction) => Clamp(PreviousY + (_requestedDelta * fraction));

        private float Clamp(float y) => Math.Clamp(y, -_limit, _limit);

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
