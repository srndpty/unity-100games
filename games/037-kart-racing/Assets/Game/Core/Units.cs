namespace Game.Core
{
    /// <summary>長さと時間の単位。1 単位 = 1 mm。1 秒 = 60 Tick。</summary>
    public static class Units
    {
        public const int TickRate = 60;

        public const int MillimetersPerMeter = 1000;

        /// <summary>壁の 1 本の上限（mm）。これ以下なら線分までの距離の 2 乗が long に収まる。</summary>
        public const int MaxSegment = 8000;

        /// <summary>中心線の、近くとみなす区間の本数（この本数より離れた区間は半幅より離れている）。</summary>
        public const int LocalStations = 2;
    }
}
