using System;

namespace Game.Core
{
    /// <summary>
    /// 不変の局面（黒と白の石のビット集合と手番）。合法手の生成と着手の適用だけを持ち、パスや終局の進行は <see cref="OthelloGame"/> が決める。
    /// ビット番号はマスの <see cref="Square.Index"/>（Y * 8 + X）。
    /// </summary>
    public sealed class Position : IEquatable<Position>
    {
        // 左右へずらすとき、反対側の列へ回り込んだビットを消すマスク。
        private const ulong NotFileA = 0xFEFEFEFEFEFEFEFEUL;
        private const ulong NotFileH = 0x7F7F7F7F7F7F7F7FUL;

        // 8方向のずらし量と、ずらした後に掛けるマスク。+1 は右、+8 は下。
        private static readonly int[] _shifts = { 1, -1, 8, -8, 9, -9, 7, -7 };
        private static readonly ulong[] _masks = { NotFileA, NotFileH, ulong.MaxValue, ulong.MaxValue, NotFileA, NotFileH, NotFileH, NotFileA };

        public Position(ulong black, ulong white, Side toMove)
        {
            if ((black & white) != 0) { throw new ArgumentException("黒と白が同じマスにあります。", nameof(white)); }
            if (toMove != Side.Black && toMove != Side.White) { throw new ArgumentOutOfRangeException(nameof(toMove)); }
            Black = black;
            White = white;
            ToMove = toMove;
        }

        /// <summary>初期配置。d4・e5 が白、e4・d5 が黒で、黒の手番。</summary>
        public static Position Initial { get; } = new(
            new Square(4, 3).Bit | new Square(3, 4).Bit,
            new Square(3, 3).Bit | new Square(4, 4).Bit,
            Side.Black);

        public ulong Black { get; }
        public ulong White { get; }
        public Side ToMove { get; }

        public ulong Empty => ~(Black | White);
        public int EmptyCount => Bits.PopCount(Empty);

        public ulong Stones(Side side) => side == Side.Black ? Black : White;

        public int Count(Side side) => Bits.PopCount(Stones(side));

        public Disc At(Square square) => (Black & square.Bit) != 0 ? Disc.Black : (White & square.Bit) != 0 ? Disc.White : Disc.Empty;

        /// <summary>side が打てるマスの集合。</summary>
        public ulong LegalMoves(Side side)
        {
            var own = Stones(side);
            var other = Stones(side.Opponent());
            var empty = Empty;
            var moves = 0UL;
            for (var d = 0; d < _shifts.Length; d++)
            {
                // 自分の石から相手の石が続く限り伸ばし（最大6個）、その先の空きが着手点になる。
                var run = Shift(own, d) & other;
                for (var i = 0; i < 5; i++) { run |= Shift(run, d) & other; }
                moves |= Shift(run, d) & empty;
            }

            return moves;
        }

        /// <summary>手番の側が打てるマスの集合。</summary>
        public ulong LegalMoves() => LegalMoves(ToMove);

        public bool HasLegalMove(Side side) => LegalMoves(side) != 0;

        /// <summary>両者とも打てない。盤が埋まった場合を含む。</summary>
        public bool IsTerminal => LegalMoves(Side.Black) == 0 && LegalMoves(Side.White) == 0;

        /// <summary>手番の側が square に打ったときに返る石の集合。空きでない・挟めないなら 0。</summary>
        public ulong Flips(Square square)
        {
            if (!square.IsInside) { return 0; }
            var move = square.Bit;
            if ((move & Empty) == 0) { return 0; }
            var own = Stones(ToMove);
            var other = Stones(ToMove.Opponent());
            var flips = 0UL;
            for (var d = 0; d < _shifts.Length; d++)
            {
                var line = 0UL;
                var cursor = Shift(move, d);
                while ((cursor & other) != 0)
                {
                    line |= cursor;
                    cursor = Shift(cursor, d);
                }

                if ((cursor & own) != 0) { flips |= line; }
            }

            return flips;
        }

        /// <summary>手番の側が square に打った後の局面（手番は相手へ移る）。非合法手なら例外。</summary>
        public Position Play(Square square)
        {
            var flips = Flips(square);
            if (flips == 0) { throw new ArgumentException($"{square.Name} には打てません。", nameof(square)); }
            return Apply(square.Bit, flips);
        }

        /// <summary>着手点と返る石が分かっているときの適用。探索で <see cref="Flips"/> を二度計算しないために使う。</summary>
        internal Position Apply(ulong move, ulong flips)
        {
            return ToMove == Side.Black
                ? new Position(Black | move | flips, White & ~flips, Side.White)
                : new Position(Black & ~flips, White | move | flips, Side.Black);
        }

        /// <summary>石はそのままで手番だけを相手へ渡した局面。</summary>
        public Position Pass() => new(Black, White, ToMove.Opponent());

        public bool Equals(Position? other) => other is not null && Black == other.Black && White == other.White && ToMove == other.ToMove;

        public override bool Equals(object? obj) => Equals(obj as Position);

        public override int GetHashCode() => unchecked((Black.GetHashCode() * 397) ^ (White.GetHashCode() * 31) ^ (int)ToMove);

        private static ulong Shift(ulong value, int direction)
        {
            var amount = _shifts[direction];
            return (amount > 0 ? value << amount : value >> -amount) & _masks[direction];
        }
    }
}
