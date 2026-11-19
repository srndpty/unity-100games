using System;

namespace Game.Core
{
    /// <summary>確定した胞子だけを持つ盤面。不変で、変更は新しい盤面を返す。y = 0 が最下段。</summary>
    public sealed class Board : IEquatable<Board>
    {
        public const int Width = 6;
        public const int VisibleHeight = 12;
        public const int Height = 13;

        /// <summary>ペアの軸が出現する列。</summary>
        public const int SpawnX = 2;

        /// <summary>ペアの軸が出現する段（可視の最上段）。</summary>
        public const int SpawnY = VisibleHeight - 1;

        public static readonly Board Empty = new(new Cell[Width * Height]);

        private readonly Cell[] _cells;

        private Board(Cell[] cells) => _cells = cells;

        public static bool IsInside(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        public static int Index(int x, int y) => (y * Width) + x;

        /// <summary>マスの配列から盤面を作る。配列は複製する。</summary>
        public static Board FromCells(Cell[] cells)
        {
            if (cells == null) { throw new ArgumentNullException(nameof(cells)); }
            if (cells.Length != Width * Height) { throw new ArgumentException($"マスの数は {Width * Height} にしてください。", nameof(cells)); }
            foreach (var cell in cells) { RequireDefined(cell); }
            return new Board((Cell[])cells.Clone());
        }

        /// <summary>マスの配列の複製を返す。</summary>
        public Cell[] ToCells() => (Cell[])_cells.Clone();

        public Cell At(int x, int y)
        {
            RequireInside(x, y);
            return _cells[Index(x, y)];
        }

        /// <summary>ペアが入れないマスか。盤面の外は入れない。</summary>
        public bool IsBlocked(int x, int y) => !IsInside(x, y) || _cells[Index(x, y)] != Cell.Empty;

        public bool Fits(ActivePair pair) => !IsBlocked(pair.X, pair.Y) && !IsBlocked(pair.ChildX, pair.ChildY);

        /// <summary>1マスを書き換えた盤面を返す。</summary>
        public Board With(int x, int y, Cell cell)
        {
            RequireInside(x, y);
            RequireDefined(cell);
            var cells = (Cell[])_cells.Clone();
            cells[Index(x, y)] = cell;
            return new Board(cells);
        }

        /// <summary>ペアを置いた盤面を返す。収まらないペアは置けない。</summary>
        public Board Place(ActivePair pair)
        {
            if (!Fits(pair)) { throw new InvalidOperationException("収まらないペアは置けません。"); }
            var cells = (Cell[])_cells.Clone();
            cells[Index(pair.X, pair.Y)] = pair.Pair.Pivot;
            cells[Index(pair.ChildX, pair.ChildY)] = pair.Pair.Child;
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
                    hash ^= (byte)cell;
                    hash *= 16777619u;
                }

                return hash;
            }
        }

        public bool Equals(Board? other)
        {
            if (other is null) { return false; }
            for (var i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] != other._cells[i]) { return false; }
            }

            return true;
        }

        public override bool Equals(object? obj) => Equals(obj as Board);
        public override int GetHashCode() => unchecked((int)Fingerprint());

        private static void RequireInside(int x, int y)
        {
            if (!IsInside(x, y)) { throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y})", "盤面の外です。"); }
        }

        private static void RequireDefined(Cell cell)
        {
            if (cell < Cell.Empty || cell > Cell.Ash) { throw new ArgumentOutOfRangeException(nameof(cell), cell, "未定義のマスです。"); }
        }
    }
}
