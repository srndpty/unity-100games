using System;

namespace Game.Core
{
    /// <summary>戦う者（プレイヤーと敵）。書き換えは Core の中だけ。</summary>
    public class Combatant
    {
        internal const int PlayerId = 0;

        private static readonly int _statusCount = Enum.GetValues(typeof(StatusKind)).Length;

        private int[] _status = new int[_statusCount];

        internal Combatant(int id, int hp, int maxHp)
        {
            Id = id;
            Hp = hp;
            MaxHp = maxHp;
        }

        /// <summary>戦闘の中の番号。プレイヤーは 0、敵は 1 から出た順。</summary>
        public int Id { get; }

        public bool IsPlayer => Id == PlayerId;

        public int Hp { get; internal set; }

        public int MaxHp { get; internal set; }

        public int Block { get; internal set; }

        public bool Alive => Hp > 0;

        public int Status(StatusKind kind) => _status[(int)kind];

        internal void SetStatus(StatusKind kind, int value) => _status[(int)kind] = value;

        internal void AddStatus(StatusKind kind, int amount) => _status[(int)kind] = (int)Math.Min(int.MaxValue / 2, (long)_status[(int)kind] + amount);

        internal virtual Combatant Clone()
        {
            var copy = (Combatant)MemberwiseClone();
            copy._status = (int[])_status.Clone();
            return copy;
        }

        internal virtual void MixInto(ref TileMotion.StateHash hash)
        {
            hash.Mix(Id);
            hash.Mix(Hp);
            hash.Mix(MaxHp);
            hash.Mix(Block);
            foreach (var s in _status) { hash.Mix(s); }
        }
    }

    /// <summary>敵。次の行動（意図）は自分の番の初めに決めて公開する。</summary>
    public sealed class Enemy : Combatant
    {
        internal Enemy(int id, EnemyDef def)
            : base(id, def.Hp, def.Hp)
        {
            Def = def;
        }

        public EnemyDef Def { get; }

        /// <summary>公開中の次の行動。呼ばれたばかりでまだ決まっていなければ null。</summary>
        public MoveDef? Intent { get; internal set; }

        /// <summary>巡回の次の位置（<see cref="MoveOrder.Cycle"/>）。</summary>
        internal int CycleIndex { get; set; }

        /// <summary>直前の行動の番号（<see cref="MoveOrder.Random"/> で同じ行動を続けない）。なければ -1。</summary>
        internal int LastMove { get; set; } = -1;

        /// <summary>HP 半分の行動を選んだか（一度だけ）。</summary>
        internal bool HalfUsed { get; set; }

        internal override Combatant Clone() => base.Clone();

        internal override void MixInto(ref TileMotion.StateHash hash)
        {
            base.MixInto(ref hash);
            hash.Mix(Def.Index);
            hash.Mix(Intent?.Index ?? -1);
            hash.Mix(CycleIndex);
            hash.Mix(LastMove);
            hash.Mix(HalfUsed);
        }
    }
}
