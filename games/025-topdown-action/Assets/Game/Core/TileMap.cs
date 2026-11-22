using System;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 部屋の地形のデータ（変わらない）。列 0 が左端、行 0 が最下段。部屋の外はすべて壁。
    /// 扉が開いているかは <see cref="Terrain"/> が持つ。
    /// </summary>
    public sealed class TileMap
    {
        private readonly TileKind[] _tiles;

        public TileMap(int width, int height, TileKind[] tiles)
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

        public TileKind this[int column, int row] => Contains(column, row) ? _tiles[(row * Width) + column] : TileKind.Wall;

        public bool Contains(int column, int row) => column >= 0 && column < Width && row >= 0 && row < Height;

        /// <summary>出入口・小扉・ボス扉のどれか。</summary>
        public static bool IsDoorTile(TileKind kind) => kind == TileKind.Doorway || kind == TileKind.SmallDoor || kind == TileKind.BossDoor;

        /// <summary>端のマスがどの辺の出入口か（出入口の種類でなければ null）。</summary>
        public Side? DoorSideOf(int column, int row)
        {
            if (!IsDoorTile(this[column, row])) { return null; }
            if (column == 0) { return Side.Left; }
            if (column == Width - 1) { return Side.Right; }
            return row == 0 ? Side.Bottom : Side.Top;
        }

        /// <summary>箱が床以外のタイルに重なるか。</summary>
        public bool TouchesNonFloor(Box box)
        {
            var (fromColumn, toColumn, fromRow, toRow) = Range(box);
            for (var row = fromRow; row <= toRow; row++)
            {
                for (var column = fromColumn; column <= toColumn; column++)
                {
                    if (this[column, row] != TileKind.Floor) { return true; }
                }
            }

            return false;
        }

        /// <summary>箱が重なるマスの範囲（両端を含む）。</summary>
        public static (int FromColumn, int ToColumn, int FromRow, int ToRow) Range(Box box)
        {
            return (Units.FloorDiv(box.Left, Units.Tile), Units.FloorDiv(box.Right - 1, Units.Tile), Units.FloorDiv(box.Bottom, Units.Tile), Units.FloorDiv(box.Top - 1, Units.Tile));
        }
    }

    /// <summary>
    /// 今いる部屋の地形。部屋のデータに、開いた扉（ダンジョンの状態の一部）を重ねたもの。衝突解決はこの地形で行う。
    /// 壁・宝箱・閉じた扉が固体。
    /// </summary>
    public sealed class Terrain : ISolidGrid
    {
        private readonly bool[] _open;

        /// <param name="map">部屋の地形。</param>
        /// <param name="open">辺ごとに扉が開いているか（大きさ 4。<see cref="Side"/> の順）。共有して、開けたら書き換える。</param>
        public Terrain(TileMap map, bool[] open)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            _open = open ?? throw new ArgumentNullException(nameof(open));
            if (open.Length != 4) { throw new ArgumentException("辺は 4 つです。", nameof(open)); }
        }

        public TileMap Map { get; }

        public bool IsOpen(Side side) => _open[(int)side];

        public bool IsSolid(int column, int row)
        {
            var kind = Map[column, row];
            return kind switch
            {
                TileKind.Wall => true,
                TileKind.Chest => true,
                TileKind.SmallDoor => !_open[(int)Map.DoorSideOf(column, row)!.Value],
                TileKind.BossDoor => !_open[(int)Map.DoorSideOf(column, row)!.Value],
                _ => false,
            };
        }

        /// <summary>通れる出入口のマスか（出入口、または開いた扉）。</summary>
        public bool IsPassage(int column, int row)
        {
            var kind = Map[column, row];
            return kind == TileKind.Doorway || (TileMap.IsDoorTile(kind) && _open[(int)Map.DoorSideOf(column, row)!.Value]);
        }

        /// <summary>箱が重なる、通れる出入口の辺。重ならなければ null。</summary>
        public Side? PassageUnder(Box box)
        {
            var (fromColumn, toColumn, fromRow, toRow) = TileMap.Range(box);
            for (var row = fromRow; row <= toRow; row++)
            {
                for (var column = fromColumn; column <= toColumn; column++)
                {
                    if (IsPassage(column, row)) { return Map.DoorSideOf(column, row); }
                }
            }

            return null;
        }

        /// <summary>箱が重なる、閉じた扉の辺。重ならなければ null。</summary>
        public Side? ClosedDoorUnder(Box box)
        {
            var (fromColumn, toColumn, fromRow, toRow) = TileMap.Range(box);
            for (var row = fromRow; row <= toRow; row++)
            {
                for (var column = fromColumn; column <= toColumn; column++)
                {
                    var kind = Map[column, row];
                    if ((kind == TileKind.SmallDoor || kind == TileKind.BossDoor) && !IsPassage(column, row)) { return Map.DoorSideOf(column, row); }
                }
            }

            return null;
        }
    }
}
