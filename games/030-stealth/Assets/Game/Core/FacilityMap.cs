using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>施設のマスの種類。</summary>
    public enum TileKind
    {
        Floor,

        /// <summary>移動・視線・音を遮る。</summary>
        Wall,

        /// <summary>移動・視線を遮るが、音は通す。</summary>
        Crate,

        /// <summary>入口兼出口（床と同じく通れる）。</summary>
        Exit,

        /// <summary>目的物の置き場（床と同じく通れる）。</summary>
        Objective,
    }

    /// <summary>マスの位置（列・行。行 0 が最下段）。</summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public Cell(int column, int row)
        {
            Column = column;
            Row = row;
        }

        public int Column { get; }

        public int Row { get; }

        /// <summary>マスの中心の座標（単位）。</summary>
        public Vec2i Center => new((Column * Units.Tile) + (Units.Tile / 2), (Row * Units.Tile) + (Units.Tile / 2));

        /// <summary>点を含むマス（境界の点は右・上のマス）。</summary>
        public static Cell Of(Vec2i point) => new(Units.FloorDiv(point.X, Units.Tile), Units.FloorDiv(point.Y, Units.Tile));

        /// <summary>マス目の 4 近傍の距離（マンハッタン距離）。</summary>
        public static int Manhattan(Cell a, Cell b) => Math.Abs(a.Column - b.Column) + Math.Abs(a.Row - b.Row);

        public bool Equals(Cell other) => Column == other.Column && Row == other.Row;

        public override bool Equals(object? obj) => obj is Cell other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Column, Row);

        public override string ToString() => $"({Column}, {Row})";

        public static bool operator ==(Cell left, Cell right) => left.Equals(right);

        public static bool operator !=(Cell left, Cell right) => !left.Equals(right);
    }

    /// <summary>
    /// 施設の地形（変わらない）。列 0 が左端、行 0 が最下段。地図の外は壁。
    /// 移動の衝突解決（<see cref="ISolidGrid"/>）では壁と木箱が固体。
    /// </summary>
    public sealed class FacilityMap : ISolidGrid
    {
        private readonly TileKind[] _tiles;

        public FacilityMap(int width, int height, TileKind[] tiles)
        {
            if (tiles == null) { throw new ArgumentNullException(nameof(tiles)); }
            if (width <= 0) { throw new ArgumentOutOfRangeException(nameof(width), width, "幅は正の値にしてください。"); }
            if (height <= 0) { throw new ArgumentOutOfRangeException(nameof(height), height, "高さは正の値にしてください。"); }
            if (tiles.Length != width * height) { throw new ArgumentException($"タイル数 {tiles.Length} が {width}×{height} と一致しません。", nameof(tiles)); }
            foreach (var tile in tiles)
            {
                if (!Enum.IsDefined(typeof(TileKind), tile)) { throw new ArgumentException($"未定義のタイル {tile} があります。", nameof(tiles)); }
            }

            Width = width;
            Height = height;
            _tiles = (TileKind[])tiles.Clone();
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>マスの数。</summary>
        public int Count => Width * Height;

        public TileKind this[Cell cell] => this[cell.Column, cell.Row];

        public TileKind this[int column, int row] => Contains(column, row) ? _tiles[(row * Width) + column] : TileKind.Wall;

        public bool Contains(int column, int row) => column >= 0 && column < Width && row >= 0 && row < Height;

        public bool Contains(Cell cell) => Contains(cell.Column, cell.Row);

        /// <summary>マスの通し番号（行優先）。</summary>
        public int IndexOf(Cell cell) => (cell.Row * Width) + cell.Column;

        public Cell CellAt(int index) => new(index % Width, index / Width);

        /// <summary>歩けるマスか（壁・木箱以外）。</summary>
        public bool IsWalkable(Cell cell) => IsWalkable(this[cell]);

        public static bool IsWalkable(TileKind kind) => kind != TileKind.Wall && kind != TileKind.Crate;

        /// <summary>視線を遮るマスか（壁・木箱）。</summary>
        public bool BlocksSight(int column, int row)
        {
            var kind = this[column, row];
            return kind == TileKind.Wall || kind == TileKind.Crate;
        }

        /// <summary>音を遮るマスか（壁だけ。木箱は音を通す）。</summary>
        public bool BlocksSound(Cell cell) => this[cell] == TileKind.Wall;

        public bool IsSolid(int column, int row) => BlocksSight(column, row);

        /// <summary>
        /// 文字の地図から作る。行は上から下の順（最初の行が最上段）。
        /// <c>#</c> 壁、<c>X</c> 木箱、<c>.</c> 床、<c>E</c> 入口兼出口、<c>O</c> 目的物。
        /// </summary>
        public static FacilityMap Parse(params string[] rows)
        {
            if (rows == null) { throw new ArgumentNullException(nameof(rows)); }
            if (rows.Length == 0) { throw new ArgumentException("行がありません。", nameof(rows)); }
            var width = rows[0].Length;
            var height = rows.Length;
            var tiles = new TileKind[width * height];
            for (var line = 0; line < height; line++)
            {
                if (rows[line].Length != width) { throw new ArgumentException($"{line} 行目の幅 {rows[line].Length} が {width} と違います。", nameof(rows)); }
                var row = height - 1 - line;
                for (var column = 0; column < width; column++)
                {
                    tiles[(row * width) + column] = rows[line][column] switch
                    {
                        '#' => TileKind.Wall,
                        'X' => TileKind.Crate,
                        '.' => TileKind.Floor,
                        'E' => TileKind.Exit,
                        'O' => TileKind.Objective,
                        var c => throw new ArgumentException($"未知の文字 '{c}'（{line} 行目 {column} 列）。", nameof(rows)),
                    };
                }
            }

            return new FacilityMap(width, height, tiles);
        }

        /// <summary>その種類のマスを 1 つ探す（なければ例外）。</summary>
        public Cell Find(TileKind kind)
        {
            Cell? found = null;
            for (var i = 0; i < _tiles.Length; i++)
            {
                if (_tiles[i] != kind) { continue; }
                if (found != null) { throw new InvalidOperationException($"{kind} が 2 つ以上あります。"); }
                found = CellAt(i);
            }

            return found ?? throw new InvalidOperationException($"{kind} がありません。");
        }
    }
}
