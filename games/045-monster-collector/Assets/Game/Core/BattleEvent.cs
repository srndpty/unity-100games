namespace Game.Core
{
    public enum BattleEventKind
    {
        /// <summary>戦闘の始まり（野生が現れた・トレーナーが勝負をしかけてきた）。</summary>
        Started = 1,

        /// <summary>場に出した（味方・相手）。</summary>
        SentOut = 2,

        /// <summary>手持ちに戻した。</summary>
        Withdrew = 3,

        UsedMove = 4,
        Missed = 5,

        /// <summary>ダメージ（Amount、HpAfter、Effectiveness）。</summary>
        Damaged = 6,

        Healed = 7,
        StatusInflicted = 8,

        /// <summary>状態異常の技が効かなかった（既に状態異常がある）。</summary>
        StatusFailed = 9,

        /// <summary>予定のターンの開始で状態異常が治った。</summary>
        StatusCured = 10,

        /// <summary>眠っていて動けない。</summary>
        Asleep = 11,

        /// <summary>毒のダメージ（Amount、HpAfter）。</summary>
        StatusDamage = 12,

        StatChanged = 13,

        /// <summary>能力がそれ以上変わらない。</summary>
        StatUnchanged = 14,

        Fainted = 15,
        ExpGained = 16,
        LeveledUp = 17,
        LearnedMove = 18,

        /// <summary>技が 4 つで覚えられない（戦闘の後に入れ替えるか選ぶ）。</summary>
        WantsToLearn = 19,

        PotionUsed = 20,

        /// <summary>捕獲玉を投げた（Amount は揺れた回数 0〜3）。</summary>
        BallThrown = 21,

        Captured = 22,
        BrokeFree = 23,
        RanAway = 24,
        RunFailed = 25,
        Won = 26,
        Lost = 27,
        TurnStarted = 28,
    }

    /// <summary>
    /// 戦闘の出来事（演出用の記録）。状態は変えない。演出はこの列を時間をかけて見せるだけ（041 の方式）。
    /// <see cref="Player"/> は味方側か、<see cref="Slot"/> は味方なら手持ちの番号、相手なら相手の手持ちの番号。
    /// </summary>
    public sealed class BattleEvent
    {
        internal BattleEvent(BattleEventKind kind, bool player, int slot, string name)
        {
            Kind = kind;
            Player = player;
            Slot = slot;
            Name = name;
        }

        public BattleEventKind Kind { get; }

        public bool Player { get; }

        public int Slot { get; }

        /// <summary>主語のモンスターの名前（トレーナーの出来事ではトレーナーの名前）。</summary>
        public string Name { get; }

        public int Amount { get; internal set; }

        public int HpAfter { get; internal set; }

        public int MaxHp { get; internal set; }

        /// <summary>相性の百分率（Damaged のみ）。</summary>
        public int Effectiveness { get; internal set; } = 100;

        public MoveDef? Move { get; internal set; }

        /// <summary>場に出した個体の種族（SentOut のみ）。</summary>
        public SpeciesDef? Species { get; internal set; }

        public StatusDef? Status { get; internal set; }

        public StatKind Stat { get; internal set; }

        public int Level { get; internal set; }

        /// <summary>このターンの番号（TurnStarted・StatusCured など）。</summary>
        public int Turn { get; internal set; }

        public override string ToString() => $"{Kind}({(Player ? "P" : "F")}{Slot} {Name} {Amount})";
    }
}
