using System;
using System.Linq;

namespace Game.Core
{
    /// <summary>不変の 4×4 盤面。マス番号は左上から右へ、行は上から下へ数える。0 は空きマス。</summary>
    public sealed class BoardState : IEquatable<BoardState>
    {
        public const int Size = 4;
        public const int CellCount = Size * Size;

        private readonly int[] _cells;

        private BoardState(int[] cells) => _cells = cells;

        public static BoardState Empty { get; } = new(new int[CellCount]);

        public int this[int cell] => _cells[cell];

        public int EmptyCount => _cells.Count(value => value == 0);
        public int TileCount => CellCount - EmptyCount;
        public int MaxValue => _cells.Max();
        public int Sum => _cells.Sum();

        public static int Index(int x, int y)
        {
            if (x < 0 || x >= Size) { throw new ArgumentOutOfRangeException(nameof(x)); }
            if (y < 0 || y >= Size) { throw new ArgumentOutOfRangeException(nameof(y)); }
            return (y * Size) + x;
        }

        /// <summary>外部から渡された値を検証して盤面を作る。各値は 0、または目標値以下の 2 の累乗。</summary>
        public static BoardState FromValues(params int[] values)
        {
            if (values == null) { throw new ArgumentNullException(nameof(values)); }
            if (values.Length != CellCount) { throw new ArgumentException($"マスの数は {CellCount} です。", nameof(values)); }
            foreach (var value in values)
            {
                if (value != 0 && !IsTileValue(value)) { throw new ArgumentOutOfRangeException(nameof(values), value, "板の値が不正です。"); }
            }

            return new BoardState((int[])values.Clone());
        }

        /// <summary>移動の結果から盤面を作る。合体後の値は検証済みの盤面からしか生まれない。</summary>
        internal static BoardState FromTrusted(int[] cells) => new(cells);

        public int At(int x, int y) => _cells[Index(x, y)];

        public int[] ToArray() => (int[])_cells.Clone();

        /// <summary>空きマスに板を置いた新しい盤面を返す。</summary>
        public BoardState With(int cell, int value)
        {
            if (cell < 0 || cell >= CellCount) { throw new ArgumentOutOfRangeException(nameof(cell)); }
            if (_cells[cell] != 0) { throw new InvalidOperationException("空きマスではありません。"); }
            if (!IsTileValue(value)) { throw new ArgumentOutOfRangeException(nameof(value)); }
            var cells = ToArray();
            cells[cell] = value;
            return new BoardState(cells);
        }

        public bool Equals(BoardState? other) => other != null && _cells.SequenceEqual(other._cells);

        public override bool Equals(object? obj) => Equals(obj as BoardState);

        public override int GetHashCode()
        {
            var hash = 17;
            foreach (var value in _cells) { hash = unchecked((hash * 31) + value); }
            return hash;
        }

        public override string ToString() => string.Join(",", _cells);

        private static bool IsTileValue(int value) =>
            value >= 2 && value <= ForgeRules.TargetValue && (value & (value - 1)) == 0;
    }
}
