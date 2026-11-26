using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IntGeometry;

namespace Game.Core
{
    /// <summary>8 方向。北（画面の上、y が小さい方）から時計回り。</summary>
    public enum Direction
    {
        North,
        NorthEast,
        East,
        SouthEast,
        South,
        SouthWest,
        West,
        NorthWest,
    }

    public static class Directions
    {
        public const int Count = 8;

        private static readonly Vec2i[] _offsets =
        {
            new(0, -1), new(1, -1), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1),
        };

        public static Vec2i Offset(this Direction direction) => _offsets[(int)direction];

        public static bool IsDiagonal(this Direction direction) => ((int)direction & 1) == 1;

        public static bool IsValid(Direction direction) => (int)direction >= 0 && (int)direction < Count;

        /// <summary>隣り合う 2 マスの方向。隣でなければ false。</summary>
        public static bool TryBetween(Vec2i from, Vec2i to, out Direction direction)
        {
            var d = to - from;
            for (int i = 0; i < Count; i++)
            {
                if (_offsets[i] == d)
                {
                    direction = (Direction)i;
                    return true;
                }
            }

            direction = Direction.North;
            return false;
        }

        /// <summary>8 方向の距離（チェビシェフ距離）。</summary>
        public static int Distance(Vec2i a, Vec2i b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    /// <summary>地形の種類。部屋と通路はどちらも歩ける床。階段は部屋の床の一種。</summary>
    public enum Tile : byte
    {
        Wall,
        Room,
        Corridor,
        Stairs,
    }

    /// <summary>部屋の内側（床）の範囲。端を含む。</summary>
    public readonly struct Room : IEquatable<Room>
    {
        public Room(int x0, int y0, int x1, int y1)
        {
            X0 = x0;
            Y0 = y0;
            X1 = x1;
            Y1 = y1;
        }

        public int X0 { get; }

        public int Y0 { get; }

        public int X1 { get; }

        public int Y1 { get; }

        public int Width => X1 - X0 + 1;

        public int Height => Y1 - Y0 + 1;

        public Vec2i Center => new((X0 + X1) / 2, (Y0 + Y1) / 2);

        public bool Contains(Vec2i p) => p.X >= X0 && p.X <= X1 && p.Y >= Y0 && p.Y <= Y1;

        public bool Equals(Room other) => X0 == other.X0 && Y0 == other.Y0 && X1 == other.X1 && Y1 == other.Y1;

        public override bool Equals(object? obj) => obj is Room other && Equals(other);

        public override int GetHashCode() => unchecked((((((X0 * 397) ^ Y0) * 397) ^ X1) * 397) ^ Y1);

        public static bool operator ==(Room left, Room right) => left.Equals(right);

        public static bool operator !=(Room left, Room right) => !left.Equals(right);
    }

    /// <summary>1 階の地形。生成の後は変わらない。</summary>
    public sealed class FloorGrid
    {
        private readonly Tile[] _tiles;
        private readonly sbyte[] _roomOf;

        internal FloorGrid(int width, int height, Tile[] tiles, sbyte[] roomOf, IList<Room> rooms)
        {
            Width = width;
            Height = height;
            _tiles = tiles;
            _roomOf = roomOf;
            Rooms = new ReadOnlyCollection<Room>(new List<Room>(rooms));
            for (int i = 0; i < tiles.Length; i++)
            {
                if (tiles[i] == Tile.Stairs) { Stairs = new Vec2i(i % width, i / width); }
            }
        }

        public int Width { get; }

        public int Height { get; }

        public ReadOnlyCollection<Room> Rooms { get; }

        /// <summary>テスト用：<see cref="Row"/> と同じ文字の行から作る。部屋の床（. と &gt;）は全て部屋 0 とする。</summary>
        internal static FloorGrid Parse(params string[] rows)
        {
            int h = rows.Length;
            int w = rows[0].Length;
            var tiles = new Tile[w * h];
            var roomOf = new sbyte[w * h];
            for (int y = 0; y < h; y++)
            {
                if (rows[y].Length != w) { throw new ArgumentException("行の長さが揃っていません。", nameof(rows)); }
                for (int x = 0; x < w; x++)
                {
                    var i = (y * w) + x;
                    tiles[i] = rows[y][x] switch
                    {
                        '.' => Tile.Room,
                        '+' => Tile.Corridor,
                        '>' => Tile.Stairs,
                        _ => Tile.Wall,
                    };
                    roomOf[i] = -1;
                }
            }

            // 部屋の床（. と >）の 4 方向につながった塊を 1 つの部屋とする。
            var rooms = new List<Room>();
            for (int i = 0; i < tiles.Length; i++)
            {
                if (roomOf[i] >= 0 || (tiles[i] != Tile.Room && tiles[i] != Tile.Stairs)) { continue; }
                var id = (sbyte)rooms.Count;
                int x0 = w, y0 = h, x1 = -1, y1 = -1;
                var stack = new Stack<int>();
                stack.Push(i);
                roomOf[i] = id;
                while (stack.Count > 0)
                {
                    var c = stack.Pop();
                    int cx = c % w, cy = c / w;
                    x0 = Math.Min(x0, cx);
                    y0 = Math.Min(y0, cy);
                    x1 = Math.Max(x1, cx);
                    y1 = Math.Max(y1, cy);
                    foreach (var n in new[] { c - 1, c + 1, c - w, c + w })
                    {
                        if (n < 0 || n >= tiles.Length || (Math.Abs((n % w) - cx) > 1)) { continue; }
                        if (roomOf[n] >= 0 || (tiles[n] != Tile.Room && tiles[n] != Tile.Stairs)) { continue; }
                        roomOf[n] = id;
                        stack.Push(n);
                    }
                }

                rooms.Add(new Room(x0, y0, x1, y1));
            }

            return new FloorGrid(w, h, tiles, roomOf, rooms);
        }

        public Vec2i Stairs { get; }

        public bool InBounds(Vec2i p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

        public int Index(Vec2i p) => (p.Y * Width) + p.X;

        /// <summary>範囲外は壁。</summary>
        public Tile this[Vec2i p] => InBounds(p) ? _tiles[Index(p)] : Tile.Wall;

        public bool IsWalkable(Vec2i p) => this[p] != Tile.Wall;

        /// <summary>視線を通すか（壁だけが遮る）。</summary>
        public bool IsTransparent(Vec2i p) => this[p] != Tile.Wall;

        /// <summary>そのマスを含む部屋の番号。部屋でなければ -1。</summary>
        public int RoomAt(Vec2i p) => InBounds(p) ? _roomOf[Index(p)] : -1;

        /// <summary>
        /// 隣のマスへ動けるか（地形だけ）。斜めは両脇のマスがどちらも歩けるときだけ（壁の角を抜けない）。
        /// 攻撃と投げる物の軌跡も同じ規則に従う。
        /// </summary>
        public bool CanStep(Vec2i from, Direction direction)
        {
            var to = from + direction.Offset();
            if (!IsWalkable(to)) { return false; }
            if (!direction.IsDiagonal()) { return true; }
            var d = direction.Offset();
            return IsWalkable(new Vec2i(from.X + d.X, from.Y)) && IsWalkable(new Vec2i(from.X, from.Y + d.Y));
        }

        /// <summary>地形の行（テストの表示・指紋用）。壁 #、部屋 .、通路 +、階段 &gt;。</summary>
        public string Row(int y)
        {
            var chars = new char[Width];
            for (int x = 0; x < Width; x++)
            {
                chars[x] = _tiles[(y * Width) + x] switch
                {
                    Tile.Room => '.',
                    Tile.Corridor => '+',
                    Tile.Stairs => '>',
                    _ => '#',
                };
            }

            return new string(chars);
        }

        /// <summary>
        /// <paramref name="origin"/> から地形だけで歩いて行ける距離（8 方向・角抜けなし）。届かないマスは -1。
        /// <paramref name="blocked"/> が true のマスは通らない（始点は除く）。
        /// </summary>
        public int[] Distances(Vec2i origin, Func<Vec2i, bool>? blocked = null)
        {
            var dist = new int[Width * Height];
            for (int i = 0; i < dist.Length; i++) { dist[i] = -1; }
            if (!InBounds(origin)) { return dist; }
            var queue = new Queue<Vec2i>();
            dist[Index(origin)] = 0;
            queue.Enqueue(origin);
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                var next = dist[Index(p)] + 1;
                for (int d = 0; d < Directions.Count; d++)
                {
                    var dir = (Direction)d;
                    if (!CanStep(p, dir)) { continue; }
                    var q = p + dir.Offset();
                    var qi = Index(q);
                    if (dist[qi] >= 0) { continue; }
                    if (blocked != null && blocked(q)) { continue; }
                    dist[qi] = next;
                    queue.Enqueue(q);
                }
            }

            return dist;
        }
    }
}
