using System;

namespace Game.Core
{
    /// <summary>6×6 の盤面。不変で、変更は新しい盤面を返す。y = 0 が最下段。</summary>
    public sealed class Board : IEquatable<Board>
    {
        public const int Width = 6;
        public const int Height = 6;
        public const int CellCount = Width * Height;

        private readonly Piece[] _cells;

        private Board(Piece[] cells) => _cells = cells;

        public static bool IsInside(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        public static bool IsInside(Point point) => IsInside(point.X, point.Y);

        public static int Index(int x, int y) => (y * Width) + x;

        /// <summary>マスの配列から盤面を作る。配列は複製する。</summary>
        public static Board FromCells(Piece[] cells)
        {
            if (cells == null) { throw new ArgumentNullException(nameof(cells)); }
            if (cells.Length != CellCount) { throw new ArgumentException($"マスの数は {CellCount} にしてください。", nameof(cells)); }
            foreach (var cell in cells) { RequireDefined(cell); }
            return new Board((Piece[])cells.Clone());
        }

        /// <summary>マスの配列の複製を返す。</summary>
        public Piece[] ToCells() => (Piece[])_cells.Clone();

        public Piece At(int x, int y)
        {
            RequireInside(x, y);
            return _cells[Index(x, y)];
        }

        public Piece At(Point point) => At(point.X, point.Y);

        /// <summary>空きマスがあるか。解決の途中でなければ空きはない。</summary>
        public bool HasEmpty()
        {
            foreach (var cell in _cells)
            {
                if (cell == Piece.Empty) { return true; }
            }

            return false;
        }

        /// <summary>2マスを入れ替えた盤面を返す。隣接や並びは問わない。</summary>
        public Board Swapped(Point a, Point b)
        {
            RequireInside(a.X, a.Y);
            RequireInside(b.X, b.Y);
            var cells = (Piece[])_cells.Clone();
            var ia = Index(a.X, a.Y);
            var ib = Index(b.X, b.Y);
            (cells[ia], cells[ib]) = (cells[ib], cells[ia]);
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

        private static void RequireDefined(Piece cell)
        {
            if (cell < Piece.Empty || cell > Piece.Violet) { throw new ArgumentOutOfRangeException(nameof(cell), cell, "未定義のマスです。"); }
        }
    }
}
