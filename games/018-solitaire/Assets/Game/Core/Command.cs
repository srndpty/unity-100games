using System;

namespace Game.Core
{
    /// <summary>コマンドの種類。</summary>
    public enum CommandKind
    {
        /// <summary>札を山から山へ動かす。</summary>
        Move,

        /// <summary>山札から設定の枚数（足りなければ残り全部）を捨て札へ表でめくる。</summary>
        Draw,

        /// <summary>空の山札へ、捨て札を全部伏せて戻す。</summary>
        Recycle,
    }

    /// <summary>
    /// プレイヤーの意図を表す値。合法かどうかは <see cref="MoveRules.Check"/> が決め、
    /// 適用は <see cref="Table.Execute"/>、取り消しはその戻り値の <see cref="MoveRecord"/> で行う。
    /// </summary>
    public readonly struct Command : IEquatable<Command>
    {
        private Command(CommandKind kind, PileId from, PileId to, int count)
        {
            Kind = kind;
            From = from;
            To = to;
            Count = count;
        }

        public static Command Draw => new(CommandKind.Draw, PileId.Stock, PileId.Waste, 0);

        public static Command Recycle => new(CommandKind.Recycle, PileId.Waste, PileId.Stock, 0);

        public CommandKind Kind { get; }

        public PileId From { get; }

        public PileId To { get; }

        /// <summary>Move で動かす枚数（山の上から数える）。Draw と Recycle では 0。</summary>
        public int Count { get; }

        public static Command Move(PileId from, PileId to, int count = 1) => new(CommandKind.Move, from, to, count);

        public bool Equals(Command other) =>
            Kind == other.Kind && From == other.From && To == other.To && Count == other.Count;

        public override bool Equals(object? obj) => obj is Command other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Kind, From, To, Count);

        public override string ToString() => Kind switch
        {
            CommandKind.Move => $"{From}->{To}x{Count}",
            _ => Kind.ToString(),
        };

        public static bool operator ==(Command left, Command right) => left.Equals(right);

        public static bool operator !=(Command left, Command right) => !left.Equals(right);
    }

    /// <summary>コマンドを拒否した理由。None 以外では盤は変わらない。</summary>
    public enum MoveError
    {
        None,

        /// <summary>試合が終わっている。</summary>
        GameOver,

        /// <summary>未定義のコマンド。</summary>
        UnknownCommand,

        /// <summary>同じ山へは動かせない。</summary>
        SamePile,

        /// <summary>動かす枚数が 1 未満。</summary>
        InvalidCount,

        /// <summary>山札から直接は動かせない（めくる）。</summary>
        FromStock,

        /// <summary>山札・捨て札へは置けない。組札から組札へも動かさない。</summary>
        InvalidDestination,

        /// <summary>動かす山が空。</summary>
        SourceEmpty,

        /// <summary>捨て札・組札から、または組札へは 1 枚ずつしか動かせない。</summary>
        NotSingleCard,

        /// <summary>伏せ札を含む、または山の枚数を超える。</summary>
        FaceDown,

        /// <summary>動かす札の並びが、色違いで 1 つずつ下がる列になっていない。</summary>
        BrokenRun,

        /// <summary>空の組札には A だけ置ける。</summary>
        FoundationNeedsAce,

        /// <summary>組札には同じスートしか置けない。</summary>
        FoundationSuitMismatch,

        /// <summary>組札には 1 つ上のランクしか置けない。</summary>
        FoundationRankMismatch,

        /// <summary>空の列には K だけ置ける。</summary>
        EmptyColumnNeedsKing,

        /// <summary>場札には色違いしか置けない。</summary>
        SameColor,

        /// <summary>場札には 1 つ下のランクしか置けない。</summary>
        RankMismatch,

        /// <summary>山札が空なのでめくれない（戻す）。</summary>
        StockEmpty,

        /// <summary>山札が残っているので戻せない。</summary>
        StockNotEmpty,

        /// <summary>捨て札が空なので戻すものがない。</summary>
        WasteEmpty,
    }

    /// <summary>適用したコマンドの記録。<see cref="Table.Undo"/> に渡すと適用前の盤に戻る。</summary>
    public readonly struct MoveRecord : IEquatable<MoveRecord>
    {
        public MoveRecord(Command command, int cards, bool flipped)
        {
            Command = command;
            Cards = cards;
            Flipped = flipped;
        }

        public Command Command { get; }

        /// <summary>動いた札の枚数（めくった・戻した枚数を含む）。</summary>
        public int Cards { get; }

        /// <summary>移動の後、動かした元の列の伏せ札を自動で表にしたか。</summary>
        public bool Flipped { get; }

        public bool Equals(MoveRecord other) => Command == other.Command && Cards == other.Cards && Flipped == other.Flipped;

        public override bool Equals(object? obj) => obj is MoveRecord other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Command, Cards, Flipped);

        public static bool operator ==(MoveRecord left, MoveRecord right) => left.Equals(right);

        public static bool operator !=(MoveRecord left, MoveRecord right) => !left.Equals(right);
    }
}
