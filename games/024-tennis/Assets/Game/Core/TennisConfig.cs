using System;

namespace Game.Core
{
    /// <summary>
    /// 試合の設定。長さは 1 単位 = 0.1 mm、速さは 1 Tick あたりの単位、時間は Tick 数（60 Tick/秒）。
    /// 浮動小数点を使わないので Mono と .NET で同じ結果になる。
    /// </summary>
    [Serializable]
    public sealed class TennisConfig
    {
        /// <summary>球の速さの上限（1 Tick に 1 m）。</summary>
        public const int MaxBallSpeed = 10000;

        public const int MaxTicks = 600;

        // ---- 試合の長さ ----

        /// <summary>何ゲームずつでタイブレークにするか。0 なら最初からタイブレーク（既定：7 ポイント先取の 1 ゲーム）、6 なら通常の 1 セット。</summary>
        public int TiebreakAtGames;

        /// <summary>タイブレークで勝つのに必要な点（2 点差も要る）。</summary>
        public int TiebreakPoints = 7;

        // ---- 球 ----

        /// <summary>1 Tick あたりの縦の速さの減り（9.8 m/s² ≒ 27）。</summary>
        public int Gravity = 27;

        /// <summary>弾んだときに残る縦の速さ（%）。</summary>
        public int RestitutionPercent = 72;

        /// <summary>弾んだときに残る横の速さ（%）。</summary>
        public int FrictionPercent = 70;

        // ---- 選手と打点 ----

        /// <summary>プレイヤーの移動の速さ（5.5 m/s）。</summary>
        public int MoveSpeed = 917;

        /// <summary>CPU の移動の速さ（5.1 m/s）。</summary>
        public int CpuMoveSpeed = 850;

        /// <summary>振り抜いている間の移動の速さ（%）。構えて待つ間はふつうに動ける。</summary>
        public int SwingMovePercent = 40;

        /// <summary>打った（空振りした）後の振り抜きの長さ。終わるまで次は振れない。</summary>
        public int SwingTicks = 16;

        /// <summary>構えて球を待てる Tick 数（1.5 秒）。押した瞬間に球が届く範囲にあればその場で打ち、まだ来ていなければ理想の打点に来るまで待つ。</summary>
        public int SwingWaitTicks = 90;

        /// <summary>届く範囲（理想の打点からの水平距離、1.8 m）。</summary>
        public int Reach = 18000;

        /// <summary>打てる高さの下限（球の下端、0.1 m）。</summary>
        public int MinHitZ = 1000;

        /// <summary>打てる高さの上限（2.6 m）。</summary>
        public int MaxHitZ = 26000;

        /// <summary>理想の打点：体の前方（0.5 m）。</summary>
        public int ContactAhead = 5000;

        /// <summary>早打ち・遅打ちのずれが最大（±1000）になる、理想の打点からの縦の距離（1.8 m、届く範囲の端）。</summary>
        public int TimingSpan = 18000;

        /// <summary>プレイヤーの打球（サーブを含む）の早打ち・遅打ちのずれを何 % に弱めるか（CPU は 100% のまま）。</summary>
        public int PlayerTimingAssistPercent = 50;

        /// <summary>ネットに近づける距離の下限（1 m）。</summary>
        public int NetGap = 10000;

        /// <summary>壁の線から内側に空ける距離。</summary>
        public int AthleteMargin = 5000;

        // ---- ショット ----

        /// <summary>ストロークの最高速（40 m/s）。ネットの余裕を満たす中でいちばん平らに打つ。</summary>
        public int StrokeSpeed = 6667;

        /// <summary>ストロークのネットの上の余裕（0.5 m）。</summary>
        public int StrokeClearance = 5000;

        /// <summary>ストロークの狙う深さ（ネットから 8.5 m）。</summary>
        public int StrokeDepth = 85000;

        /// <summary>ロブの最高速（15 m/s）。</summary>
        public int LobSpeed = 2500;

        /// <summary>ロブのネットの上の余裕（3 m）。</summary>
        public int LobClearance = 30000;

        /// <summary>ロブの狙う深さ（9.5 m）。</summary>
        public int LobDepth = 95000;

        /// <summary>左右に狙ったときの横の位置（2.6 m）。</summary>
        public int AimWide = 26000;

        /// <summary>ずれが最大のときの横のずれ（早打ちは打つ人の左、遅打ちは右へ、3 m）。</summary>
        public int TimingShift = 30000;

        /// <summary>早打ちが最大のときに伸びる深さ（3.6 m）。</summary>
        public int LongError = 36000;

        /// <summary>遅打ちが最大のときに縮む深さ（8 m）。同じ時間で手前に落ちるので、低くなってネットにかかりうる。</summary>
        public int ShortError = 80000;

        // ---- サーブ ----

        /// <summary>トスを上げる高さ（球の下端、2.0 m）。</summary>
        public int TossZ = 20000;

        /// <summary>トスの上向きの速さ（頂点まで約 1 m）。</summary>
        public int TossSpeed = 735;

        /// <summary>サーブを打てるのは、トスの頂点からこの Tick 数以内。ずれはこの幅で ±1000 になる。</summary>
        public int ServeTimingSpan = 20;

        /// <summary>サーブの最高速（50 m/s）。ネットの余裕を満たす中でいちばん平らに打つので、実際はこれより遅い。</summary>
        public int ServeSpeed = 8333;

        /// <summary>サーブのネットの上の余裕（0.25 m）。</summary>
        public int ServeClearance = 2500;

        /// <summary>サーブの狙う深さ（ネットから 5.5 m）。</summary>
        public int ServeDepth = 55000;

        /// <summary>サーブで左右に狙ったときの、ボックスの中央からの横の位置（1.3 m）。</summary>
        public int ServeAim = 13000;

        /// <summary>サーブのずれが最大のときの横のずれ（1.2 m）。</summary>
        public int ServeShift = 12000;

        /// <summary>サーブの早打ちが最大のときに伸びる深さ（2 m）。</summary>
        public int ServeLongError = 20000;

        /// <summary>サーブの遅打ちが最大のときに縮む深さ（5 m）。同じ時間で手前に落ちるので、急に落ちてネットにかかる。</summary>
        public int ServeShortError = 50000;

        /// <summary>サーバーの位置：ベースラインの後ろ（0.2 m）。</summary>
        public int ServeBackOffset = 2000;

        /// <summary>サーバーの位置：センターからの横（1 m）。ポイントの始めに置く位置。</summary>
        public int ServeSideOffset = 10000;

        /// <summary>サーブ待ちに横へ動けるのは、センターからこの距離（0.3 m）からサイドラインまで。</summary>
        public int ServeSideMin = 3000;

        /// <summary>レシーバーの位置：ベースラインの後ろ（1 m）。</summary>
        public int ReceiveBackOffset = 10000;

        /// <summary>レシーバーの位置：センターからの横（2.2 m）。</summary>
        public int ReceiveSideOffset = 22000;

        // ---- 進行 ----

        /// <summary>ポイントが決まってから次のポイントまで（判定を見せる間）。</summary>
        public int PointGapTicks = 90;

        // ---- CPU ----

        /// <summary>相手が打ってから動き始めるまで。</summary>
        public int CpuReactionTicks = 8;

        /// <summary>
        /// 打点の時機のぶれ（±Tick）。早すぎる分は構えて待つので、ミスは遅れたとき（ネット・右へのアウト）になる。
        /// 補助つきの自動操縦との 20 試合で、4 なら平均 31 本のラリー、5 なら 15 本（1 試合約 3 分）、6 なら 8 本。
        /// </summary>
        public int CpuTimingJitter = 5;

        /// <summary>ロブを選ぶ割合（%）。相手がネットに近いときは必ずロブ。</summary>
        public int CpuLobPercent = 12;

        /// <summary>相手がネットからこの距離より近ければロブ（5 m）。</summary>
        public int CpuLobTrigger = 50000;

        /// <summary>サーブのトスを上げるまでの待ち。</summary>
        public int CpuServeWaitTicks = 40;

        public TennisConfig Copy() => (TennisConfig)MemberwiseClone();

        public BallPhysics Physics => new(Gravity, RestitutionPercent, FrictionPercent);

        /// <summary>トスの頂点の Tick（トスを上げた Tick を 0 とする）。</summary>
        public int TossApexTick => TossSpeed / Gravity;

        public void Validate()
        {
            Require(TiebreakAtGames, 0, MatchScore.MaxTiebreakAtGames, nameof(TiebreakAtGames));
            Require(TiebreakPoints, 1, MatchScore.MaxTiebreakPoints, nameof(TiebreakPoints));
            Require(Gravity, 1, 100, nameof(Gravity));
            Require(RestitutionPercent, 1, 99, nameof(RestitutionPercent));
            Require(FrictionPercent, 1, 100, nameof(FrictionPercent));
            Require(MoveSpeed, 1, MaxBallSpeed, nameof(MoveSpeed));
            Require(CpuMoveSpeed, 1, MaxBallSpeed, nameof(CpuMoveSpeed));
            Require(SwingMovePercent, 0, 100, nameof(SwingMovePercent));
            Require(SwingTicks, 1, MaxTicks, nameof(SwingTicks));
            Require(SwingWaitTicks, 0, MaxTicks, nameof(SwingWaitTicks));
            Require(Reach, 1000, 50000, nameof(Reach));
            Require(MinHitZ, 0, 50000, nameof(MinHitZ));
            Require(MaxHitZ, MinHitZ + 1, 50000, nameof(MaxHitZ));
            Require(ContactAhead, 0, Reach, nameof(ContactAhead));
            Require(TimingSpan, 1, 50000, nameof(TimingSpan));
            Require(PlayerTimingAssistPercent, 0, 100, nameof(PlayerTimingAssistPercent));
            Require(NetGap, 0, Court.HalfLength, nameof(NetGap));
            Require(AthleteMargin, 0, Court.FenceX - Court.HalfWidth, nameof(AthleteMargin));
            Require(StrokeSpeed, 1, MaxBallSpeed, nameof(StrokeSpeed));
            Require(StrokeClearance, 0, 50000, nameof(StrokeClearance));
            Require(StrokeDepth, 1, Court.HalfLength, nameof(StrokeDepth));
            Require(LobSpeed, 1, MaxBallSpeed, nameof(LobSpeed));
            Require(LobClearance, 0, 50000, nameof(LobClearance));
            Require(LobDepth, 1, Court.HalfLength, nameof(LobDepth));
            Require(AimWide, 0, Court.HalfWidth, nameof(AimWide));
            Require(TimingShift, 0, Court.FenceX, nameof(TimingShift));
            Require(LongError, 0, Court.FenceY, nameof(LongError));
            Require(ShortError, 0, Math.Min(StrokeDepth, LobDepth) - 1, nameof(ShortError));
            Require(TossZ, 1, 50000, nameof(TossZ));
            Require(TossSpeed, Gravity, MaxBallSpeed, nameof(TossSpeed));
            Require(ServeTimingSpan, 1, TossApexTick, nameof(ServeTimingSpan));
            Require(ServeSpeed, 1, MaxBallSpeed, nameof(ServeSpeed));
            Require(ServeClearance, 0, 50000, nameof(ServeClearance));
            Require(ServeDepth, 1, Court.ServiceLine, nameof(ServeDepth));
            Require(ServeAim, 0, Court.HalfWidth / 2, nameof(ServeAim));
            Require(ServeShift, 0, Court.FenceX, nameof(ServeShift));
            Require(ServeLongError, 0, Court.FenceY, nameof(ServeLongError));
            Require(ServeShortError, 0, ServeDepth - 1, nameof(ServeShortError));
            Require(ServeBackOffset, 0, Court.FenceY - Court.HalfLength - AthleteMargin, nameof(ServeBackOffset));
            Require(ServeSideMin, 0, Court.HalfWidth, nameof(ServeSideMin));
            Require(ServeSideOffset, ServeSideMin, Court.HalfWidth, nameof(ServeSideOffset));
            Require(ReceiveBackOffset, 0, Court.FenceY - Court.HalfLength - AthleteMargin, nameof(ReceiveBackOffset));
            Require(ReceiveSideOffset, 0, Court.HalfWidth, nameof(ReceiveSideOffset));
            Require(PointGapTicks, 1, MaxTicks, nameof(PointGapTicks));
            Require(CpuReactionTicks, 0, MaxTicks, nameof(CpuReactionTicks));
            Require(CpuTimingJitter, 0, 30, nameof(CpuTimingJitter));
            Require(CpuLobPercent, 0, 100, nameof(CpuLobPercent));
            Require(CpuLobTrigger, 0, Court.HalfLength, nameof(CpuLobTrigger));
            Require(CpuServeWaitTicks, 0, MaxTicks, nameof(CpuServeWaitTicks));

            // 打点を飛び越えない：1 Tick の球の移動が届く範囲より小さい（打点の Tick に、球が届く範囲を通り過ぎてしまわない）。
            var fastest = Math.Max(StrokeSpeed, Math.Max(LobSpeed, ServeSpeed));
            if (fastest >= Reach)
            {
                throw new ArgumentException($"球の最高速（{fastest}）が届く範囲（{Reach}）以上です。1 Tick で打点を飛び越えます。");
            }

            // ずれ 0 の打球が入ること：狙いを左・中・右に変えて実際に球を飛ばして確かめる。
            var failure = ShotCheck.FirstFailure(this);
            if (failure != null) { throw new ArgumentException(failure); }
        }

        private static void Require(int value, int min, int max, string name)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{name} は {min}〜{max} にしてください。");
            }
        }
    }
}
