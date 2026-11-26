using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum CardType
    {
        Attack,
        Skill,

        /// <summary>持続。使うと効果が状態として残り、カードは廃棄の山へ移る。</summary>
        Power,
    }

    public enum Rarity
    {
        /// <summary>初期デッキだけのカード。報酬に出ない。</summary>
        Starter,
        Common,
        Uncommon,
        Rare,
    }

    /// <summary>効果（コマンド）の種類。</summary>
    public enum EffectKind
    {
        /// <summary>攻撃。量に筋力・脱力・弱体が掛かり、ブロックが先に受ける。</summary>
        Damage,

        /// <summary>使う者の今のブロックを基礎の量にした攻撃。</summary>
        DamageByBlock,

        Block,

        /// <summary>今のブロックを 2 倍にする。</summary>
        DoubleBlock,

        Draw,

        GainEnergy,

        /// <summary>手札から無作為に量の枚数を廃棄する。</summary>
        ExhaustRandom,

        /// <summary>状態異常を量だけ足す。</summary>
        Status,

        /// <summary>敵を呼ぶ（敵の行動だけ）。</summary>
        Summon,
    }

    /// <summary>効果の対象。カードは Chosen・AllEnemies・RandomEnemy・Self・None、敵は Self・Player・None を使う。</summary>
    public enum TargetMode
    {
        None,

        /// <summary>効果を出した者自身。</summary>
        Self,

        /// <summary>プレイヤーが選んだ敵 1 体（カードだけ）。選ぶのは App、Core は確定した対象を受け取る。</summary>
        Chosen,

        /// <summary>解決の時点で生きている全ての敵（番号順）。</summary>
        AllEnemies,

        /// <summary>解決の時点で生きている敵から無作為に 1 体。</summary>
        RandomEnemy,

        /// <summary>プレイヤー（敵の行動だけ）。</summary>
        Player,
    }

    public enum StatusKind
    {
        /// <summary>攻撃の量に足す。戦闘の間ずっと残る。</summary>
        Strength,

        /// <summary>脱力：与える攻撃が 75%。持ち主のターンの終わりに 1 減る。</summary>
        Weak,

        /// <summary>弱体：受ける攻撃が 150%。持ち主のターンの終わりに 1 減る。</summary>
        Vulnerable,

        /// <summary>毒：持ち主のターンの初めに量だけ HP を失い（ブロックを無視）、1 減る。</summary>
        Poison,

        /// <summary>守りの火：自分のターンの初めに量のブロックを得る（持続）。</summary>
        Bulwark,

        /// <summary>煤煙：自分のターンの初めに全ての敵へ量の毒を与える（持続）。</summary>
        Miasma,
    }

    /// <summary>カード・敵の行動に書いた効果 1 つ。書いた順に 1 コマンドとして解決する。</summary>
    public sealed class EffectDef
    {
        public EffectDef(EffectKind kind, int amount, TargetMode target, StatusKind status = StatusKind.Strength, string summon = "")
        {
            Kind = kind;
            Amount = amount;
            Target = target;
            Status = status;
            Summon = summon;
        }

        public EffectKind Kind { get; }

        public int Amount { get; }

        public TargetMode Target { get; }

        /// <summary><see cref="EffectKind.Status"/> のときの種類。</summary>
        public StatusKind Status { get; }

        /// <summary><see cref="EffectKind.Summon"/> のときの敵の ID。</summary>
        public string Summon { get; }
    }

    public sealed class CardDef
    {
        internal CardDef(int index, string id, string name, CardType type, Rarity rarity, int cost, bool exhaust, EffectDef[] effects)
        {
            Index = index;
            Id = id;
            Name = name;
            Type = type;
            Rarity = rarity;
            Cost = cost;
            Exhaust = exhaust;
            Effects = Array.AsReadOnly(effects);
            foreach (var e in effects)
            {
                if (e.Target == TargetMode.Chosen) { NeedsTarget = true; }
            }
        }

        /// <summary>データの中の番号（指紋・ハッシュ用）。</summary>
        public int Index { get; }

        public string Id { get; }

        public string Name { get; }

        public CardType Type { get; }

        public Rarity Rarity { get; }

        public int Cost { get; }

        /// <summary>使った後に捨て札ではなく廃棄の山へ移る。持続カードは常に廃棄の山へ移る。</summary>
        public bool Exhaust { get; }

        /// <summary>使った後に廃棄の山へ移るか（廃棄のキーワードか持続）。</summary>
        public bool GoesToExhaust => Exhaust || Type == CardType.Power;

        /// <summary>対象の敵を 1 体選ぶ必要があるか（効果に <see cref="TargetMode.Chosen"/> がある）。</summary>
        public bool NeedsTarget { get; }

        public IReadOnlyList<EffectDef> Effects { get; }
    }

    /// <summary>敵の行動 1 つ。</summary>
    public sealed class MoveDef
    {
        internal MoveDef(int index, string id, string name, int weight, EffectDef[] effects)
        {
            Index = index;
            Id = id;
            Name = name;
            Weight = weight;
            Effects = Array.AsReadOnly(effects);
        }

        public int Index { get; }

        public string Id { get; }

        public string Name { get; }

        /// <summary><see cref="MoveOrder.Random"/> の抽選の重み。</summary>
        public int Weight { get; }

        public IReadOnlyList<EffectDef> Effects { get; }
    }

    public enum MoveOrder
    {
        /// <summary>書いた順に繰り返す。</summary>
        Cycle,

        /// <summary>重みで抽選する。直前と同じ行動は選ばない。</summary>
        Random,
    }

    public sealed class EnemyDef
    {
        internal EnemyDef(int index, string id, string name, int hp, MoveOrder order, MoveDef[] moves, int halfMove)
        {
            Index = index;
            Id = id;
            Name = name;
            Hp = hp;
            Order = order;
            Moves = Array.AsReadOnly(moves);
            HalfMove = halfMove;
        }

        public int Index { get; }

        public string Id { get; }

        public string Name { get; }

        public int Hp { get; }

        public MoveOrder Order { get; }

        public IReadOnlyList<MoveDef> Moves { get; }

        /// <summary>HP が半分以下になって最初に選ぶ行動の番号（一度だけ、普段の順番には入らない）。なければ -1。</summary>
        public int HalfMove { get; }
    }

    /// <summary>遭遇の強さ。ルートの節ごとに選べる段が決まっている。</summary>
    public enum EncounterTier
    {
        Easy = 1,
        Normal = 2,
        Hard = 3,
        Boss = 4,
    }

    public sealed class EncounterDef
    {
        internal EncounterDef(int index, string id, string name, EncounterTier tier, EnemyDef[] foes)
        {
            Index = index;
            Id = id;
            Name = name;
            Tier = tier;
            Foes = Array.AsReadOnly(foes);
        }

        public int Index { get; }

        public string Id { get; }

        public string Name { get; }

        public EncounterTier Tier { get; }

        public IReadOnlyList<EnemyDef> Foes { get; }
    }

    /// <summary>デッキの中のカード 1 枚（実体）。ID は 1 回の挑戦の中で一意。</summary>
    public sealed class CardRef
    {
        public CardRef(int id, CardDef def)
        {
            Id = id;
            Def = def ?? throw new ArgumentNullException(nameof(def));
        }

        public int Id { get; }

        public CardDef Def { get; }
    }
}
