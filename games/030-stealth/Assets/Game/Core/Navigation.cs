using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 目標のマスへの歩く経路（4 近傍、壁・木箱は通れない）。目標から幅優先で距離を数え、距離が 1 小さい隣へ進む。
    /// 同じ距離の隣が複数あれば 右・上・左・下 の順で選ぶ（決まった経路になる）。目標が変わったときだけ数え直す。
    /// </summary>
    public sealed class PathField
    {
        private readonly FacilityMap _map;
        private readonly int[] _distance;
        private readonly int[] _queue;

        public PathField(FacilityMap map)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _distance = new int[map.Count];
            _queue = new int[map.Count];
        }

        private PathField(PathField source)
        {
            _map = source._map;
            _distance = (int[])source._distance.Clone();
            _queue = new int[source._queue.Length];
            Goal = source.Goal;
        }

        /// <summary>今の目標（まだ数えていなければ null）。</summary>
        public Cell? Goal { get; private set; }

        public PathField Clone() => new(this);

        /// <summary>目標までの歩数（届かなければ -1）。</summary>
        public int DistanceTo(Cell from, Cell goal)
        {
            Prepare(goal);
            return _map.Contains(from) ? _distance[_map.IndexOf(from)] : -1;
        }

        /// <summary><paramref name="from"/> から <paramref name="goal"/> へ 1 歩進んだマス。着いている・届かないなら <paramref name="from"/>。</summary>
        public Cell NextStep(Cell from, Cell goal)
        {
            Prepare(goal);
            if (!_map.Contains(from)) { return from; }
            var d = _distance[_map.IndexOf(from)];
            if (d <= 0) { return from; }
            for (var direction = 0; direction < 4; direction++)
            {
                var next = Neighbor(from, direction);
                if (_map.Contains(next) && _distance[_map.IndexOf(next)] == d - 1) { return next; }
            }

            return from;
        }

        private void Prepare(Cell goal)
        {
            if (Goal == goal) { return; }
            Goal = goal;
            Flood(_map, goal, int.MaxValue, _distance, _queue);
        }

        /// <summary>隣のマス。<paramref name="direction"/> は 0 右・1 上・2 左・3 下。</summary>
        public static Cell Neighbor(Cell cell, int direction) => direction switch
        {
            0 => new Cell(cell.Column + 1, cell.Row),
            1 => new Cell(cell.Column, cell.Row + 1),
            2 => new Cell(cell.Column - 1, cell.Row),
            3 => new Cell(cell.Column, cell.Row - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "向きは 0〜3 です。"),
        };

        /// <summary>歩けるマスを <paramref name="start"/> から幅優先で数える（<paramref name="limit"/> 歩まで）。数えたマスの数を返す。</summary>
        internal static int Flood(FacilityMap map, Cell start, int limit, int[] distance, int[] queue)
        {
            for (var i = 0; i < distance.Length; i++) { distance[i] = -1; }
            if (!map.Contains(start) || !map.IsWalkable(start)) { return 0; }
            var head = 0;
            var tail = 0;
            var first = map.IndexOf(start);
            distance[first] = 0;
            queue[tail++] = first;
            while (head < tail)
            {
                var index = queue[head++];
                var d = distance[index];
                if (d >= limit) { continue; }
                var cell = map.CellAt(index);
                for (var direction = 0; direction < 4; direction++)
                {
                    tail = Push(map, Neighbor(cell, direction), d + 1, distance, queue, tail);
                }
            }

            return tail;
        }

        private static int Push(FacilityMap map, Cell cell, int d, int[] distance, int[] queue, int tail)
        {
            if (!map.Contains(cell) || !map.IsWalkable(cell)) { return tail; }
            var index = map.IndexOf(cell);
            if (distance[index] >= 0) { return tail; }
            distance[index] = d;
            queue[tail] = index;
            return tail + 1;
        }
    }

    /// <summary>捜索で次に向かうマスを選ぶ。判断（<see cref="GuardBrain"/>）は地図を知らず、これだけを使う。</summary>
    public interface ISearchArea
    {
        /// <summary><paramref name="center"/> の周りの、捜索で向かうマスを 1 つ選ぶ。</summary>
        Cell Pick(Cell center, SeededRandom random);
    }

    /// <summary>最後に見た位置から歩いて <c>radius</c> 歩以内の歩けるマスから、乱数で 1 つ選ぶ（中心は除く。ほかになければ中心）。</summary>
    public sealed class SearchArea : ISearchArea
    {
        private readonly FacilityMap _map;
        private readonly int _radius;
        private readonly int[] _distance;
        private readonly int[] _queue;

        public SearchArea(FacilityMap map, int radius)
        {
            _map = map ?? throw new ArgumentNullException(nameof(map));
            if (radius < 0) { throw new ArgumentOutOfRangeException(nameof(radius), radius, "範囲は 0 以上にしてください。"); }
            _radius = radius;
            _distance = new int[map.Count];
            _queue = new int[map.Count];
        }

        public Cell Pick(Cell center, SeededRandom random)
        {
            if (random == null) { throw new ArgumentNullException(nameof(random)); }
            var count = PathField.Flood(_map, center, _radius, _distance, _queue);
            if (count <= 1) { return center; }

            // 幅優先の順（決まった順）に並んだ候補から、中心（先頭）を除いて選ぶ。
            return _map.CellAt(_queue[1 + random.Next(count - 1)]);
        }
    }
}
