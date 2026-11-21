using System;

namespace Game.Core
{
    public enum TileKind
    {
        Empty,
        Solid,

        /// <summary>ゴールの旗の柱。通り抜けられ、触れると面クリア。</summary>
        Goal,
    }

    /// <summary>
    /// 面の地形。列 0 が左端、行 0 が最下段。
    /// 左右の外は壁（固体）、上と下の外は空。下の外へ落ちたら穴に落ちたことになる。
    /// </summary>
    public sealed class TileMap
    {
        private readonly TileKind[] _tiles;

        public TileMap(int width, int height, TileKind[] tiles)
        {
            if (tiles == null) { throw new ArgumentNullException(nameof(tiles)); }
            if (width <= 0) { throw new ArgumentOutOfRangeException(nameof(width), width, "幅は正の値にしてください。"); }
            if (height <= 0) { throw new ArgumentOutOfRangeException(nameof(height), height, "高さは正の値にしてください。"); }
            if (tiles.Length != width * height)
            {
                throw new ArgumentException($"タイル数 {tiles.Length} が {width}×{height} と一致しません。", nameof(tiles));
            }

            foreach (var tile in tiles)
            {
                if (!Enum.IsDefined(typeof(TileKind), tile))
                {
                    throw new ArgumentException($"未定義のタイル {tile} があります。", nameof(tiles));
                }
            }

            Width = width;
            Height = height;
            _tiles = (TileKind[])tiles.Clone();
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>面の幅（単位）。</summary>
        public int PixelWidth => Width * Units.Tile;

        public TileKind this[int column, int row]
        {
            get
            {
                if (column < 0 || column >= Width) { return TileKind.Solid; }
                if (row < 0 || row >= Height) { return TileKind.Empty; }
                return _tiles[(row * Width) + column];
            }
        }

        public bool IsSolid(int column, int row) => this[column, row] == TileKind.Solid;

        /// <summary>箱が重なる行の中に、列 <paramref name="column"/> の固体があるか。</summary>
        public bool AnySolidInColumn(int column, Box box)
        {
            var from = Units.FloorDiv(box.Bottom, Units.Tile);
            var to = Units.FloorDiv(box.Top - 1, Units.Tile);
            for (var row = from; row <= to; row++)
            {
                if (IsSolid(column, row)) { return true; }
            }

            return false;
        }

        /// <summary>箱が重なる列の中に、行 <paramref name="row"/> の固体があるか。</summary>
        public bool AnySolidInRow(int row, Box box)
        {
            var from = Units.FloorDiv(box.Left, Units.Tile);
            var to = Units.FloorDiv(box.Right - 1, Units.Tile);
            for (var column = from; column <= to; column++)
            {
                if (IsSolid(column, row)) { return true; }
            }

            return false;
        }

        /// <summary>箱が重なるタイルに <paramref name="kind"/> があるか。</summary>
        public bool Touches(Box box, TileKind kind)
        {
            var fromColumn = Units.FloorDiv(box.Left, Units.Tile);
            var toColumn = Units.FloorDiv(box.Right - 1, Units.Tile);
            var fromRow = Units.FloorDiv(box.Bottom, Units.Tile);
            var toRow = Units.FloorDiv(box.Top - 1, Units.Tile);
            for (var row = fromRow; row <= toRow; row++)
            {
                for (var column = fromColumn; column <= toColumn; column++)
                {
                    if (this[column, row] == kind) { return true; }
                }
            }

            return false;
        }
    }
}
