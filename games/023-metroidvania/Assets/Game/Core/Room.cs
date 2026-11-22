using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TileMotion;

namespace Game.Core
{
    /// <summary>部屋の辺。扉はどの辺にも高々 1 か所。</summary>
    public enum Side
    {
        Left,
        Right,
        Top,
        Bottom,
    }

    public enum PickupKind
    {
        /// <summary>回復の実。体力が戻る。</summary>
        Fruit,

        /// <summary>二段ジャンプの能力。</summary>
        DoubleJump,

        /// <summary>破壊ショットの能力。</summary>
        BreakShot,
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

        public bool Equals(Cell other) => Column == other.Column && Row == other.Row;

        public override bool Equals(object? obj) => obj is Cell other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Column, Row);

        public override string ToString() => $"({Column}, {Row})";

        public static bool operator ==(Cell left, Cell right) => left.Equals(right);

        public static bool operator !=(Cell left, Cell right) => !left.Equals(right);
    }

    /// <summary>部屋に置かれたアイテム。取ったかどうかは世界の状態が持つ。</summary>
    public readonly struct RoomPickup
    {
        public RoomPickup(PickupKind kind, Cell cell)
        {
            Kind = kind;
            Cell = cell;
        }

        public PickupKind Kind { get; }

        public Cell Cell { get; }

        /// <summary>取るときの当たり（マスの中央の 10×10 px）。</summary>
        public Box Box => new((Cell.Column * Units.Tile) + (3 * Units.Pixel), (Cell.Row * Units.Tile) + (3 * Units.Pixel), 10 * Units.Pixel, 10 * Units.Pixel);
    }

    /// <summary>
    /// 1 部屋（1 画面）のデータ。文字の地図から作る。行は上から下の順に書く（最後の行が最下段）。
    /// <list type="bullet">
    /// <item><c>.</c> 空、<c>#</c> 固体、<c>%</c> ひびの壁、<c>D</c> 扉（部屋の端だけ。角は不可）</item>
    /// <item><c>l</c> <c>r</c> <c>t</c> <c>b</c>：左・右・上・下の扉から入ったときに立つ位置（扉のある辺には必ず 1 つ）</item>
    /// <item><c>S</c> 開始位置、<c>$</c> セーブ像、<c>+</c> 回復の実、<c>J</c> 二段ジャンプ、<c>K</c> 破壊ショット、<c>w</c> 歩く敵、<c>M</c> ボス</item>
    /// </list>
    /// 記号のマスは空として扱う。開始位置と到着位置は真下が固体で、そこに立った自機が固体・扉に重ならないこと。
    /// </summary>
    public sealed class Room
    {
        /// <summary>部屋の幅（タイル）。1 画面に収まる大きさで固定する。</summary>
        public const int Columns = 40;

        /// <summary>部屋の高さ（タイル）。</summary>
        public const int Rows = 22;

        private readonly Cell?[] _arrivals;
        private readonly bool[] _doors;

        private Room(string name, int gridX, int gridY, TileMap map, Cell? start, Cell?[] arrivals, bool[] doors, List<Cell> statues, List<RoomPickup> pickups, List<Cell> walkers, Cell? boss)
        {
            Name = name;
            GridX = gridX;
            GridY = gridY;
            Map = map;
            Start = start;
            _arrivals = arrivals;
            _doors = doors;
            Statues = statues.AsReadOnly();
            Pickups = pickups.AsReadOnly();
            Walkers = walkers.AsReadOnly();
            Boss = boss;
        }

        public string Name { get; }

        /// <summary>地図の格子での位置（右が +x、上が +y）。隣の部屋はこの格子で決まる。</summary>
        public int GridX { get; }

        public int GridY { get; }

        public TileMap Map { get; }

        /// <summary>試合の開始位置（開始の部屋だけ）。</summary>
        public Cell? Start { get; }

        public ReadOnlyCollection<Cell> Statues { get; }

        public ReadOnlyCollection<RoomPickup> Pickups { get; }

        public ReadOnlyCollection<Cell> Walkers { get; }

        public Cell? Boss { get; }

        public bool HasDoor(Side side) => _doors[(int)side];

        /// <summary><paramref name="side"/> の扉から入ったときに立つマス。</summary>
        public Cell Arrival(Side side)
        {
            return _arrivals[(int)side] ?? throw new InvalidOperationException($"{Name} の {side} には扉がありません。");
        }

        /// <summary>40×22 の部屋を解析する。</summary>
        public static Room Parse(string name, int gridX, int gridY, IReadOnlyList<string> rows) => Parse(name, gridX, gridY, rows, Columns, Rows);

        /// <summary>大きさを指定して解析する（テスト用の小さい部屋）。</summary>
        public static Room Parse(string name, int gridX, int gridY, IReadOnlyList<string> rows, int columns, int height)
        {
            if (name == null) { throw new ArgumentNullException(nameof(name)); }
            if (rows == null) { throw new ArgumentNullException(nameof(rows)); }
            if (columns < 2 || columns > Columns || height < 2 || height > Rows)
            {
                throw new ArgumentOutOfRangeException(nameof(columns), $"大きさは 2×2〜{Columns}×{Rows} にしてください（{columns}×{height}）。");
            }

            if (rows.Count != height)
            {
                throw new ArgumentException($"行数が {height} ではありません（{rows.Count}）。", nameof(rows));
            }

            var tiles = new TileKind[columns * height];
            Cell? start = null;
            var arrivals = new Cell?[4];
            var doors = new bool[4];
            var statues = new List<Cell>();
            var pickups = new List<RoomPickup>();
            var walkers = new List<Cell>();
            Cell? boss = null;
            for (var line = 0; line < height; line++)
            {
                var text = rows[line];
                if (text == null || text.Length != columns)
                {
                    throw new ArgumentException($"{line + 1} 行目の幅が {columns} ではありません。", nameof(rows));
                }

                var row = height - 1 - line;
                for (var column = 0; column < columns; column++)
                {
                    var cell = new Cell(column, row);
                    var kind = TileKind.Empty;
                    switch (text[column])
                    {
                        case '.': break;
                        case '#': kind = TileKind.Solid; break;
                        case '%': kind = TileKind.Cracked; break;
                        case 'D':
                            kind = TileKind.Door;
                            doors[(int)DoorSide(column, row, columns, height)] = true;
                            break;
                        case 'l': SetOnce(ref arrivals[(int)Side.Left], cell, "到着位置 l"); break;
                        case 'r': SetOnce(ref arrivals[(int)Side.Right], cell, "到着位置 r"); break;
                        case 't': SetOnce(ref arrivals[(int)Side.Top], cell, "到着位置 t"); break;
                        case 'b': SetOnce(ref arrivals[(int)Side.Bottom], cell, "到着位置 b"); break;
                        case 'S': SetOnce(ref start, cell, "開始位置 S"); break;
                        case 'M': SetOnce(ref boss, cell, "ボス M"); break;
                        case '$': statues.Add(cell); break;
                        case '+': pickups.Add(new RoomPickup(PickupKind.Fruit, cell)); break;
                        case 'J': pickups.Add(new RoomPickup(PickupKind.DoubleJump, cell)); break;
                        case 'K': pickups.Add(new RoomPickup(PickupKind.BreakShot, cell)); break;
                        case 'w': walkers.Add(cell); break;
                        default:
                            throw new ArgumentException($"{line + 1} 行目 {column + 1} 文字目の '{text[column]}' は使えません。", nameof(rows));
                    }

                    tiles[(row * columns) + column] = kind;
                }
            }

            var map = new TileMap(columns, height, tiles);
            for (var side = 0; side < 4; side++)
            {
                if (doors[side] != arrivals[side].HasValue)
                {
                    throw new ArgumentException($"{(Side)side} の扉と到着位置は組で置いてください。", nameof(rows));
                }

                if (arrivals[side] is Cell arrival) { RequireStanding(map, arrival, "到着位置"); }
            }

            if (start is Cell s) { RequireStanding(map, s, "開始位置"); }
            return new Room(name, gridX, gridY, map, start, arrivals, doors, statues, pickups, walkers, boss);
        }

        /// <summary>マス (column, row) の下端の中央に、幅 width・高さ height の体を置いた箱。</summary>
        public static Box PlaceOnTile(Cell cell, int width, int height)
        {
            return new Box((cell.Column * Units.Tile) + ((Units.Tile - width) / 2), cell.Row * Units.Tile, width, height);
        }

        /// <summary>セーブ像の当たり（マス全体）。</summary>
        public static Box StatueBox(Cell cell) => new(cell.Column * Units.Tile, cell.Row * Units.Tile, Units.Tile, Units.Tile);

        private static Side DoorSide(int column, int row, int columns, int height)
        {
            var left = column == 0;
            var right = column == columns - 1;
            var bottom = row == 0;
            var top = row == height - 1;
            if ((left || right) && (top || bottom))
            {
                throw new ArgumentException($"扉 ({column}, {row}) を角には置けません。");
            }

            if (left) { return Side.Left; }
            if (right) { return Side.Right; }
            if (top) { return Side.Top; }
            if (bottom) { return Side.Bottom; }
            throw new ArgumentException($"扉 ({column}, {row}) は部屋の端に置いてください。");
        }

        private static void SetOnce(ref Cell? slot, Cell cell, string what)
        {
            if (slot.HasValue) { throw new ArgumentException($"{what} が 2 つ以上あります。"); }
            slot = cell;
        }

        private static void RequireStanding(TileMap map, Cell cell, string what)
        {
            if (map[cell.Column, cell.Row - 1] != TileKind.Solid)
            {
                throw new ArgumentException($"{what} {cell} の真下が固体ではありません。");
            }

            var box = PlaceOnTile(cell, Player.Width, Player.Height);
            if (map.Touches(box, TileKind.Solid) || map.Touches(box, TileKind.Cracked) || map.Touches(box, TileKind.Door))
            {
                throw new ArgumentException($"{what} {cell} に立つ自機が固体・ひびの壁・扉に重なります。");
            }
        }
    }
}
