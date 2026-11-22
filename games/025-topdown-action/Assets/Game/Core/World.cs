using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Core
{
    /// <summary>
    /// ダンジョンの部屋の組。部屋は地図の格子に並び、辺の出入口はその辺の隣の部屋の反対側の出入口につながる。
    /// つながった出入口の組（通路）には通し番号を振り、扉が開いたかをダンジョンの状態がその番号で持つ（両側で同じ 1 枚）。
    /// 宝箱にも通し番号（部屋の順・部屋の中の順）を振る。
    /// </summary>
    public sealed class World
    {
        private readonly Room[] _rooms;
        private readonly int[,] _passages;
        private readonly int[] _firstChest;

        public World(IReadOnlyList<Room> rooms)
        {
            if (rooms == null) { throw new ArgumentNullException(nameof(rooms)); }
            if (rooms.Count == 0) { throw new ArgumentException("部屋が 1 つもありません。", nameof(rooms)); }
            _rooms = new Room[rooms.Count];
            _firstChest = new int[rooms.Count];
            var chests = 0;
            var starts = 0;
            var bosses = 0;
            for (var i = 0; i < _rooms.Length; i++)
            {
                var room = rooms[i] ?? throw new ArgumentException($"{i} 番目の部屋が null です。", nameof(rooms));
                for (var j = 0; j < i; j++)
                {
                    if (_rooms[j].GridX == room.GridX && _rooms[j].GridY == room.GridY)
                    {
                        throw new ArgumentException($"{_rooms[j].Name} と {room.Name} が地図の同じ位置にあります。", nameof(rooms));
                    }
                }

                _rooms[i] = room;
                _firstChest[i] = chests;
                chests += room.Chests.Count;
                if (room.Start.HasValue)
                {
                    StartRoom = i;
                    starts++;
                }

                if (room.Boss.HasValue)
                {
                    BossRoom = i;
                    bosses++;
                }
            }

            if (starts != 1) { throw new ArgumentException($"開始位置 S はダンジョンに 1 つだけ置いてください（{starts} 個）。", nameof(rooms)); }
            if (bosses > 1) { throw new ArgumentException($"ボス M はダンジョンに 1 つまでです（{bosses} 個）。", nameof(rooms)); }
            if (bosses == 0) { BossRoom = -1; }
            ChestCount = chests;
            Rooms = Array.AsReadOnly(_rooms);

            _passages = new int[_rooms.Length, 4];
            var passages = 0;
            for (var i = 0; i < _rooms.Length; i++)
            {
                foreach (Side side in Enum.GetValues(typeof(Side)))
                {
                    _passages[i, (int)side] = -1;
                    var kind = _rooms[i].Door(side);
                    if (kind == DoorKind.None) { continue; }
                    var next = Neighbor(i, side);
                    if (next < 0 || _rooms[next].Door(Opposite(side)) != kind)
                    {
                        throw new ArgumentException($"{_rooms[i].Name} の {side} の出入口の先に、同じ種類の反対側の出入口を持つ部屋がありません。", nameof(rooms));
                    }

                    // 先に番号を振った側（部屋の番号が小さい側）と同じ番号にする。
                    _passages[i, (int)side] = next < i ? _passages[next, (int)Opposite(side)] : passages++;
                }
            }

            PassageCount = passages;
        }

        public ReadOnlyCollection<Room> Rooms { get; }

        public int StartRoom { get; }

        /// <summary>ボスの部屋（なければ -1）。</summary>
        public int BossRoom { get; }

        public int ChestCount { get; }

        /// <summary>出入口の組の数。</summary>
        public int PassageCount { get; }

        /// <summary>部屋 <paramref name="room"/> の <paramref name="side"/> の出入口の通路番号（出入口がなければ -1）。</summary>
        public int PassageId(int room, Side side) => _passages[room, (int)side];

        /// <summary>部屋 <paramref name="room"/> の <paramref name="index"/> 番目の宝箱の通し番号。</summary>
        public int ChestId(int room, int index)
        {
            if (index < 0 || index >= _rooms[room].Chests.Count) { throw new ArgumentOutOfRangeException(nameof(index), index, "部屋にない宝箱です。"); }
            return _firstChest[room] + index;
        }

        /// <summary>部屋 <paramref name="room"/> の <paramref name="side"/> の辺の隣にある部屋（なければ -1）。</summary>
        public int Neighbor(int room, Side side)
        {
            var (dx, dy) = side switch
            {
                Side.Left => (-1, 0),
                Side.Right => (1, 0),
                Side.Top => (0, 1),
                Side.Bottom => (0, -1),
                _ => throw new ArgumentOutOfRangeException(nameof(side), side, "未定義の辺です。"),
            };
            var x = _rooms[room].GridX + dx;
            var y = _rooms[room].GridY + dy;
            for (var i = 0; i < _rooms.Length; i++)
            {
                if (_rooms[i].GridX == x && _rooms[i].GridY == y) { return i; }
            }

            return -1;
        }

        public static Side Opposite(Side side) => side switch
        {
            Side.Left => Side.Right,
            Side.Right => Side.Left,
            Side.Top => Side.Bottom,
            Side.Bottom => Side.Top,
            _ => throw new ArgumentOutOfRangeException(nameof(side), side, "未定義の辺です。"),
        };
    }
}
