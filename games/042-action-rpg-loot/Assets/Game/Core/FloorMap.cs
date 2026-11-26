using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>敵の種類。並びはドロップ表・設定の添字に使う。</summary>
    public enum FoeKind
    {
        Grunt,
        Archer,
        Charger,
        Boss,
    }

    public static class FoeInfo
    {
        public const int Count = 4;

        public static string DropId(FoeKind kind) => kind switch
        {
            FoeKind.Grunt => "grunt",
            FoeKind.Archer => "archer",
            FoeKind.Charger => "charger",
            FoeKind.Boss => "boss",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        public static string Name(FoeKind kind) => kind switch
        {
            FoeKind.Grunt => "灰鬼",
            FoeKind.Archer => "骨弓",
            FoeKind.Charger => "角獣",
            FoeKind.Boss => "灰の番人",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>地図に置いた敵。<see cref="Index"/> は階の中の配置番号（左上から行ごと）。</summary>
    public readonly struct FoeSpawn
    {
        public FoeSpawn(int index, FoeKind kind, int column, int row)
        {
            Index = index;
            Kind = kind;
            Column = column;
            Row = row;
        }

        public int Index { get; }

        public FoeKind Kind { get; }

        public int Column { get; }

        public int Row { get; }

        public Vec2i Center => FloorMap.TileCenter(Column, Row);
    }

    /// <summary>
    /// 1 つの階の地図。列は西から東、行は南から北（Unity の y と同じ向き）。文字の地図は上の行が北。
    /// 地図の外は壁として扱う。
    /// </summary>
    public sealed class FloorMap : ISolidGrid
    {
        private readonly bool[] _solid;

        internal FloorMap(int number, string name, int width, int height, bool[] solid, int entranceColumn, int entranceRow,
            int stairsColumn, int stairsRow, List<FoeSpawn> spawns)
        {
            Number = number;
            Name = name;
            Width = width;
            Height = height;
            _solid = solid;
            EntranceColumn = entranceColumn;
            EntranceRow = entranceRow;
            StairsColumn = stairsColumn;
            StairsRow = stairsRow;
            Spawns = spawns.AsReadOnly();
        }

        /// <summary>階の番号（1 から）。</summary>
        public int Number { get; }

        public string Name { get; }

        public int Width { get; }

        public int Height { get; }

        public int EntranceColumn { get; }

        public int EntranceRow { get; }

        public Vec2i Entrance => TileCenter(EntranceColumn, EntranceRow);

        public bool HasStairs => StairsColumn >= 0;

        public int StairsColumn { get; }

        public int StairsRow { get; }

        public ReadOnlyCollection<FoeSpawn> Spawns { get; }

        public bool IsSolid(int column, int row) =>
            column < 0 || row < 0 || column >= Width || row >= Height || _solid[(row * Width) + column];

        public static Vec2i TileCenter(int column, int row) =>
            new((column * Units.Tile) + (Units.Tile / 2), (row * Units.Tile) + (Units.Tile / 2));

        public static int ColumnOf(int x) => Units.FloorDiv(x, Units.Tile);

        public static int RowOf(int y) => Units.FloorDiv(y, Units.Tile);

        /// <summary>箱が階段のタイルに重なるか。</summary>
        public bool OnStairs(Box box)
        {
            if (!HasStairs) { return false; }
            var stairs = new Box(StairsColumn * Units.Tile, StairsRow * Units.Tile, Units.Tile, Units.Tile);
            return stairs.Overlaps(box);
        }
    }

    /// <summary>全ての階（Floors.txt）。</summary>
    public sealed class FloorSet
    {
        private FloorSet(List<FloorMap> floors)
        {
            Floors = floors.AsReadOnly();
            var hash = StateHash.Start();
            foreach (var map in floors)
            {
                hash.Mix(map.Width);
                hash.Mix(map.Height);
                for (int row = 0; row < map.Height; row++)
                {
                    for (int column = 0; column < map.Width; column++) { hash.Mix(map.IsSolid(column, row)); }
                }

                hash.Mix(map.EntranceColumn);
                hash.Mix(map.EntranceRow);
                hash.Mix(map.StairsColumn);
                hash.Mix(map.StairsRow);
                foreach (var s in map.Spawns)
                {
                    hash.Mix((int)s.Kind);
                    hash.Mix(s.Column);
                    hash.Mix(s.Row);
                }
            }

            Fingerprint = hash.Value;
        }

        public ReadOnlyCollection<FloorMap> Floors { get; }

        /// <summary>地図の指紋。状態のハッシュに混ぜる。</summary>
        public uint Fingerprint { get; }

        public FloorMap this[int number] => Floors[number - 1];

        public static FloorSet Parse(string text)
        {
            if (text == null) { throw new ArgumentNullException(nameof(text)); }
            var lines = text.Replace("\r", string.Empty).Split('\n');
            var floors = new List<FloorMap>();
            List<string>? rows = null;
            string name = string.Empty;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (rows == null)
                {
                    if (line.StartsWith("floor ", StringComparison.Ordinal))
                    {
                        var parts = line.Split(' ');
                        if (parts.Length < 3 || parts[1] != (floors.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))
                        {
                            throw new FormatException($"{i + 1} 行目: floor は番号順に「floor 番号 名前」で書きます。");
                        }

                        name = parts[2];
                        rows = new List<string>();
                    }

                    continue;
                }

                if (line == "end")
                {
                    floors.Add(Build(floors.Count + 1, name, rows, i + 1));
                    rows = null;
                    continue;
                }

                rows.Add(line);
            }

            if (rows != null) { throw new FormatException("最後の floor に end がありません。"); }
            if (floors.Count != LootData.MaxFloor) { throw new FormatException($"階は {LootData.MaxFloor} つです（{floors.Count}）。"); }
            for (int n = 0; n < floors.Count; n++)
            {
                bool last = n == floors.Count - 1;
                if (floors[n].HasStairs == last) { throw new FormatException($"{n + 1} 階：階段は最後の階以外に 1 つずつ置きます。"); }
                bool boss = false;
                foreach (var s in floors[n].Spawns) { boss |= s.Kind == FoeKind.Boss; }
                if (boss != last) { throw new FormatException($"{n + 1} 階：番人は最後の階に 1 体だけ置きます。"); }
            }

            return new FloorSet(floors);
        }

        private static FloorMap Build(int number, string name, List<string> rows, int lineNo)
        {
            if (rows.Count == 0) { throw new FormatException($"{lineNo} 行目: {number} 階の地図が空です。"); }
            int width = rows[0].Length;
            int height = rows.Count;
            var solid = new bool[width * height];
            int ex = -1, ey = -1, sx = -1, sy = -1;
            var spawns = new List<FoeSpawn>();
            int bosses = 0;
            for (int r = 0; r < height; r++)
            {
                if (rows[r].Length != width) { throw new FormatException($"{number} 階の {r + 1} 行目の幅が違います。"); }
                int row = height - 1 - r;
                for (int column = 0; column < width; column++)
                {
                    char ch = rows[r][column];
                    switch (ch)
                    {
                        case '#': solid[(row * width) + column] = true; break;
                        case '.': break;
                        case '@':
                            if (ex >= 0) { throw new FormatException($"{number} 階：入口が 2 つあります。"); }
                            ex = column;
                            ey = row;
                            break;
                        case '>':
                            if (sx >= 0) { throw new FormatException($"{number} 階：階段が 2 つあります。"); }
                            sx = column;
                            sy = row;
                            break;
                        case 'g': spawns.Add(new FoeSpawn(spawns.Count, FoeKind.Grunt, column, row)); break;
                        case 'a': spawns.Add(new FoeSpawn(spawns.Count, FoeKind.Archer, column, row)); break;
                        case 'c': spawns.Add(new FoeSpawn(spawns.Count, FoeKind.Charger, column, row)); break;
                        case 'B':
                            bosses++;
                            spawns.Add(new FoeSpawn(spawns.Count, FoeKind.Boss, column, row));
                            break;
                        default: throw new FormatException($"{number} 階：不明な文字 '{ch}' です。");
                    }
                }
            }

            if (ex < 0) { throw new FormatException($"{number} 階：入口がありません。"); }
            if (bosses > 1) { throw new FormatException($"{number} 階：番人が 2 体以上います。"); }
            var map = new FloorMap(number, name, width, height, solid, ex, ey, sx, sy, spawns);
            RequireReachable(map);
            return map;
        }

        /// <summary>入口から階段と全ての敵へ歩いて届くこと（4 方向のタイルの探索）。</summary>
        private static void RequireReachable(FloorMap map)
        {
            var dist = TileGrid.Distances(map, map.EntranceColumn, map.EntranceRow);
            if (map.HasStairs && dist[(map.StairsRow * map.Width) + map.StairsColumn] < 0)
            {
                throw new FormatException($"{map.Number} 階：入口から階段へ届きません。");
            }

            foreach (var s in map.Spawns)
            {
                if (dist[(s.Row * map.Width) + s.Column] < 0) { throw new FormatException($"{map.Number} 階：配置 {s.Index} へ届きません。"); }
            }
        }
    }

    /// <summary>タイルの 4 方向の幅優先探索。地図の検証と操縦役の経路で使う。</summary>
    public static class TileGrid
    {
        /// <summary>(column, row) からの歩数。届かないタイルは -1。</summary>
        public static int[] Distances(FloorMap map, int column, int row)
        {
            if (map == null) { throw new ArgumentNullException(nameof(map)); }
            var dist = new int[map.Width * map.Height];
            for (int i = 0; i < dist.Length; i++) { dist[i] = -1; }
            if (map.IsSolid(column, row)) { return dist; }
            var queue = new Queue<int>();
            dist[(row * map.Width) + column] = 0;
            queue.Enqueue((row * map.Width) + column);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                int c = cell % map.Width;
                int r = cell / map.Width;
                Visit(c + 1, r);
                Visit(c - 1, r);
                Visit(c, r + 1);
                Visit(c, r - 1);

                void Visit(int nc, int nr)
                {
                    if (map.IsSolid(nc, nr)) { return; }
                    int next = (nr * map.Width) + nc;
                    if (dist[next] >= 0) { return; }
                    dist[next] = dist[cell] + 1;
                    queue.Enqueue(next);
                }
            }

            return dist;
        }
    }
}
