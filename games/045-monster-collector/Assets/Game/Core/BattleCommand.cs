using System;

namespace Game.Core
{
    public enum CommandKind
    {
        /// <summary>技を使う（Index は技の枠。PP が全て 0 なら わるあがき）。</summary>
        Fight = 1,

        /// <summary>交代する（Index は手持ちの番号）。倒れたときの強制交代もこれ。</summary>
        Switch = 2,

        /// <summary>傷薬を使う（Index は手持ちの番号）。</summary>
        Potion = 3,

        /// <summary>捕獲玉を投げる（野生のみ）。</summary>
        Ball = 4,

        /// <summary>逃げる（野生のみ）。</summary>
        Run = 5,
    }

    public readonly struct BattleCommand : IEquatable<BattleCommand>
    {
        private BattleCommand(CommandKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }

        public CommandKind Kind { get; }

        public int Index { get; }

        public static BattleCommand Fight(int slot) => new(CommandKind.Fight, slot);

        public static BattleCommand Switch(int member) => new(CommandKind.Switch, member);

        public static BattleCommand Potion(int member) => new(CommandKind.Potion, member);

        public static BattleCommand Ball() => new(CommandKind.Ball, 0);

        public static BattleCommand Run() => new(CommandKind.Run, 0);

        public bool Equals(BattleCommand other) => Kind == other.Kind && Index == other.Index;

        public override bool Equals(object? obj) => obj is BattleCommand other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397) ^ Index;

        public override string ToString() => $"{Kind}({Index})";

        public static bool operator ==(BattleCommand left, BattleCommand right) => left.Equals(right);

        public static bool operator !=(BattleCommand left, BattleCommand right) => !left.Equals(right);
    }

    /// <summary>コマンドを受け付けなかった理由。拒否したときは状態・乱数・出来事を何も変えない。</summary>
    public enum RejectReason
    {
        None = 0,

        /// <summary>戦闘が終わっている。</summary>
        BattleOver = 1,

        /// <summary>倒れた味方の交代先を選ぶ場面で、交代以外を選んだ。</summary>
        MustSwitch = 2,

        NoSuchMove = 3,
        NoPp = 4,
        NoSuchMember = 5,
        MemberFainted = 6,
        AlreadyActive = 7,
        NoPotion = 8,

        /// <summary>HP が満タンで傷薬の必要がない。</summary>
        FullHp = 9,

        NoBall = 10,

        /// <summary>トレーナーのモンスターは捕まえられず、トレーナー戦からは逃げられない。</summary>
        TrainerBattle = 11,

        /// <summary>手持ちもボックスも満杯で、捕まえても置き場所がない。</summary>
        NoRoom = 12,
    }
}
