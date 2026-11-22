using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TileMotion;

namespace Game.Core
{
    public enum TileKind
    {
        Floor,
        Wall,

        /// <summary>開いた出入口（部屋の端）。重なると隣の部屋へ移る。</summary>
        Doorway,

        /// <summary>小扉（部屋の端）。開くまでは固体。小鍵を 1 つ使って開く。</summary>
        SmallDoor,

        /// <summary>ボス扉（部屋の端）。開くまでは固体。ボス鍵を持っていれば開く。</summary>
        BossDoor,

        /// <summary>宝箱。開けても固体のまま。</summary>
        Chest,
    }

    /// <summary>部屋の辺。出入口はどの辺にも高々 1 か所。</summary>
    public enum Side
    {
        Left,
        Right,
        Top,
        Bottom,
    }

    /// <summary>辺の出入口の種類。</summary>
    public enum DoorKind
    {
        None,
        Open,
        Small,
        Boss,
    }

    public enum ChestContent
    {
        SmallKey,
        BossKey,
    }

    public enum EnemyKind
    {
        /// <summary>乱数で歩き回る。</summary>
        Slime,

        /// <summary>その場に立ち、縦横に並ぶと弾を撃つ。</summary>
        Spitter,

        /// <summary>縦横に並んで視線が通ると、構えてから壁まで突進する。</summary>
        Charger,

        Boss,
    }

    /// <summary>部屋に置かれた宝箱。開いたかどうかはダンジョンの状態が持つ。</summary>
    public readonly struct RoomChest
    {
        public RoomChest(ChestContent content, Cell cell)
        {
            Content = content;
            Cell = cell;
        }

        public ChestContent Content { get; }

        public Cell Cell { get; }
    }

    /// <summary>敵の出現位置。部屋に入るたびにここから始まる。</summary>
    public readonly struct Spawn
    {
        public Spawn(EnemyKind kind, Cell cell)
        {
            Kind = kind;
            Cell = cell;
        }

        public EnemyKind Kind { get; }

        public Cell Cell { get; }
    }

    /// <summary>
    /// 1 部屋（1 画面）のデータ。文字の地図から作る。行は上から下の順に書く（最後の行が最下段）。
    /// <list type="bullet">
    /// <item><c>.</c> 床、<c>#</c> 壁、<c>D</c> 出入口、<c>L</c> 小扉、<c>B</c> ボス扉（この 3 つは部屋の端だけ。角は不可。1 つの辺は同じ種類でそろえる）</item>
    /// <item><c>k</c> 小鍵の宝箱、<c>K</c> ボス鍵の宝箱</item>
    /// <item><c>S</c> 開始位置、<c>R</c> 全滅で小鍵が出る場所、<c>s</c> スライム、<c>a</c> 射手、<c>c</c> 突進兵、<c>M</c> ボス（倒すと同じ場所に秘宝が出る）</item>
    /// </list>
    /// 記号のマスは床として扱う。辺の出入口から入ると、出入口の 1 マス内側の中央に立つ。
    /// </summary>
    public sealed class Room
    {
        /// <summary>部屋の幅（タイル）。1 画面に収まる大きさで固定する。</summary>
        public const int Columns = 21;

        /// <summary>部屋の高さ（タイル）。</summary>
        public const int Rows = 11;

        private readonly DoorKind[] _doors;
        private readonly Box[] _arrivals;

        private Room(string name, int gridX, int gridY, TileMap map, DoorKind[] doors, Box[] arrivals, Cell? start, Cell? reward, List<RoomChest> chests, List<Spawn> spawns)
        {
            Name = name;
            GridX = gridX;
            GridY = gridY;
            Map = map;
            _doors = doors;
            _arrivals = arrivals;
            Start = start;
            Reward = reward;
            Chests = chests.AsReadOnly();
            Spawns = spawns.AsReadOnly();
            foreach (var spawn in spawns)
            {
                if (spawn.Kind == EnemyKind.Boss) { Boss = spawn.Cell; }
            }
        }

        public string Name { get; }

        /// <summary>地図の格子での位置（右が +x、上が +y）。隣の部屋はこの格子で決まる。</summary>
        public int GridX { get; }

        public int GridY { get; }

        public TileMap Map { get; }

        /// <summary>試合の開始位置（開始の部屋だけ）。</summary>
        public Cell? Start { get; }

        /// <summary>敵を全滅させると小鍵が出る場所（ある部屋だけ）。</summary>
        public Cell? Reward { get; }

        public ReadOnlyCollection<RoomChest> Chests { get; }

        public ReadOnlyCollection<Spawn> Spawns { get; }

        /// <summary>ボスの出現位置。倒した後は秘宝がここに出る。</summary>
        public Cell? Boss { get; }

        public DoorKind Door(Side side) => _doors[(int)side];

        /// <summary><paramref name="side"/> の出入口から入ったときの自機の箱。</summary>
        public Box Arrival(Side side)
        {
            if (_doors[(int)side] == DoorKind.None) { throw new InvalidOperationException($"{Name} の {side} には出入口がありません。"); }
            return _arrivals[(int)side];
        }

        /// <summary>マス <paramref name="cell"/> にある宝箱の、部屋の中の番号。なければ -1。</summary>
        public int ChestAt(Cell cell)
        {
            for (var i = 0; i < Chests.Count; i++)
            {
                if (Chests[i].Cell == cell) { return i; }
            }

            return -1;
        }

        /// <summary>全滅の小鍵・秘宝を取るときの当たり（マスの中央の 10×10 px）。</summary>
        public static Box ItemBox(Cell cell) => cell.Centered(10 * Units.Pixel);

        /// <summary>21×11 の部屋を解析する。</summary>
        public static Room Parse(string name, int gridX, int gridY, IReadOnlyList<string> rows) => Parse(name, gridX, gridY, rows, Columns, Rows);

        /// <summary>大きさを指定して解析する（テスト用の小さい部屋）。</summary>
        public static Room Parse(string name, int gridX, int gridY, IReadOnlyList<string> rows, int columns, int height)
        {
            if (name == null) { throw new ArgumentNullException(nameof(name)); }
            if (rows == null) { throw new ArgumentNullException(nameof(rows)); }
            if (columns < 3 || columns > Columns || height < 3 || height > Rows)
            {
                throw new ArgumentOutOfRangeException(nameof(columns), $"大きさは 3×3〜{Columns}×{Rows} にしてください（{columns}×{height}）。");
            }

            if (rows.Count != height) { throw new ArgumentException($"行数が {height} ではありません（{rows.Count}）。", nameof(rows)); }

            var tiles = new TileKind[columns * height];
            var doors = new DoorKind[4];
            Cell? start = null;
            Cell? reward = null;
            var chests = new List<RoomChest>();
            var spawns = new List<Spawn>();
            var bosses = 0;
            for (var line = 0; line < height; line++)
            {
                var text = rows[line];
                if (text == null || text.Length != columns) { throw new ArgumentException($"{line + 1} 行目の幅が {columns} ではありません。", nameof(rows)); }
                var row = height - 1 - line;
                for (var column = 0; column < columns; column++)
                {
                    var cell = new Cell(column, row);
                    var kind = TileKind.Floor;
                    switch (text[column])
                    {
                        case '.': break;
                        case '#': kind = TileKind.Wall; break;
                        case 'D': kind = Edge(TileKind.Doorway, DoorKind.Open, cell, columns, height, doors); break;
                        case 'L': kind = Edge(TileKind.SmallDoor, DoorKind.Small, cell, columns, height, doors); break;
                        case 'B': kind = Edge(TileKind.BossDoor, DoorKind.Boss, cell, columns, height, doors); break;
                        case 'k': kind = TileKind.Chest; chests.Add(new RoomChest(ChestContent.SmallKey, cell)); break;
                        case 'K': kind = TileKind.Chest; chests.Add(new RoomChest(ChestContent.BossKey, cell)); break;
                        case 'S': SetOnce(ref start, cell, "開始位置 S"); break;
                        case 'R': SetOnce(ref reward, cell, "全滅の小鍵 R"); break;
                        case 's': spawns.Add(new Spawn(EnemyKind.Slime, cell)); break;
                        case 'a': spawns.Add(new Spawn(EnemyKind.Spitter, cell)); break;
                        case 'c': spawns.Add(new Spawn(EnemyKind.Charger, cell)); break;
                        case 'M': spawns.Add(new Spawn(EnemyKind.Boss, cell)); bosses++; break;
                        default: throw new ArgumentException($"{line + 1} 行目 {column + 1} 文字目の '{text[column]}' は使えません。", nameof(rows));
                    }

                    tiles[(row * columns) + column] = kind;
                }
            }

            if (bosses > 1) { throw new ArgumentException("ボス M は部屋に 1 つまでです。", nameof(rows)); }
            var map = new TileMap(columns, height, tiles);
            var arrivals = new Box[4];
            for (var side = 0; side < 4; side++)
            {
                if (doors[side] == DoorKind.None) { continue; }
                arrivals[side] = ArrivalBox(map, (Side)side);
                RequireClear(map, arrivals[side], $"{(Side)side} の出入口の到着位置");
            }

            if (start is Cell s) { RequireClear(map, s.Centered(Hero.Size), "開始位置"); }
            foreach (var spawn in spawns) { RequireClear(map, spawn.Cell.Centered(Enemy.SizeOf(spawn.Kind)), $"敵 {spawn.Kind} {spawn.Cell}"); }
            return new Room(name, gridX, gridY, map, doors, arrivals, start, reward, chests, spawns);
        }

        /// <summary>マスが端のどの辺にあるか。端でなければ null。角は例外。</summary>
        public static Side? EdgeSide(int column, int row, int columns, int height)
        {
            var left = column == 0;
            var right = column == columns - 1;
            var bottom = row == 0;
            var top = row == height - 1;
            if ((left || right) && (top || bottom)) { throw new ArgumentException($"出入口 ({column}, {row}) を角には置けません。"); }
            if (left) { return Side.Left; }
            if (right) { return Side.Right; }
            if (top) { return Side.Top; }
            if (bottom) { return Side.Bottom; }
            return null;
        }

        private static TileKind Edge(TileKind tile, DoorKind kind, Cell cell, int columns, int height, DoorKind[] doors)
        {
            var side = EdgeSide(cell.Column, cell.Row, columns, height) ?? throw new ArgumentException($"出入口 {cell} は部屋の端に置いてください。");
            var current = doors[(int)side];
            if (current != DoorKind.None && current != kind) { throw new ArgumentException($"{side} の出入口の種類がそろっていません。"); }
            doors[(int)side] = kind;
            return tile;
        }

        /// <summary>出入口のマスの並びの中央、1 マス内側に自機を置いた箱。</summary>
        private static Box ArrivalBox(TileMap map, Side side)
        {
            int min = int.MaxValue, max = int.MinValue;
            var vertical = side == Side.Left || side == Side.Right;
            var count = vertical ? map.Height : map.Width;
            for (var i = 0; i < count; i++)
            {
                var column = vertical ? (side == Side.Left ? 0 : map.Width - 1) : i;
                var row = vertical ? i : (side == Side.Bottom ? 0 : map.Height - 1);
                if (!TileMap.IsDoorTile(map[column, row])) { continue; }
                min = Math.Min(min, i);
                max = Math.Max(max, i);
            }

            // 並びの中央（単位）。出入口のマスは途切れずに並べる前提で、両端から求める。
            var center = ((min + max + 1) * Units.Tile) / 2;
            var half = Hero.Size / 2;
            return side switch
            {
                Side.Left => new Box(Units.Tile + (Units.Tile - Hero.Size) / 2, center - half, Hero.Size, Hero.Size),
                Side.Right => new Box(((map.Width - 2) * Units.Tile) + ((Units.Tile - Hero.Size) / 2), center - half, Hero.Size, Hero.Size),
                Side.Bottom => new Box(center - half, Units.Tile + ((Units.Tile - Hero.Size) / 2), Hero.Size, Hero.Size),
                _ => new Box(center - half, ((map.Height - 2) * Units.Tile) + ((Units.Tile - Hero.Size) / 2), Hero.Size, Hero.Size),
            };
        }

        private static void SetOnce(ref Cell? slot, Cell cell, string what)
        {
            if (slot.HasValue) { throw new ArgumentException($"{what} が 2 つ以上あります。"); }
            slot = cell;
        }

        private static void RequireClear(TileMap map, Box box, string what)
        {
            if (map.TouchesNonFloor(box))
            {
                throw new ArgumentException($"{what} {box} が壁・宝箱・出入口に重なります。");
            }
        }
    }
}
