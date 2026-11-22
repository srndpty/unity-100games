using System;
using TileMotion;

namespace Game.Core
{
    public enum TileKind
    {
        Empty,
        Solid,

        /// <summary>ひびの壁。壊されるまでは固体。破壊ショットで 1 タイルずつ壊れる。</summary>
        Cracked,

        /// <summary>扉。通り抜けられ、触れると隣の部屋へ移る。部屋の端にだけ置ける。</summary>
        Door,
    }

    /// <summary>
    /// 部屋の地形のデータ（変わらない）。列 0 が左端、行 0 が最下段。
    /// 左右の外は壁（固体）、上と下の外は空。ひびの壁が壊れているかは <see cref="Terrain"/> が持つ。
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

        public TileKind this[int column, int row]
        {
            get
            {
                if (column < 0 || column >= Width) { return TileKind.Solid; }
                if (row < 0 || row >= Height) { return TileKind.Empty; }
                return _tiles[Index(column, row)];
            }
        }

        public bool Contains(int column, int row) => column >= 0 && column < Width && row >= 0 && row < Height;

        public int Index(int column, int row) => (row * Width) + column;

        /// <summary>箱が重なるタイルに <paramref name="kind"/> があるか（タイル全体で判定する）。</summary>
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

    /// <summary>
    /// 今いる部屋の地形。部屋のデータに、壊したひびの壁（世界の状態の一部）を重ねたもの。
    /// 衝突解決（<see cref="BodyMotion"/>）はこの地形で行う。
    /// </summary>
    public sealed class Terrain : ISolidGrid
    {
        private readonly bool[] _broken;

        public Terrain(TileMap map, bool[] broken)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            _broken = broken ?? throw new ArgumentNullException(nameof(broken));
            if (broken.Length != map.Width * map.Height)
            {
                throw new ArgumentException("壊れた壁の配列の大きさが地形と一致しません。", nameof(broken));
            }
        }

        public TileMap Map { get; }

        public bool IsSolid(int column, int row)
        {
            var kind = Map[column, row];
            return kind == TileKind.Solid || (kind == TileKind.Cracked && !_broken[Map.Index(column, row)]);
        }

        public bool IsBroken(int column, int row) => Map.Contains(column, row) && _broken[Map.Index(column, row)];

        /// <summary>
        /// 列 <paramref name="column"/> のうち箱が重なる行にある、壊れていないひびの壁を壊す。壊した数を返す。
        /// </summary>
        public int BreakInColumn(int column, Box box)
        {
            var broken = 0;
            var from = Units.FloorDiv(box.Bottom, Units.Tile);
            var to = Units.FloorDiv(box.Top - 1, Units.Tile);
            for (var row = from; row <= to; row++)
            {
                if (Map[column, row] == TileKind.Cracked && !_broken[Map.Index(column, row)])
                {
                    _broken[Map.Index(column, row)] = true;
                    broken++;
                }
            }

            return broken;
        }
    }
}
