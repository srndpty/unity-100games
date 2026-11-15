using System;

namespace Game.Core
{
    /// <summary>左右にだけ動くパドル。ボールは上面でだけ跳ね返る。</summary>
    public sealed class Paddle
    {
        private readonly BreakoutConfig _config;

        public Paddle(BreakoutConfig config)
        {
            _config = config;
            HalfWidth = config.PaddleWidth / 2f;
        }

        public float X => Motion.To;

        public float HalfWidth { get; private set; }

        public float Top => _config.PaddleTop;

        /// <summary>直前の Tick の軌跡。ボールとの衝突判定と描画の補間に使う。</summary>
        public PaddleMotion Motion { get; private set; }

        private float Limit => (_config.FieldWidth / 2f) - HalfWidth;

        /// <summary>入力（-1 = 左、+1 = 右）に従って 1 Tick 分動かす。範囲外や非数の入力は丸める。</summary>
        public void Move(float input)
        {
            Motion = PaddleMotion.Clamped(X, SanitizeInput(input) * _config.PaddleSpeed * _config.TickDuration, Limit);
        }

        /// <summary>幅を変える。広げて壁にめり込むなら内側へ押し戻す（Tick 中の移動として扱わない）。</summary>
        public void SetExpanded(bool expanded)
        {
            HalfWidth = _config.PaddleWidth * (expanded ? _config.ExpandScale : 1f) / 2f;
            Motion = PaddleMotion.At(Math.Clamp(X, -Limit, Limit));
        }

        /// <summary>中央へ瞬間移動させる。</summary>
        public void Center() => Motion = PaddleMotion.At(0f);

        /// <summary>Tick 中の時刻 <paramref name="fraction"/>（0 = 開始、1 = 終了）での x。</summary>
        public float XAt(float fraction) => Motion.XAt(fraction);

        public static float SanitizeInput(float input)
        {
            if (!float.IsFinite(input))
            {
                return 0f;
            }

            return Math.Clamp(input, -1f, 1f);
        }
    }
}
