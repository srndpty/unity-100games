using IntGeometry;

namespace Game.Core
{
    /// <summary>1 台の状態。レースが毎 Tick 書き換える。</summary>
    public struct Kart
    {
        public Vec2i Position;

        public int Heading;

        public int Speed;

        /// <summary>今のドリフトを押している Tick 数。0 はドリフトしていない。</summary>
        public int DriftTicks;

        /// <summary>ドリフトを始めたときの旋回（-1 または 1）。</summary>
        public int DriftSteer;

        public int BoostLeft;

        public int Laps;

        /// <summary>次に有効なゲートの番号。</summary>
        public int NextGate;

        /// <summary>いちばん近い中心線の区間。</summary>
        public int Station;

        /// <summary>その区間の始点からの距離（mm）。</summary>
        public int Along;

        /// <summary>今の区間をどれだけ進んだか（mm、順位に使う）。</summary>
        public int SectionAlong;

        public int StallTicks;

        public int RecoverLeft;

        /// <summary>完走した Tick。していないときは -1。</summary>
        public int FinishTick;

        public bool Finished;

        public Kart Clone() => this;
    }
}
