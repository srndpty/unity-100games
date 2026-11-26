namespace Game.Core
{
    public enum EventKind
    {
        /// <summary>プレイヤーの番が始まった。Amount = ターン数。</summary>
        TurnStarted,

        /// <summary>カードを使った（手札 → 解決中）。Detail = カードの実体 ID、Target = 選んだ敵（なければ -1）、Amount = 払ったエネルギー。</summary>
        CardPlayed,

        /// <summary>敵が行動を始めた。Source = 敵、Detail = 行動の番号。</summary>
        EnemyActed,

        /// <summary>攻撃を受けた。Amount = 失った HP、Detail = ブロックで防いだ量。</summary>
        Damaged,

        /// <summary>毒で HP を失った。Amount = 失った HP。毒は 1 減る。</summary>
        PoisonTick,

        /// <summary>ブロックを得た。Amount = 増えた量。</summary>
        BlockGained,

        /// <summary>ターンの初めにブロックが 0 に戻った。Amount = 失った量。</summary>
        BlockLost,

        /// <summary>状態異常が変わった。Detail = 種類、Amount = 足した量（ターンの終わりの減少は負）。毒の減少は <see cref="PoisonTick"/> に含む。</summary>
        StatusApplied,

        /// <summary>エネルギーが変わった（カードの効果、ターンの初めの補充）。Amount = 増えた量（補充で減るときは負）。</summary>
        EnergyGained,

        /// <summary>敵が次の行動を決めて公開した。Source = 敵、Detail = 行動の番号。</summary>
        IntentSet,

        /// <summary>カードを引いた（山札 → 手札）。Detail = 実体 ID。</summary>
        Drawn,

        /// <summary>手札が満杯で引けなかった。</summary>
        HandFull,

        /// <summary>捨て札を混ぜて山札にした。Amount = 枚数。</summary>
        Shuffled,

        /// <summary>カードが捨て札へ移った。Detail = 実体 ID。</summary>
        Discarded,

        /// <summary>カードが廃棄の山へ移った。Detail = 実体 ID。</summary>
        Exhausted,

        /// <summary>対象がいないため効果が出なかった。Detail = 効果の種類。</summary>
        Fizzled,

        /// <summary>倒れた。</summary>
        Died,

        /// <summary>敵が呼ばれた。Target = 新しい敵の番号。</summary>
        Summoned,

        Victory,

        Defeat,
    }

    /// <summary>
    /// 解決の記録 1 件。Core は状態を先に最後まで変え、演出（Unity）はこの列を順に再生する。
    /// Source・Target は戦う者の番号（プレイヤー 0、敵 1〜）。使わない項目は -1 か 0。
    /// </summary>
    public readonly struct BattleEvent
    {
        public BattleEvent(EventKind kind, int source, int target, int amount, int detail)
        {
            Kind = kind;
            Source = source;
            Target = target;
            Amount = amount;
            Detail = detail;
        }

        public EventKind Kind { get; }

        public int Source { get; }

        public int Target { get; }

        public int Amount { get; }

        public int Detail { get; }

        public override string ToString() => $"{Kind}({Source}->{Target} {Amount} {Detail})";
    }
}
