using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>確定したブロックだけを持つ盤面。不変で、変更は新しい盤面を返す。</summary>
    public sealed class Board : IEquatable<Board>
    {
        public const int Width = 10;
        public const int VisibleHeight = 20;
        public const int Height = 24;

        public static readonly Board Empty = new(new byte[Width * Height]);

        private const byte EmptyCell = 0;

        // 0 は空き、それ以外は PieceKind + 1。
        private readonly byte[] _cells;

        private Board(byte[] cells) => _cells = cells;

        public static bool IsInside(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        /// <summary>そのマスの確定ブロックの種類。空きなら null。</summary>
        public PieceKind? At(int x, int y)
        {
            RequireInside(x, y);
            var value = _cells[Index(x, y)];
            return value == EmptyCell ? null : (PieceKind)(value - 1);
        }

        /// <summary>ピースが入れないマスか。盤面の外は入れない。</summary>
        public bool IsBlocked(int x, int y) => !IsInside(x, y) || _cells[Index(x, y)] != EmptyCell;

        public bool Fits(ActivePiece piece)
        {
            for (var i = 0; i < PieceShapes.CellCount; i++)
            {
                var cell = piece.CellAt(i);
                if (IsBlocked(cell.X, cell.Y)) { return false; }
            }

            return true;
        }

        /// <summary>1マスを書き換えた盤面を返す。</summary>
        public Board With(int x, int y, PieceKind? kind)
        {
            RequireInside(x, y);
            if (kind is PieceKind value && (value < PieceKind.I || value > PieceKind.L)) { throw new ArgumentOutOfRangeException(nameof(kind), kind, "未定義の種類です。"); }
            var cells = (byte[])_cells.Clone();
            cells[Index(x, y)] = kind.HasValue ? (byte)((int)kind.Value + 1) : EmptyCell;
            return new Board(cells);
        }

        /// <summary>ピースを確定した盤面を返す。収まらないピースは確定できない。</summary>
        public Board Lock(ActivePiece piece)
        {
            if (!Fits(piece)) { throw new InvalidOperationException("収まらないピースは確定できません: " + piece); }
            var cells = (byte[])_cells.Clone();
            for (var i = 0; i < PieceShapes.CellCount; i++)
            {
                var cell = piece.CellAt(i);
                cells[Index(cell.X, cell.Y)] = (byte)((int)piece.Kind + 1);
            }

            return new Board(cells);
        }

        /// <summary>揃っている段を下から順に返す。</summary>
        public IReadOnlyList<int> FullRows()
        {
            var rows = new List<int>();
            for (var y = 0; y < Height; y++)
            {
                if (IsRowFull(y)) { rows.Add(y); }
            }

            return rows.AsReadOnly();
        }

        /// <summary>揃っている段を消し、上の段をその分だけ下へ詰めた盤面を返す。</summary>
        public Board ClearRows(IReadOnlyList<int> rows)
        {
            if (rows == null) { throw new ArgumentNullException(nameof(rows)); }
            var removed = new bool[Height];
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row < 0 || row >= Height) { throw new ArgumentOutOfRangeException(nameof(rows), row, "盤面の外の段です。"); }
                if (removed[row] || !IsRowFull(row)) { throw new ArgumentException("揃っていない段、または重複した段です: " + row, nameof(rows)); }
                removed[row] = true;
            }

            var cells = new byte[_cells.Length];
            var write = 0;
            for (var y = 0; y < Height; y++)
            {
                if (removed[y]) { continue; }
                Array.Copy(_cells, Index(0, y), cells, Index(0, write), Width);
                write++;
            }

            return new Board(cells);
        }

        /// <summary>盤面の内容から決まる値（FNV-1a）。実行環境が違っても同じ値になる。</summary>
        public uint Fingerprint()
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var cell in _cells)
                {
                    hash ^= cell;
                    hash *= 16777619u;
                }

                return hash;
            }
        }

        public bool Equals(Board? other) => other != null && _cells.AsSpan().SequenceEqual(other._cells);
        public override bool Equals(object? obj) => Equals(obj as Board);
        public override int GetHashCode() => unchecked((int)Fingerprint());

        private static int Index(int x, int y) => (y * Width) + x;

        private static void RequireInside(int x, int y)
        {
            if (!IsInside(x, y)) { throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y})", "盤面の外です。"); }
        }

        private bool IsRowFull(int y)
        {
            for (var x = 0; x < Width; x++)
            {
                if (_cells[Index(x, y)] == EmptyCell) { return false; }
            }

            return true;
        }
    }
}
