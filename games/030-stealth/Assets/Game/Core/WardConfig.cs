using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 試合の設定。速さは 1 Tick あたりの移動量で、1 タイルを <see cref="Units.Tile"/> 等分した整数。時間は Tick 数、角度は
    /// <see cref="Trig"/> の角度（1 周 65536）。浮動小数点を使わないので Mono と .NET で同じ結果になる。
    /// </summary>
    [Serializable]
    public sealed class WardConfig
    {
        /// <summary>気づき値の上限。ここに達したら発見（追跡）。</summary>
        public const int AwarenessMax = 1000;

        /// <summary>1 Tick の移動量の上限。体の大きさの半分未満に抑える（マスを飛び越えず、1 Tick の視線の判定で見逃さない）。</summary>
        public const int MaxSpeed = Units.Tile / 4;

        public const int MaxTicks = 3600;

        /// <summary>視程の上限（単位）。</summary>
        public const int MaxVisionRange = 12 * Units.Tile;

        /// <summary>足音の届く距離の上限（マス）。</summary>
        public const int MaxNoise = 16;

        /// <summary>体（自機・警備員）の幅と高さ。1 マスの通路を通れる大きさ。</summary>
        public int BodySize = 176;

        /// <summary>忍び足の速さ。斜めは各軸 181/256 倍。</summary>
        public int SneakSpeed = 9;

        public int WalkSpeed = 18;

        public int RunSpeed = 32;

        /// <summary>動いている間、何 Tick ごとに足音が出るか。動き始めた Tick に 1 回目が出る。</summary>
        public int StepTicks = 15;

        /// <summary>足音の届く距離（壁を回り込む経路のマス数）。</summary>
        public int SneakNoise = 1;

        public int WalkNoise = 3;

        public int RunNoise = 7;

        public int PatrolSpeed = 12;

        /// <summary>警戒・厳戒の間の巡回の速さ。</summary>
        public int CautionPatrolSpeed = 16;

        public int InvestigateSpeed = 14;

        public int ChaseSpeed = 26;

        public int SearchSpeed = 16;

        /// <summary>1 Tick に向きを変えられる角度。</summary>
        public int TurnSpeed = 1024;

        /// <summary>平常時の視程（単位）。</summary>
        public int VisionRange = 6 * Units.Tile;

        /// <summary>警戒・厳戒の間の視程（単位）。</summary>
        public int AlertVisionRange = 8 * Units.Tile;

        /// <summary>半視野角（向きから左右それぞれ）。既定は 40°。</summary>
        public int VisionHalfAngle = 7282;

        /// <summary>目の前（距離 0）で見えた Tick に増える気づき値。</summary>
        public int AwarenessNearGain = 60;

        /// <summary>視程ちょうどで見えた Tick に増える気づき値。間の距離では線形に補う。</summary>
        public int AwarenessFarGain = 10;

        /// <summary>見えていない Tick に減る気づき値。</summary>
        public int AwarenessDecay = 3;

        /// <summary>調査で目標に着いてから見回す Tick 数。</summary>
        public int InvestigateLookTicks = 120;

        /// <summary>捜索を続ける Tick 数。</summary>
        public int SearchTicks = 600;

        /// <summary>捜索で巡るマスの範囲（最後に見た位置から歩いて何マスまで）。</summary>
        public int SearchRadius = 5;

        /// <summary>捜索で 1 か所に着いてから見回す Tick 数。</summary>
        public int SearchLookTicks = 40;

        /// <summary>見回すときに向きを振る角度（片側）。既定は 50°。</summary>
        public int LookSweep = 9102;

        /// <summary>見回しの 1 往復の Tick 数（4 の倍数）。</summary>
        public int LookPeriodTicks = 160;

        /// <summary>誰も追跡していない Tick がこの数だけ続いたら、厳戒から警戒へ下がる。</summary>
        public int AlertTicks = 600;

        /// <summary>警戒が続いてこの Tick 数で平常へ下がる。</summary>
        public int CautionTicks = 900;

        /// <summary>斜めの移動量。各軸を 1/√2 倍（181/256）にした整数。正の速さは最低 1 にする。</summary>
        public static int DiagonalOf(int speed) => speed <= 0 ? 0 : Math.Max(1, speed * 181 / 256);

        /// <summary>範囲と組み合わせを確かめる。範囲外なら例外（fail-closed）。</summary>
        public void Validate()
        {
            Require(BodySize, Units.Tile / 4, Units.Tile - 16, nameof(BodySize));
            Require(SneakSpeed, 1, MaxSpeed, nameof(SneakSpeed));
            Require(WalkSpeed, SneakSpeed, MaxSpeed, nameof(WalkSpeed));
            Require(RunSpeed, WalkSpeed, MaxSpeed, nameof(RunSpeed));
            if (RunSpeed * 2 >= BodySize) { throw new ArgumentOutOfRangeException(nameof(RunSpeed), RunSpeed, "走りの 1 Tick の移動量は体の大きさの半分未満にしてください。"); }
            Require(StepTicks, 1, MaxTicks, nameof(StepTicks));
            Require(SneakNoise, 0, MaxNoise, nameof(SneakNoise));
            Require(WalkNoise, SneakNoise + 1, MaxNoise, nameof(WalkNoise));
            Require(RunNoise, WalkNoise + 1, MaxNoise, nameof(RunNoise));
            Require(PatrolSpeed, 1, MaxSpeed, nameof(PatrolSpeed));
            Require(CautionPatrolSpeed, PatrolSpeed, MaxSpeed, nameof(CautionPatrolSpeed));
            Require(InvestigateSpeed, 1, MaxSpeed, nameof(InvestigateSpeed));
            Require(ChaseSpeed, 1, RunSpeed - 1, nameof(ChaseSpeed));
            Require(SearchSpeed, 1, MaxSpeed, nameof(SearchSpeed));
            Require(TurnSpeed, 1, Trig.FullTurn / 2, nameof(TurnSpeed));
            Require(VisionRange, Units.Tile, MaxVisionRange, nameof(VisionRange));
            Require(AlertVisionRange, VisionRange, MaxVisionRange, nameof(AlertVisionRange));
            Require(VisionHalfAngle, 1, Trig.FullTurn / 2, nameof(VisionHalfAngle));
            Require(AwarenessFarGain, 1, AwarenessMax, nameof(AwarenessFarGain));
            Require(AwarenessNearGain, AwarenessFarGain, AwarenessMax, nameof(AwarenessNearGain));
            Require(AwarenessDecay, 1, AwarenessMax, nameof(AwarenessDecay));
            Require(InvestigateLookTicks, 1, MaxTicks, nameof(InvestigateLookTicks));
            Require(SearchTicks, 1, MaxTicks, nameof(SearchTicks));
            Require(SearchRadius, 0, MaxNoise, nameof(SearchRadius));
            Require(SearchLookTicks, 1, MaxTicks, nameof(SearchLookTicks));
            Require(LookSweep, 0, Trig.FullTurn / 2, nameof(LookSweep));
            Require(LookPeriodTicks, 4, MaxTicks, nameof(LookPeriodTicks));
            if (LookPeriodTicks % 4 != 0) { throw new ArgumentOutOfRangeException(nameof(LookPeriodTicks), LookPeriodTicks, "見回しの周期は 4 の倍数にしてください。"); }
            Require(AlertTicks, 1, MaxTicks, nameof(AlertTicks));
            Require(CautionTicks, 1, MaxTicks, nameof(CautionTicks));
        }

        /// <summary>進行中の試合が設定の書き換えの影響を受けないよう複製する。</summary>
        public WardConfig Copy() => (WardConfig)MemberwiseClone();

        /// <summary>警戒レベルに応じた視界。</summary>
        public VisionSpec VisionAt(AlertLevel level) => new(level == AlertLevel.Normal ? VisionRange : AlertVisionRange, VisionHalfAngle);

        private static void Require(int value, int min, int max, string name)
        {
            if (value < min || value > max) { throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。"); }
        }
    }
}
