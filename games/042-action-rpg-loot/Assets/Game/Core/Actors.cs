using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>1 Tick の自機への操作。押下（<see cref="Skill"/>・<see cref="Potion"/>・<see cref="Pick"/>）はその Tick の間に押したか。</summary>
    public readonly struct HeroCommand
    {
        public HeroCommand(int moveX, int moveY, Vec2i aim, bool attack, bool skill = false, bool potion = false, bool pick = false)
        {
            MoveX = Math.Sign(moveX);
            MoveY = Math.Sign(moveY);
            Aim = aim;
            Attack = attack;
            Skill = skill;
            Potion = potion;
            Pick = pick;
        }

        public int MoveX { get; }

        public int MoveY { get; }

        /// <summary>照準の点（階の座標）。</summary>
        public Vec2i Aim { get; }

        /// <summary>基本攻撃を押している（押したままで攻撃間隔ごとに続ける）。</summary>
        public bool Attack { get; }

        public bool Skill { get; }

        public bool Potion { get; }

        public bool Pick { get; }

        public static HeroCommand Idle(Vec2i aim) => new(0, 0, aim, false);
    }

    /// <summary>自機の状態。</summary>
    public sealed class Hero
    {
        public Vec2i Position { get; internal set; }

        /// <summary>照準の向き（長さは問わない、0 にはしない）。</summary>
        public Vec2i Facing { get; internal set; } = new(1, 0);

        public int Life { get; internal set; }

        public bool Dead { get; internal set; }

        /// <summary>倒れてから復活までの残り Tick。</summary>
        public int DeathTimer { get; internal set; }

        public int Invuln { get; internal set; }

        /// <summary>次の基本攻撃までの残り Tick。</summary>
        public int AttackCooldown { get; internal set; }

        /// <summary>振っている基本攻撃が当たる Tick（なければ -1）と向き。</summary>
        public long SwingHitTick { get; internal set; } = -1;

        public Vec2i SwingDirection { get; internal set; } = new(1, 0);

        /// <summary>振り始めた Tick（描画用）。</summary>
        public long SwingStartTick { get; internal set; } = -1;

        public int SkillCooldown { get; internal set; }

        /// <summary>薙ぎ払いが当たる Tick（なければ -1）と振り始めた Tick。</summary>
        public long SpinHitTick { get; internal set; } = -1;

        public long SpinStartTick { get; internal set; } = -1;

        public int PotionCooldown { get; internal set; }

        public int Potions { get; internal set; }

        public Hero Clone() => (Hero)MemberwiseClone();
    }

    public enum FoeState
    {
        Move,
        Windup,
        ChargeWindup,
        Charge,
        Recover,
    }

    /// <summary>敵 1 体の状態。</summary>
    public sealed class Foe
    {
        internal Foe(FoeSpawn spawn, int maxLife, int damage)
        {
            Spawn = spawn;
            MaxLife = maxLife;
            Damage = damage;
            Reset();
        }

        public FoeSpawn Spawn { get; }

        public FoeKind Kind => Spawn.Kind;

        public int MaxLife { get; }

        /// <summary>この階の威力（倍率を掛けた後）。</summary>
        public int Damage { get; }

        public Vec2i Position { get; internal set; }

        public int Life { get; internal set; }

        public bool Aggro { get; internal set; }

        public FoeState State { get; internal set; }

        /// <summary>状態の残り Tick（移動中は射手・突進の次の攻撃までの待ち）。</summary>
        public int Timer { get; internal set; }

        /// <summary>構え・突進の向き。</summary>
        public Vec2i Direction { get; internal set; } = new(1, 0);

        /// <summary>この突進でもう当てたか。</summary>
        public bool ChargeHit { get; internal set; }

        /// <summary>番人の火の扇・突進までの残り Tick。</summary>
        public int FanTimer { get; internal set; }

        public int ChargeTimer { get; internal set; }

        public bool Dead { get; internal set; }

        /// <summary>ドロップを判定したか（倒れた Tick に 1 回だけ）。</summary>
        public bool Dropped { get; internal set; }

        /// <summary>最後に攻撃を受けた Tick（描画の点滅用）。</summary>
        public long LastHitTick { get; internal set; } = -1000;

        public bool Enraged => Kind == FoeKind.Boss && (long)Life * 2 <= MaxLife;

        public Foe Clone() => (Foe)MemberwiseClone();

        /// <summary>生きている敵を初めの位置と体力へ戻す（自機の復活のとき）。</summary>
        internal void Reset()
        {
            Position = Spawn.Center;
            Life = MaxLife;
            Aggro = false;
            State = FoeState.Move;
            Timer = 0;
            Direction = new Vec2i(1, 0);
            ChargeHit = false;
            FanTimer = 0;
            ChargeTimer = 0;
        }
    }

    /// <summary>敵の飛び道具。</summary>
    public sealed class Projectile
    {
        internal Projectile(Vec2i position, Vec2i velocity, int radius, int damage, Element element, FoeKind owner)
        {
            Position = position;
            Velocity = velocity;
            Radius = radius;
            Damage = damage;
            Element = element;
            Owner = owner;
        }

        public Vec2i Position { get; internal set; }

        public Vec2i Velocity { get; }

        public int Radius { get; }

        public int Damage { get; }

        public Element Element { get; }

        public FoeKind Owner { get; }

        public Projectile Clone() => (Projectile)MemberwiseClone();
    }

    /// <summary>地面に落ちている品。<see cref="Item"/> が null なら回復薬。</summary>
    public sealed class GroundItem
    {
        internal GroundItem(long id, Vec2i position, Item? item)
        {
            Id = id;
            Position = position;
            Item = item;
        }

        /// <summary>試合の中で一意の番号。</summary>
        public long Id { get; }

        public Vec2i Position { get; }

        public Item? Item { get; }

        public bool IsPotion => Item == null;
    }
}
