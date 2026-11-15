using System;

namespace Game.Core
{
    /// <summary>1 面ぶんのブロック配置。1 行 = 1 文字列、1 文字 = 1 マス（記号は <see cref="StageParser"/>）。</summary>
    [Serializable]
    public sealed class StageDefinition
    {
        public string[] Rows = Array.Empty<string>();

        public StageDefinition()
        {
        }

        public StageDefinition(params string[] rows)
        {
            Rows = rows;
        }
    }

    /// <summary>
    /// ゲームのパラメータ。単位はワールド座標（フィールド中央が原点、上が +y）と秒。
    /// Unity の設定アセットからそのままシリアライズできるよう、公開フィールドにしている。
    /// </summary>
    [Serializable]
    public sealed class BreakoutConfig
    {
        public float FieldWidth = 12f;
        public float FieldHeight = 16f;

        public float BrickWidth = 1f;
        public float BrickHeight = 0.5f;

        /// <summary>上の壁からブロックの最上段までの距離。</summary>
        public float GridTopMargin = 1.5f;

        public float PaddleWidth = 2f;
        public float PaddleHeight = 0.3f;

        /// <summary>下端（ミスの線）からパドル中心までの距離。</summary>
        public float PaddleInset = 1.2f;

        public float PaddleSpeed = 12f;
        public float BallSize = 0.25f;

        /// <summary>1 面目の発射速度。</summary>
        public float BallSpeed = 7f;

        /// <summary>面が進むごとに発射速度に足す量。</summary>
        public float StageSpeedStep = 0.75f;

        public float SpeedIncreasePerBrick = 0.05f;
        public float MaxBallSpeed = 11f;

        /// <summary>パドルの端で打ち返したときの反射角（垂直からの角度）。</summary>
        public float MaxBounceAngleDegrees = 60f;

        /// <summary>発射角の範囲（垂直から ±）。</summary>
        public float MaxServeAngleDegrees = 30f;

        public int Lives = 3;
        public int TicksPerSecond = 60;

        /// <summary>面クリアから次の面が始まるまでの待ち時間。</summary>
        public int StageClearDelayTicks = 90;

        public int PierceDurationTicks = 480;
        public int ExpandDurationTicks = 600;
        public float ExpandScale = 1.5f;

        public float CapsuleWidth = 0.8f;
        public float CapsuleHeight = 0.3f;
        public float CapsuleSpeed = 3f;

        public int NormalScore = 10;
        public int HardScore = 20;
        public int ItemScore = 30;

        public StageDefinition[] Stages =
        {
            new(
                "NNNNNNNNNNNN",
                "NNNNNNNNNNNN",
                "NNNENNNNPNNN",
                "NNNNNNNNNNNN"),
            new(
                "HHHHHHHHHHHH",
                "N.NN.NN.NN.N",
                "NNNPNNNNENNN",
                "HHHHHHHHHHHH",
                "NNNNNNNNNNNN"),
            new(
                "HHHHHHHHHHHH",
                "H.NNNNNNNN.H",
                "H.NPNNNNEN.H",
                "H.NNHHHHNN.H",
                "H.NNNNNNNN.H",
                "HHHHH..HHHHH"),
        };

        public float TickDuration => 1f / TicksPerSecond;

        /// <summary>横に並ぶマスの数。フィールドの幅をブロックの幅で割り切れること（<see cref="Validate"/>）。</summary>
        public int Columns => (int)MathF.Round(FieldWidth / BrickWidth);

        /// <summary>パドル中心の y。</summary>
        public float PaddleY => (-FieldHeight / 2f) + PaddleInset;

        /// <summary>パドル上面の y。ボールはここでだけ跳ね返る。</summary>
        public float PaddleTop => PaddleY + (PaddleHeight / 2f);

        /// <summary>最上段のブロックの上端の y。</summary>
        public float GridTop => (FieldHeight / 2f) - GridTopMargin;

        /// <summary>
        /// 1 Tick にボールが動いてよい距離の上限（これ未満であること）。向かい合う 2 つの面の間をボールが渡るのに要する最短距離で、
        /// ブロックの短辺からボールの大きさを引いたもの。
        /// <para>
        /// これ未満なら、1 Tick に跳ね返るのは x・y 各 1 回まで（角は 1 回で両方）。同じ軸で 2 回跳ね返るには、
        /// 片方の向きの面から反対向きの面まで渡る必要があるため。ブロックはマス目に揃い、左右の壁はマス目の端、
        /// 上の壁・パドルはブロックから 1 段以上離れている（<see cref="Validate"/>）ので、向かい合う面の間隔は 1 マス以上ある。
        /// </para>
        /// </summary>
        public float MaxTravelPerTick => MathF.Min(BrickWidth, BrickHeight) - BallSize;

        public void Validate()
        {
            Require(
                AllFinite(
                    FieldWidth, FieldHeight, BrickWidth, BrickHeight, GridTopMargin, PaddleWidth, PaddleHeight, PaddleInset, PaddleSpeed,
                    BallSize, BallSpeed, StageSpeedStep, SpeedIncreasePerBrick, MaxBallSpeed, MaxBounceAngleDegrees, MaxServeAngleDegrees,
                    ExpandScale, CapsuleWidth, CapsuleHeight, CapsuleSpeed),
                "実数値はすべて有限");
            Require(FieldWidth > 0f && FieldHeight > 0f, "フィールドの大きさは正の値");
            Require(BrickWidth > 0f && BrickHeight > 0f, "ブロックの大きさは正の値");
            Require(MathF.Abs((Columns * BrickWidth) - FieldWidth) < 1e-4f, "フィールドの幅はブロックの幅で割り切れる");
            Require(BallSize > 0f && BallSize < MathF.Min(BrickWidth, BrickHeight), "ボールはブロックより小さい正の大きさ");
            Require(GridTopMargin >= BrickHeight, "上の壁とブロックの間は 1 段以上空ける");
            Require(PaddleWidth > 0f && PaddleHeight > 0f, "パドルは正の大きさ");
            Require(ExpandScale >= 1f && PaddleWidth * ExpandScale < FieldWidth, "拡張したパドルはフィールドに収まる");
            Require(PaddleInset > (PaddleHeight / 2f) + BallSize, "パドルの下をボールが抜けられる");
            Require(PaddleSpeed > 0f && BallSpeed > 0f && StageSpeedStep >= 0f && SpeedIncreasePerBrick >= 0f, "速度は正の値");
            Require(MaxBounceAngleDegrees > 0f && MaxBounceAngleDegrees < 90f, "反射角は 0〜90 度");
            Require(MaxServeAngleDegrees >= 0f && MaxServeAngleDegrees < 90f, "発射角は 0〜90 度");
            Require(Lives >= 1, "残機は 1 以上");
            Require(TicksPerSecond >= 1, "Tick レートは 1 以上");
            Require(StageClearDelayTicks >= 1, "面クリアの待ちは 1 Tick 以上");
            Require(PierceDurationTicks >= 1 && ExpandDurationTicks >= 1, "効果の時間は 1 Tick 以上");
            Require(CapsuleWidth > 0f && CapsuleHeight > 0f && CapsuleSpeed > 0f, "カプセルは正の大きさと速度");
            Require(CapsuleSpeed * TickDuration < PaddleHeight, "カプセルの 1 Tick の落下量はパドルの厚みより小さい（すり抜けない）");
            Require(NormalScore >= 0 && HardScore >= 0 && ItemScore >= 0, "得点は 0 以上");
            Require(Stages is { Length: >= 1 }, "面は 1 つ以上");

            var maxRows = 0;
            var maxScore = 0L;
            for (var i = 0; i < Stages.Length; i++)
            {
                var layout = StageParser.Parse(Stages[i], Columns, i);
                maxRows = Math.Max(maxRows, layout.Rows);
                foreach (var brick in layout.Bricks)
                {
                    maxScore += ScoreOf(brick.Kind);
                }
            }

            Require(GridTop - (maxRows * BrickHeight) - PaddleTop >= BrickHeight, "最下段のブロックとパドルの間は 1 段以上空ける");
            Require(maxScore <= int.MaxValue, "全ブロックを壊したときの合計得点が int の範囲に収まる");
            Require(MaxBallSpeed >= BallSpeed + (StageSpeedStep * (Stages.Length - 1)), "最高速度は各面の発射速度以上");
            Require(MaxBallSpeed * TickDuration < MaxTravelPerTick, "最高速度での 1 Tick の移動量は、ブロックの短辺からボールの大きさを引いた長さより短い");
        }

        /// <summary><paramref name="stageIndex"/> 面目（0 始まり）の発射速度。</summary>
        public float LaunchSpeed(int stageIndex) => BallSpeed + (StageSpeedStep * stageIndex);

        public int ScoreOf(BrickKind kind) => kind switch
        {
            BrickKind.Normal => NormalScore,
            BrickKind.Hard => HardScore,
            _ => ItemScore,
        };

        public int DurationTicks(EffectKind effect) => effect == EffectKind.Pierce ? PierceDurationTicks : ExpandDurationTicks;

        private static bool AllFinite(params float[] values) => Array.TrueForAll(values, float.IsFinite);

        private static void Require(bool condition, string rule)
        {
            if (!condition)
            {
                throw new ArgumentException($"BreakoutConfig が不正です: {rule}");
            }
        }
    }
}
