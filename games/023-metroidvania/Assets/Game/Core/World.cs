using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Core
{
    /// <summary>
    /// 部屋の組。部屋は地図の格子に並び、扉はその辺の隣の部屋の反対側の扉につながる。
    /// アイテムには世界での通し番号（部屋の順・部屋の中の順）を振り、取ったかどうかを世界の状態がその番号で持つ。
    /// </summary>
    public sealed class World
    {
        private readonly Room[] _rooms;
        private readonly int[] _firstPickup;

        public World(IReadOnlyList<Room> rooms)
        {
            if (rooms == null) { throw new ArgumentNullException(nameof(rooms)); }
            if (rooms.Count == 0) { throw new ArgumentException("部屋が 1 つもありません。", nameof(rooms)); }
            _rooms = new Room[rooms.Count];
            _firstPickup = new int[rooms.Count];
            var pickups = 0;
            var starts = 0;
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
                _firstPickup[i] = pickups;
                pickups += room.Pickups.Count;
                if (room.Start.HasValue)
                {
                    StartRoom = i;
                    starts++;
                }
            }

            if (starts != 1) { throw new ArgumentException($"開始位置 S は世界に 1 つだけ置いてください（{starts} 個）。", nameof(rooms)); }
            PickupCount = pickups;
            Rooms = Array.AsReadOnly(_rooms);
            for (var i = 0; i < _rooms.Length; i++)
            {
                foreach (Side side in Enum.GetValues(typeof(Side)))
                {
                    if (!_rooms[i].HasDoor(side)) { continue; }
                    var next = Neighbor(i, side);
                    if (next < 0 || !_rooms[next].HasDoor(Opposite(side)))
                    {
                        throw new ArgumentException($"{_rooms[i].Name} の {side} の扉の先に、反対側の扉を持つ部屋がありません。", nameof(rooms));
                    }
                }
            }
        }

        public ReadOnlyCollection<Room> Rooms { get; }

        public int StartRoom { get; }

        /// <summary>世界にあるアイテムの数。</summary>
        public int PickupCount { get; }

        /// <summary>部屋 <paramref name="room"/> の <paramref name="index"/> 番目のアイテムの通し番号。</summary>
        public int PickupId(int room, int index)
        {
            if (index < 0 || index >= _rooms[room].Pickups.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), index, "部屋にないアイテムです。");
            }

            return _firstPickup[room] + index;
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
