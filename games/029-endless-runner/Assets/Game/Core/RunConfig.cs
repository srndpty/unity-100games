using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 試合の設定。長さは 1 m を <see cref="Units.Tile"/>（256）等分した整数、時間は Tick 数（60 Tick/秒）で持つ
    /// （浮動小数点を使わないので Mono と .NET で同じ結果になる）。距離の設定は m 単位の整数。
    /// </summary>
    [Serializable]
    public sealed class RunConfig
    {
        /// <summary>レーンの数（固定）。番号は左から 0, 1, 2。</summary>
        public const int Lanes = 3;

        /// <summary>長さの設定の上限（16 m）。掃引の分数の計算が long で桁あふれしない範囲に抑える。</summary>
        public const int MaxLength = 16 * Units.Tile;

        /// <summary>1 Tick の前進量の上限（1 m）。</summary>
        public const int MaxSpeedLimit = Units.Tile;

        public const int MaxStatusTicks = 600;

        public const int MaxMeters = 100_000;

        /// <summary>生きているチャンクの数の上限の最大。描画のプールもこの数から決める。</summary>
        public const int MaxActiveChunksLimit = 64;

        /// <summary>レーンの中心どうしの間隔。障害物の幅もこれ（隣の障害物と隙間なく並ぶ）。</summary>
        public int LaneWidth = 640;

        /// <summary>隣のレーンへ移る Tick 数。移動中の横位置は線形。</summary>
        public int LaneChangeTicks = 8;

        public int RunnerWidth = 192;

        public int RunnerDepth = 128;

        public int StandHeight = 448;

        /// <summary>スライド中の当たり箱の高さ。</summary>
        public int SlideHeight = 160;

        /// <summary>跳んだ Tick の上向きの速さ（単位/Tick）。</summary>
        public int JumpVelocity = 32;

        /// <summary>1 Tick ごとに縦の速さから引く値。</summary>
        public int Gravity = 2;

        /// <summary>スライドが続く Tick 数（押した Tick を含む）。</summary>
        public int SlideTicks = 36;

        /// <summary>今は効かない入力（レーン移動中の次の移動・空中のジャンプなど）を覚えておく Tick 数（押した Tick を含む）。</summary>
        public int InputBufferTicks = 10;

        /// <summary>チャンクの 1 行の奥行き。</summary>
        public int RowLength = 512;

        /// <summary>障害物の奥行き（行の中央に置く）。壁も薄い板にして、通り過ぎた後の横移動でぶつからないようにする。</summary>
        public int ObstacleDepth = 128;

        public int HurdleHeight = 160;

        /// <summary>Beam の下端の高さ。上端は <see cref="WallHeight"/>。</summary>
        public int BeamBottom = 256;

        public int WallHeight = 768;

        /// <summary>走り出しの速さ（単位/Tick）。</summary>
        public int BaseSpeed = 40;

        /// <summary>速さの段階が 1 上がるごとに足す値。</summary>
        public int SpeedStep = 8;

        public int MaxSpeedLevel = 5;

        /// <summary>速さの段階が 1 上がる距離（m）。</summary>
        public int SpeedLevelMeters = 200;

        public int MaxTier = 3;

        /// <summary>解禁される難度が 1 上がる距離（m）。</summary>
        public int TierMeters = 300;

        /// <summary>最初のチャンクの前の、障害物のない助走の長さ（m）。</summary>
        public int RunwayMeters = 30;

        /// <summary>自機の前方のこの距離（m）までチャンクを並べる。</summary>
        public int LookAheadMeters = 120;

        /// <summary>自機の後方のこの距離（m）を過ぎたチャンクを回収する。</summary>
        public int BehindMeters = 16;

        /// <summary>生きているチャンクの数の上限（固定容量のリング）。</summary>
        public int MaxActiveChunks = 24;

        /// <summary>最も速い段階の速さ。</summary>
        public int TopSpeed => BaseSpeed + (SpeedStep * MaxSpeedLevel);

        public RunConfig Copy() => (RunConfig)MemberwiseClone();

        /// <summary>レーンの中心の横位置（中央のレーンが 0）。</summary>
        public int LaneX(int lane) => (lane - 1) * LaneWidth;

        /// <summary>値の範囲と組み合わせを検査する。チャンクの到達性は <see cref="RunRules.Build"/> が検査する。</summary>
        public void Validate()
        {
            Require(LaneWidth, 2, MaxLength, nameof(LaneWidth));
            Require(LaneChangeTicks, 1, MaxStatusTicks, nameof(LaneChangeTicks));
            Require(RunnerWidth, 2, LaneWidth - 1, nameof(RunnerWidth));
            Require(RunnerDepth, 2, MaxLength, nameof(RunnerDepth));
            Require(StandHeight, 1, MaxLength, nameof(StandHeight));
            Require(SlideHeight, 1, StandHeight, nameof(SlideHeight));
            Require(JumpVelocity, 1, MaxLength, nameof(JumpVelocity));
            Require(Gravity, 1, JumpVelocity, nameof(Gravity));
            Require(SlideTicks, 1, MaxStatusTicks, nameof(SlideTicks));
            Require(InputBufferTicks, 1, MaxStatusTicks, nameof(InputBufferTicks));
            Require(ObstacleDepth, 1, MaxLength, nameof(ObstacleDepth));
            Require(RowLength, ObstacleDepth, MaxLength, nameof(RowLength));
            Require(HurdleHeight, 1, MaxLength, nameof(HurdleHeight));
            Require(WallHeight, 1, MaxLength, nameof(WallHeight));
            Require(BeamBottom, 0, WallHeight - 1, nameof(BeamBottom));
            Require(BaseSpeed, 1, MaxSpeedLimit, nameof(BaseSpeed));
            Require(SpeedStep, 0, MaxSpeedLimit, nameof(SpeedStep));
            Require(MaxSpeedLevel, 0, 100, nameof(MaxSpeedLevel));
            Require(SpeedLevelMeters, 1, MaxMeters, nameof(SpeedLevelMeters));
            Require(MaxTier, 1, 100, nameof(MaxTier));
            Require(TierMeters, 1, MaxMeters, nameof(TierMeters));
            Require(RunwayMeters, 1, MaxMeters, nameof(RunwayMeters));
            Require(LookAheadMeters, 1, MaxMeters, nameof(LookAheadMeters));
            Require(BehindMeters, 1, MaxMeters, nameof(BehindMeters));
            Require(MaxActiveChunks, 2, MaxActiveChunksLimit, nameof(MaxActiveChunks));
            if (RunnerWidth % 2 != 0 || RunnerDepth % 2 != 0 || LaneWidth % 2 != 0)
            {
                throw new ArgumentException("自機の幅・奥行きとレーンの幅は偶数にしてください（中心から半分ずつ広げる）。");
            }

            if (TopSpeed > MaxSpeedLimit)
            {
                throw new ArgumentException($"最も速い段階の速さ {TopSpeed} が上限 {MaxSpeedLimit} を超えます。");
            }

            // 隣のチャンクの障害物と同じ Tick に触れないよう、チャンクの先頭と末尾の空の行は
            // 自機の奥行きと 1 Tick の前進を合わせた長さ以上にする（チャンクごとの到達性の検査が独立に成り立つ）。
            if (RowLength < RunnerDepth + TopSpeed)
            {
                throw new ArgumentException($"行の奥行き {RowLength} は自機の奥行きと最も速い前進の和 {RunnerDepth + TopSpeed} 以上にしてください。");
            }
        }

        private static void Require(int value, int min, int max, string name)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} にしてください。");
            }
        }
    }
}
