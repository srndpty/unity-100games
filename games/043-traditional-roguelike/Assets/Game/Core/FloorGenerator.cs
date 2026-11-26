using System;
using System.Collections.Generic;
using IntGeometry;

namespace Game.Core
{
    /// <summary>生成した 1 階：地形・自機の開始位置・検証を通るまでに引いた回数。</summary>
    public sealed class FloorLayout
    {
        internal FloorLayout(FloorGrid grid, Vec2i start, int attempts)
        {
            Grid = grid;
            Start = start;
            Attempts = attempts;
        }

        public FloorGrid Grid { get; }

        public Vec2i Start { get; }

        public int Attempts { get; }
    }

    /// <summary>
    /// 地形の生成。盤を 3×3 の区画に分け、6〜9 区画に部屋を置き（残りは通路の交点）、区画の全域木と余分な数本で通路をつなぐ。
    /// 乱数は (試合シード, 階) の地形専用の系列だけを使うので、自機の行動に依らず同じ階は同じ地形になる。
    /// </summary>
    public static class FloorGenerator
    {
        private const int Cells = 3;

        public static FloorLayout Generate(int matchSeed, int floor, CrawlConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var rng = RandomSeries.Derive(matchSeed, floor, (int)RngPurpose.Terrain);
            for (int attempt = 1; attempt <= config.GenerationAttempts; attempt++)
            {
                var layout = TryGenerate(rng, config, attempt);
                if (layout != null) { return layout; }
            }

            throw new InvalidOperationException($"{config.GenerationAttempts} 回引き直しても地形の検証を通りませんでした（シード {matchSeed}、{floor} 階）。");
        }

        /// <summary>検証：全ての床が開始位置から歩いて行ける・階段は開始の部屋と別の部屋にある。</summary>
        public static bool IsValid(FloorGrid grid, Vec2i start)
        {
            if (grid.RoomAt(start) < 0 || grid.RoomAt(grid.Stairs) < 0) { return false; }
            if (grid.RoomAt(start) == grid.RoomAt(grid.Stairs)) { return false; }
            var dist = grid.Distances(start);
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var p = new Vec2i(x, y);
                    if (grid.IsWalkable(p) && dist[grid.Index(p)] < 0) { return false; }
                }
            }

            return true;
        }

        private static FloorLayout? TryGenerate(RandomSeries rng, CrawlConfig config, int attempt)
        {
            int w = config.Width;
            int h = config.Height;
            var tiles = new Tile[w * h];
            var roomOf = new sbyte[w * h];
            for (int i = 0; i < roomOf.Length; i++) { roomOf[i] = -1; }

            // 部屋を置く区画を選ぶ。
            var roomCount = rng.Range(config.MinRooms, config.MaxRooms);
            var order = Shuffled(rng, Cells * Cells);
            var hasRoom = new bool[Cells * Cells];
            for (int i = 0; i < roomCount; i++) { hasRoom[order[i]] = true; }

            // 区画ごとの部屋（または交点の 1 マス）。区画の内側に、右と下に 3 マス・左と上に 1 マスの余白を取る。
            var anchors = new Room[Cells * Cells];
            var rooms = new List<Room>();
            for (int cell = 0; cell < Cells * Cells; cell++)
            {
                CellBounds(config, cell, out var cx0, out var cy0, out var cx1, out var cy1);
                int minX = cx0 + 1;
                int maxX = cx1 - 3;
                int minY = cy0 + 1;
                int maxY = cy1 - 3;
                if (hasRoom[cell])
                {
                    var rw = rng.Range(4, maxX - minX + 1);
                    var rh = rng.Range(3, maxY - minY + 1);
                    var x0 = rng.Range(minX, maxX - rw + 1);
                    var y0 = rng.Range(minY, maxY - rh + 1);
                    var room = new Room(x0, y0, x0 + rw - 1, y0 + rh - 1);
                    anchors[cell] = room;
                    var id = (sbyte)rooms.Count;
                    rooms.Add(room);
                    for (int y = room.Y0; y <= room.Y1; y++)
                    {
                        for (int x = room.X0; x <= room.X1; x++)
                        {
                            tiles[(y * w) + x] = Tile.Room;
                            roomOf[(y * w) + x] = id;
                        }
                    }
                }
                else
                {
                    var p = new Vec2i(rng.Range(minX, maxX), rng.Range(minY, maxY));
                    anchors[cell] = new Room(p.X, p.Y, p.X, p.Y);
                    tiles[(p.Y * w) + p.X] = Tile.Corridor;
                }
            }

            foreach (var (a, b) in Connections(rng, config))
            {
                Carve(rng, tiles, w, anchors[a], hasRoom[a], anchors[b], hasRoom[b], IsHorizontal(a, b));
            }

            // 開始位置と階段は別の部屋。
            var startRoom = rng.Next(rooms.Count);
            var stairsRoom = rng.Next(rooms.Count - 1);
            if (stairsRoom >= startRoom) { stairsRoom++; }
            var start = RandomIn(rng, rooms[startRoom]);
            var stairs = RandomIn(rng, rooms[stairsRoom]);
            tiles[(stairs.Y * w) + stairs.X] = Tile.Stairs;

            var grid = new FloorGrid(w, h, tiles, roomOf, rooms);
            return IsValid(grid, start) ? new FloorLayout(grid, start, attempt) : null;
        }

        /// <summary>区画の範囲 [cx0, cx1) × [cy0, cy1)。</summary>
        private static void CellBounds(CrawlConfig config, int cell, out int cx0, out int cy0, out int cx1, out int cy1)
        {
            int col = cell % Cells;
            int row = cell / Cells;
            cx0 = col * config.Width / Cells;
            cx1 = (col + 1) * config.Width / Cells;
            cy0 = row * config.Height / Cells;
            cy1 = (row + 1) * config.Height / Cells;
        }

        private static bool IsHorizontal(int a, int b) => a / Cells == b / Cells;

        /// <summary>隣り合う区画の全域木（無作為な Prim 法）と余分な辺。各組は (左, 右) か (上, 下) の順。</summary>
        private static List<(int, int)> Connections(RandomSeries rng, CrawlConfig config)
        {
            var all = new List<(int, int)>();
            for (int cell = 0; cell < Cells * Cells; cell++)
            {
                if (cell % Cells < Cells - 1) { all.Add((cell, cell + 1)); }
                if (cell / Cells < Cells - 1) { all.Add((cell, cell + Cells)); }
            }

            var inTree = new bool[Cells * Cells];
            inTree[rng.Next(Cells * Cells)] = true;
            var used = new bool[all.Count];
            var result = new List<(int, int)>();
            for (int added = 1; added < Cells * Cells; added++)
            {
                var frontier = new List<int>();
                for (int i = 0; i < all.Count; i++)
                {
                    var (a, b) = all[i];
                    if (inTree[a] != inTree[b]) { frontier.Add(i); }
                }

                var pick = frontier[rng.Next(frontier.Count)];
                used[pick] = true;
                inTree[all[pick].Item1] = true;
                inTree[all[pick].Item2] = true;
                result.Add(all[pick]);
            }

            var extra = rng.Range(config.MinExtraCorridors, config.MaxExtraCorridors);
            for (int i = 0; i < extra; i++)
            {
                var rest = new List<int>();
                for (int e = 0; e < all.Count; e++)
                {
                    if (!used[e]) { rest.Add(e); }
                }

                if (rest.Count == 0) { break; }
                var pick = rest[rng.Next(rest.Count)];
                used[pick] = true;
                result.Add(all[pick]);
            }

            return result;
        }

        /// <summary>
        /// 2 つの区画を通路でつなぐ。部屋の壁の 1 マスを出入口にし、2 つの出入口の間の列（行）で折れる。
        /// 折れる位置は両方の部屋の壁より内側（区画の余白）なので、通路が部屋を横切らない。
        /// </summary>
        private static void Carve(RandomSeries rng, Tile[] tiles, int w, Room a, bool aRoom, Room b, bool bRoom, bool horizontal)
        {
            Vec2i from;
            Vec2i to;
            if (horizontal)
            {
                from = aRoom ? new Vec2i(a.X1 + 1, rng.Range(a.Y0, a.Y1)) : new Vec2i(a.X0, a.Y0);
                to = bRoom ? new Vec2i(b.X0 - 1, rng.Range(b.Y0, b.Y1)) : new Vec2i(b.X0, b.Y0);
                var mx = rng.Range(from.X + 1, to.X - 1);
                Line(tiles, w, from, new Vec2i(mx, from.Y));
                Line(tiles, w, new Vec2i(mx, from.Y), new Vec2i(mx, to.Y));
                Line(tiles, w, new Vec2i(mx, to.Y), to);
            }
            else
            {
                from = aRoom ? new Vec2i(rng.Range(a.X0, a.X1), a.Y1 + 1) : new Vec2i(a.X0, a.Y0);
                to = bRoom ? new Vec2i(rng.Range(b.X0, b.X1), b.Y0 - 1) : new Vec2i(b.X0, b.Y0);
                var my = rng.Range(from.Y + 1, to.Y - 1);
                Line(tiles, w, from, new Vec2i(from.X, my));
                Line(tiles, w, new Vec2i(from.X, my), new Vec2i(to.X, my));
                Line(tiles, w, new Vec2i(to.X, my), to);
            }
        }

        /// <summary>縦か横の線分を通路にする（部屋の床は書き換えない）。</summary>
        private static void Line(Tile[] tiles, int w, Vec2i from, Vec2i to)
        {
            var dx = Math.Sign(to.X - from.X);
            var dy = Math.Sign(to.Y - from.Y);
            var p = from;
            while (true)
            {
                var i = (p.Y * w) + p.X;
                if (tiles[i] == Tile.Wall) { tiles[i] = Tile.Corridor; }
                if (p == to) { break; }
                p = new Vec2i(p.X + dx, p.Y + dy);
            }
        }

        private static Vec2i RandomIn(RandomSeries rng, Room room) => new(rng.Range(room.X0, room.X1), rng.Range(room.Y0, room.Y1));

        private static int[] Shuffled(RandomSeries rng, int count)
        {
            var order = new int[count];
            for (int i = 0; i < count; i++) { order[i] = i; }
            for (int i = count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            return order;
        }
    }
}
