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

        /// <summary>この Tick の始めに芝（ターマックの外）にいたか。</summary>
        public bool Offroad;

        /// <summary>この Tick に使った操作（表示用。以後の動きには使わない）。</summary>
        public Drive Input;

        /// <summary>この Tick に他の車と触れたか（表示用）。</summary>
        public bool Bumped;

        /// <summary>完走した Tick。していないときは -1。</summary>
        public int FinishTick;

        public bool Finished;

        public Kart Clone() => this;
    }
}
