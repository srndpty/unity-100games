using System;

namespace Game.Core
{
    /// <summary>
    /// 車両とレースの設定。値は全て整数（長さは mm、角度は <c>Trig</c> の 1 周 = 65536、時間は Tick）。
    /// Unity のシリアライズのため公開フィールドで書く。使う前に <see cref="Validate"/> で検証し、返った複製を使う。
    /// </summary>
    [Serializable]
    public sealed class KartConfig
    {
        public const int KartCount = 4;

        public const int LapsToFinish = 3;

        /// <summary>車の半径（mm）。</summary>
        public int Radius = 450;

        /// <summary>加速（mm/Tick²）。</summary>
        public int Accel = 8;

        /// <summary>加速の上限（mm/Tick）。</summary>
        public int MaxSpeed = 340;

        /// <summary>ブースト中の上限（mm/Tick）。</summary>
        public int BoostSpeed = 500;

        /// <summary>加速もブレーキもしていないときの減速（mm/Tick²、0 を通り越さない）。</summary>
        public int Drag = 3;

        /// <summary>ブレーキの減速（mm/Tick²）。</summary>
        public int Brake = 14;

        /// <summary>後退の上限（mm/Tick、正の値）。</summary>
        public int ReverseSpeed = 140;

        /// <summary>ごく低速のときの 1 Tick の旋回（角度単位、左が正）。</summary>
        public int TurnAtLow = 700;

        /// <summary><see cref="MaxSpeed"/> のときの 1 Tick の旋回。</summary>
        public int TurnAtHigh = 160;

        /// <summary>ドリフト中に旋回へ足す量。</summary>
        public int DriftTurn = 280;

        /// <summary>ドリフト中の追加の減速。</summary>
        public int DriftDrag = 4;

        /// <summary>ドリフトを始められる最低の速さ。</summary>
        public int DriftMinSpeed = 160;

        /// <summary>ドリフト中、移動方向が向きより遅れる角度。</summary>
        public int SlipAngle = 600;

        /// <summary>ブーストに必要な、ドリフトを押していた Tick 数。</summary>
        public int MiniTurboTicks = 8;

        /// <summary>ブーストの長さ（Tick）。</summary>
        public int BoostTicks = 40;

        /// <summary>CPU がハンドルを向ける、中心線上の先の点までの距離（mm）。速度はこれより先の曲率も見る。</summary>
        public int Lookahead = 8000;

        /// <summary>この角度差以上の先を、ドリフトする角とみなす。</summary>
        public int DriftCorner = 5000;

        /// <summary>角での目標の速さ（mm/Tick）。直線では <see cref="MaxSpeed"/> まで上げる。</summary>
        public int CornerSpeed = 170;

        /// <summary>中心線方向の進みがこの Tick 増えなければ回復を始める。</summary>
        public int StuckTicks = 40;

        /// <summary>回復で後退する Tick 数。</summary>
        public int RecoverTicks = 35;

        /// <summary>CPU 3 台の直線の速さの割合（1000 = プレイヤーと同じ）。角の目標は割合で遅くしない。</summary>
        public int CpuPace1 = 880;

        public int CpuPace2 = 800;

        public int CpuPace3 = 720;

        /// <summary>スタートしてから車を動かさない Tick 数。</summary>
        public int StartDelayTicks = 90;

        /// <summary>この Tick で未完走が残っていても順位を確定する。</summary>
        public int MaxRaceTicks = 20000;

        /// <summary>速さ <paramref name="speed"/> の 1 Tick の旋回量。0 では 0。</summary>
        public int Turn(int speed)
        {
            var magnitude = Math.Abs(speed);
            if (magnitude <= 0) { return 0; }
            if (magnitude >= MaxSpeed) { return TurnAtHigh; }
            return TurnAtLow + ((TurnAtHigh - TurnAtLow) * magnitude / MaxSpeed);
        }

        /// <summary>車の番号（0 がプレイヤー）の直線の割合。</summary>
        public int PaceOf(int index)
        {
            if (index <= 0) { return 1000; }
            if (index == 1) { return CpuPace1; }
            if (index == 2) { return CpuPace2; }
            return CpuPace3;
        }

        public KartConfig Copy() => (KartConfig)MemberwiseClone();

        /// <summary>範囲を確かめ、問題なければ複製を返す。</summary>
        public KartConfig Validate()
        {
            Require(Radius, 200, 2000, nameof(Radius));
            Require(Accel, 1, 80, nameof(Accel));
            Require(MaxSpeed, Accel + 1, 2000, nameof(MaxSpeed));
            Require(BoostSpeed, MaxSpeed, 2500, nameof(BoostSpeed));
            Require(Drag, 0, MaxSpeed, nameof(Drag));
            Require(Brake, 1, MaxSpeed, nameof(Brake));
            Require(ReverseSpeed, 1, MaxSpeed, nameof(ReverseSpeed));
            Require(TurnAtLow, 1, 4000, nameof(TurnAtLow));
            Require(TurnAtHigh, 1, TurnAtLow, nameof(TurnAtHigh));
            Require(DriftTurn, 0, 4000, nameof(DriftTurn));
            Require(DriftDrag, 0, MaxSpeed, nameof(DriftDrag));
            Require(DriftMinSpeed, 1, MaxSpeed, nameof(DriftMinSpeed));
            Require(SlipAngle, 1, TrigQuarter(), nameof(SlipAngle));
            Require(MiniTurboTicks, 1, 600, nameof(MiniTurboTicks));
            Require(BoostTicks, 1, 600, nameof(BoostTicks));
            Require(Lookahead, 1000, 40000, nameof(Lookahead));
            Require(DriftCorner, 1, 20000, nameof(DriftCorner));
            Require(CornerSpeed, 1, MaxSpeed, nameof(CornerSpeed));
            Require(StuckTicks, 1, 600, nameof(StuckTicks));
            Require(RecoverTicks, 1, 600, nameof(RecoverTicks));
            Require(CpuPace1, 1, 1000, nameof(CpuPace1));
            Require(CpuPace2, 1, 1000, nameof(CpuPace2));
            Require(CpuPace3, 1, 1000, nameof(CpuPace3));
            Require(StartDelayTicks, 0, 600, nameof(StartDelayTicks));
            Require(MaxRaceTicks, StartDelayTicks + 1, 200000, nameof(MaxRaceTicks));
            if (BoostSpeed > Units.MaxSegment)
            {
                throw new ArgumentOutOfRangeException(nameof(BoostSpeed), BoostSpeed, "1 Tick の移動が壁の線分の上限を超えます。");
            }

            return Copy();
        }

        private static int TrigQuarter() => 65536 / 8;

        private static void Require(int value, int min, int max, string name)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} にしてください。");
            }
        }
    }
}
