using IntGeometry;

namespace Game.Core
{
    /// <summary>1 回の操作の間に起きた出来事の種類。メッセージと音はここから作る。</summary>
    public enum EventKind
    {
        /// <summary>近接攻撃。Actor が Target を攻撃し、Value はダメージ（外れは -1）。自機は番号 0。</summary>
        Attack,

        /// <summary>射手の矢。Value はダメージ（外れは -1）。</summary>
        Shot,

        /// <summary>敵が倒れた（Actor は倒れた敵）。</summary>
        Killed,

        LevelUp,
        PickedUp,

        /// <summary>品の上に乗ったが持ち物が満杯で拾えない。</summary>
        FullCannotPickUp,

        Dropped,

        /// <summary>品を使った（効果の出来事は別に出る）。</summary>
        Used,

        Healed,
        Poisoned,
        FellAsleep,
        WokeUp,
        Ate,
        MapRevealed,
        Warped,

        Thrown,

        /// <summary>投げた品が敵に当たった。Value はダメージ（効果がダメージでなければ 0）。</summary>
        ThrowHit,

        /// <summary>投げた品が床に落ちた。</summary>
        ThrowLanded,

        /// <summary>置き場がなく品が消えた。</summary>
        ItemLost,

        /// <summary>盗っ人に盗まれた。</summary>
        Stolen,

        /// <summary>盗っ人が別の場所へ跳んだ（Position は跳ぶ前の位置）。</summary>
        Vanished,

        /// <summary>敵が目を覚ました。</summary>
        MonsterWoke,

        /// <summary>敵が眠った（眠り薬）。</summary>
        MonsterSlept,

        Hungry,
        Starving,

        /// <summary>空腹で HP が減った。Value は減った量。</summary>
        Starve,

        Descended,
        Died,
        Won,
    }

    /// <summary>
    /// 出来事。<see cref="Seen"/> は起きた場所が自機の視界の中だったか（自機自身の出来事は常に true）。
    /// <see cref="PlayerView"/> は見えた出来事だけを出す。
    /// </summary>
    public readonly struct RunEvent
    {
        public RunEvent(EventKind kind, int actor, int target, MonsterKind monster, ItemKind item, Vec2i position, int value, bool seen)
        {
            Kind = kind;
            Actor = actor;
            Target = target;
            Monster = monster;
            Item = item;
            Position = position;
            Value = value;
            Seen = seen;
        }

        public EventKind Kind { get; }

        /// <summary>行った者の番号（自機は 0）。</summary>
        public int Actor { get; }

        /// <summary>受けた者の番号（自機は 0、なければ -1）。</summary>
        public int Target { get; }

        /// <summary>関わった敵の種類（敵が関わらなければ既定値）。</summary>
        public MonsterKind Monster { get; }

        public ItemKind Item { get; }

        public Vec2i Position { get; }

        public int Value { get; }

        public bool Seen { get; }

        public override string ToString() => $"{Kind} actor={Actor} target={Target} {Monster} {Item} {Position} value={Value} seen={Seen}";
    }
}
