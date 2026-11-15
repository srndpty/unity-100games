using System;

namespace Game.Core
{
    /// <summary>
    /// 試合のパラメータ。単位はワールド座標（コート中央が原点）と秒。
    /// Unity の設定アセットからそのままシリアライズできるよう、公開フィールドにしている。
    /// </summary>
    [Serializable]
    public sealed class PongConfig
    {
        public float FieldWidth = 16f;
        public float FieldHeight = 9f;
        public float PaddleWidth = 0.25f;
        public float PaddleHeight = 1.6f;

        /// <summary>ゴールラインからパドル中心までの距離。</summary>
        public float PaddleInset = 0.6f;

        public float PaddleSpeed = 9f;
        public float BallSize = 0.25f;
        public float ServeSpeed = 7f;
        public float SpeedIncreasePerHit = 0.5f;
        public float MaxBallSpeed = 16f;

        /// <summary>パドル端で打ち返したときの反射角（水平からの角度）。</summary>
        public float MaxBounceAngleDegrees = 55f;

        public float MaxServeAngleDegrees = 30f;
        public int WinScore = 5;
        public int TicksPerSecond = 60;

        /// <summary>得点・試合開始からサーブまでの待ち時間。</summary>
        public int ServeDelayTicks = 60;

        public float TickDuration => 1f / TicksPerSecond;

        public float PaddleX(Side side) => side.Direction() * ((FieldWidth / 2f) - PaddleInset);

        public void Validate()
        {
            Require(
                AllFinite(FieldWidth, FieldHeight, PaddleWidth, PaddleHeight, PaddleInset, PaddleSpeed, BallSize, ServeSpeed, SpeedIncreasePerHit, MaxBallSpeed, MaxBounceAngleDegrees, MaxServeAngleDegrees),
                "実数値はすべて有限");
            Require(FieldWidth > 0f && FieldHeight > 0f, "コートの大きさは正の値");
            Require(PaddleWidth > 0f && PaddleHeight > 0f && PaddleHeight < FieldHeight, "パドルはコートに収まる正の大きさ");
            Require(PaddleInset > (PaddleWidth / 2f) + BallSize && PaddleInset < FieldWidth / 4f, "パドルはゴールラインより内側で、ボールが裏へ抜けられる位置");
            Require(BallSize > 0f && BallSize < PaddleHeight, "ボールはパドルより小さい正の大きさ");
            Require(PaddleSpeed > 0f && ServeSpeed > 0f && SpeedIncreasePerHit >= 0f, "速度は正の値");
            Require(MaxBallSpeed >= ServeSpeed, "最高速度はサーブ速度以上");
            Require(MaxBounceAngleDegrees > 0f && MaxBounceAngleDegrees < 90f, "反射角は 0〜90 度");
            Require(MaxServeAngleDegrees >= 0f && MaxServeAngleDegrees < 90f, "サーブ角は 0〜90 度");
            Require(WinScore >= 1, "勝利点は 1 以上");
            Require(TicksPerSecond >= 1, "Tick レートは 1 以上");
            Require(ServeDelayTicks >= 1, "サーブ待ちは 1 Tick 以上");

            Require(MaxBallSpeed * TickDuration < MaxTravelPerTick, "最高速度での 1 Tick の移動量は、上下の壁の間隔と左右のパドル面の間隔より短い");
        }

        /// <summary>
        /// 1 Tick にボールが動いてよい距離の上限（これ未満であること）。上下の壁の接触面の間隔と、左右のパドルの接触面の間隔の短い方。
        /// これ未満なら 1 Tick の接触は最大 3 回（壁 → パドル → 同じ壁）に収まる。
        /// 理由：横向きを変えるのはパドルだけなので、同じパドルに続けて当たるには反対のパドルを経由する（横に間隔ぶん動く）。
        /// 縦向きは壁とパドルが変えるので、パドルを挟まずに壁へ 2 回当たるには反対の壁を経由する（縦に間隔ぶん動く）。
        /// 4 回の接触には、このどちらかが必ず含まれる。
        /// </summary>
        public float MaxTravelPerTick
        {
            get
            {
                var wallGap = FieldHeight - BallSize;
                var paddleGap = 2f * (MathF.Abs(PaddleX(Side.Right)) - (PaddleWidth / 2f) - (BallSize / 2f));
                return MathF.Min(wallGap, paddleGap);
            }
        }

        private static bool AllFinite(params float[] values) => Array.TrueForAll(values, float.IsFinite);

        private static void Require(bool condition, string rule)
        {
            if (!condition)
            {
                throw new ArgumentException($"PongConfig が不正です: {rule}");
            }
        }
    }
}
