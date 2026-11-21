using System;

namespace Game.Core
{
    public enum Cell : byte
    {
        /// <summary>まだ占有していない海。敵が動ける。</summary>
        Open,

        /// <summary>占有済みの陸。最初の外周 1 セルもこれ。</summary>
        Claimed,

        /// <summary>描いている途中の線。閉じると陸に、ミスすると海に戻る。</summary>
        Trail,
    }

    /// <summary>
    /// 占有マスク。セル (x, y) は x が右、y が上に増える。外周 1 セルは最初から陸。
    /// Open と Trail の数は書き換えのたびに数え直さず、差分で保つ。
    /// </summary>
    public sealed class CellGrid
    {
        public const int MinSize = 5;
        public const int MaxSize = 256;

        private readonly Cell[] _cells;

        public CellGrid(int width, int height)
        {
            Width = RequireSize(width, nameof(width));
            Height = RequireSize(height, nameof(height));
            _cells = new Cell[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                    _cells[Index(x, y)] = edge ? Cell.Claimed : Cell.Open;
                }
            }

            OpenCount = (width - 2) * (height - 2);
        }

        public int Width { get; }

        public int Height { get; }

        public int CellCount => _cells.Length;

        public int OpenCount { get; private set; }

        public int TrailCount { get; private set; }

        /// <summary>中身が変わるたびに増える番号。描画が変わったときだけ書き直すために使う。</summary>
        public int Version { get; private set; }

        public Cell this[int x, int y] => _cells[Index(x, y)];

        public Cell this[int index] => _cells[index];

        public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public int Index(int x, int y)
        {
            if (!Contains(x, y))
            {
                throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) は盤の外です。");
            }

            return y * Width + x;
        }

        public void Set(int x, int y, Cell value) => Set(Index(x, y), value);

        public void Set(int index, Cell value)
        {
            if (!Enum.IsDefined(typeof(Cell), value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "未定義のセルです。");
            }

            var old = _cells[index];
            if (old == value)
            {
                return;
            }

            Count(old, -1);
            Count(value, 1);
            _cells[index] = value;
            Version = unchecked(Version + 1);
        }

        /// <summary>
        /// 自機が線を引かずに歩ける縁。陸のうち、8 近傍のどこかに海があるセル。
        /// 海を囲む陸は、この定義なら 4 方向の移動でひと続きにたどれる。
        /// </summary>
        public bool IsBoundary(int x, int y)
        {
            if (this[x, y] != Cell.Claimed)
            {
                return false;
            }

            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var nx = x + dx;
                    var ny = y + dy;
                    if (Contains(nx, ny) && this[nx, ny] == Cell.Open)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>盤の内容のハッシュ（FNV-1a）。</summary>
        public uint Hash(uint hash = 2166136261u)
        {
            unchecked
            {
                foreach (var cell in _cells)
                {
                    hash = (hash ^ (byte)cell) * 16777619u;
                }
            }

            return hash;
        }

        private static int RequireSize(int size, string name)
        {
            if (size < MinSize || size > MaxSize)
            {
                throw new ArgumentOutOfRangeException(name, size, $"{MinSize}〜{MaxSize} の範囲で指定してください。");
            }

            return size;
        }

        private void Count(Cell cell, int delta)
        {
            if (cell == Cell.Open)
            {
                OpenCount += delta;
            }
            else if (cell == Cell.Trail)
            {
                TrailCount += delta;
            }
        }
    }
}
