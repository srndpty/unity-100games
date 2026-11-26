using System.Collections.Generic;
using IntGeometry;

namespace Game.Core
{
    /// <summary>自機。</summary>
    internal sealed class Player
    {
        public Vec2i Position;
        public Direction Facing = Direction.South;
        public int Hp;
        public int MaxHp;
        public int Level = 1;
        public int Exp;
        public int Attack;
        public int Defense;
        public int Satiety;

        /// <summary>時間を使った行動の数（満腹度・自然回復を数える）。</summary>
        public int Actions;

        /// <summary>眠り薬で眠っている残りの行動数。0 より大きい間は自動で足踏みする。</summary>
        public int SleepTurns;

        public readonly List<ItemKind> Inventory = new();
    }

    /// <summary>敵。番号は試合の中で出現の順に 1 から振る（スケジューラの同時刻の順序にも使う）。</summary>
    internal sealed class Monster
    {
        public Monster(int id, MonsterKind kind, Vec2i position, int hp)
        {
            Id = id;
            Kind = kind;
            Position = position;
            Hp = hp;
            MaxHp = hp;
        }

        public int Id { get; }

        public MonsterKind Kind { get; }

        public Vec2i Position;
        public int Hp;
        public int MaxHp;

        /// <summary>生まれつきの眠り（大蛇）。自機が隣に来るか攻撃されると起きる。</summary>
        public bool Asleep;

        /// <summary>眠り薬の残りの行動数。攻撃されても起きない。</summary>
        public int SleepTurns;

        /// <summary>盗んだ品（盗っ人）。倒すと落とす。</summary>
        public ItemKind? Held;

        /// <summary>最後に自機を見た位置。見失った後はここへ向かう。</summary>
        public Vec2i? LastSeen;

        /// <summary>巡回の行き先。</summary>
        public Vec2i? WanderTarget;

        public bool IsSleeping => Asleep || SleepTurns > 0;
    }

    /// <summary>試合の状態。</summary>
    public enum RunState
    {
        Playing,
        Won,
        Dead,
    }

    /// <summary>死因。</summary>
    public enum DeathCause
    {
        None,
        Monster,
        Starvation,
    }
}
