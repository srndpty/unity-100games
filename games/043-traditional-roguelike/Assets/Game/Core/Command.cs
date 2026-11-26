namespace Game.Core
{
    /// <summary>自機の操作の種類。</summary>
    public enum CommandKind
    {
        /// <summary>8 方向に 1 歩。敵のいるマスなら攻撃。</summary>
        Move,

        /// <summary>向きだけ変える。時間を使わない。</summary>
        Face,

        /// <summary>足踏み（1 行動）。</summary>
        Wait,

        /// <summary>足元の品を拾う。</summary>
        Pickup,

        /// <summary>持ち物を使う。</summary>
        Use,

        /// <summary>持ち物を向いている方向へ投げる。</summary>
        Throw,

        /// <summary>持ち物を足元に置く。</summary>
        Drop,

        /// <summary>階段を降りる。</summary>
        Descend,
    }

    /// <summary>自機の 1 操作。方向は Move・Face、枠は Use・Throw・Drop で使う。</summary>
    public readonly struct Command
    {
        private Command(CommandKind kind, Direction direction, int slot)
        {
            Kind = kind;
            Direction = direction;
            Slot = slot;
        }

        public CommandKind Kind { get; }

        public Direction Direction { get; }

        public int Slot { get; }

        public static Command Move(Direction direction) => new(CommandKind.Move, direction, 0);

        public static Command Face(Direction direction) => new(CommandKind.Face, direction, 0);

        public static Command Wait() => new(CommandKind.Wait, Direction.North, 0);

        public static Command Pickup() => new(CommandKind.Pickup, Direction.North, 0);

        public static Command Use(int slot) => new(CommandKind.Use, Direction.North, slot);

        public static Command Throw(int slot) => new(CommandKind.Throw, Direction.North, slot);

        public static Command Drop(int slot) => new(CommandKind.Drop, Direction.North, slot);

        public static Command Descend() => new(CommandKind.Descend, Direction.North, 0);

        public override string ToString() => Kind switch
        {
            CommandKind.Move or CommandKind.Face => $"{Kind}({Direction})",
            CommandKind.Use or CommandKind.Throw or CommandKind.Drop => $"{Kind}({Slot})",
            _ => Kind.ToString(),
        };
    }

    /// <summary>操作を受け付けなかった理由。</summary>
    public enum RejectReason
    {
        None,

        /// <summary>試合が終わっている。</summary>
        NotPlaying,

        /// <summary>方向・枠の値が範囲外。</summary>
        InvalidCommand,

        /// <summary>壁、または斜めの両脇の壁（角抜け）。</summary>
        Blocked,

        NothingToPickUp,
        InventoryFull,
        EmptySlot,

        /// <summary>石は使えない（投げる物）。</summary>
        CannotUse,

        /// <summary>足元にすでに品がある・階の品が上限。</summary>
        CannotDrop,

        NotOnStairs,
    }

    /// <summary>操作の結果。受け付けなかった操作は状態を何も変えない（時刻・乱数・満腹度も）。</summary>
    public readonly struct Outcome
    {
        private Outcome(RejectReason reason)
        {
            Reason = reason;
        }

        public bool Accepted => Reason == RejectReason.None;

        public RejectReason Reason { get; }

        public static Outcome Ok => new(RejectReason.None);

        public static Outcome Reject(RejectReason reason) => new(reason);

        public override string ToString() => Accepted ? "Accepted" : $"Rejected({Reason})";
    }
}
