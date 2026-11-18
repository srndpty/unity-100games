using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>辺を通る主体の種類。扉と寝床に入れるかが変わる。</summary>
    public enum Traversal
    {
        /// <summary>通路だけを通る（自機、巡回・追跡・逃走中の猫）。</summary>
        Corridor,

        /// <summary>扉と寝床も通る（退出中・帰還中の猫）。</summary>
        Gate,
    }

    /// <summary>
    /// 迷路のグラフ。壁以外のマスをノード、隣り合うマスと左右の周回を辺とする。
    /// 構築時に不変条件を検証するので、通過したグラフでは行き止まりや孤立した通路が存在しない。
    /// </summary>
    public sealed class MazeGraph
    {
        private const int Unreachable = -1;
        private readonly int[] _neighbors;
        private readonly int[] _toHouse;
        private readonly int[] _toExit;

        public MazeGraph(MazeLayout layout)
        {
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            CellCount = layout.Width * layout.Height;
            _neighbors = new int[CellCount * DirectionExtensions.Count];
            BuildNeighbors();

            var door = layout.Door;
            if (layout.Kind(door.X, door.Y - 1) != CellKind.Path) { throw new ArgumentException("扉の上は通路が必要です。", nameof(layout)); }
            if (layout.Kind(door.X, door.Y + 1) != CellKind.House) { throw new ArgumentException("扉の下は寝床が必要です。", nameof(layout)); }
            for (var offset = -2; offset <= 2; offset += 2)
            {
                if (layout.Kind(door.X + offset, door.Y + 2) != CellKind.House) { throw new ArgumentException("猫の開始位置が寝床の中にありません。", nameof(layout)); }
            }

            ExitCell = CellId(door.X, door.Y - 1);
            HouseCell = CellId(door.X, door.Y + 2);
            PlayerCell = CellId(layout.PlayerSpawn.X, layout.PlayerSpawn.Y);
            _toHouse = DistancesFrom(HouseCell, Traversal.Gate);
            _toExit = DistancesFrom(ExitCell, Traversal.Gate);
            Validate();
        }

        public MazeLayout Layout { get; }
        public int Width => Layout.Width;
        public int Height => Layout.Height;
        public int CellCount { get; }

        /// <summary>寝床の扉のすぐ外のマス。</summary>
        public int ExitCell { get; }

        /// <summary>帰還した猫が目指す寝床の中央のマス。</summary>
        public int HouseCell { get; }

        public int PlayerCell { get; }

        public int CellId(int x, int y) => (y * Width) + x;

        public GridPoint Point(int cell) => new(cell % Width, cell / Width);

        public CellKind Kind(int cell)
        {
            var point = Point(cell);
            return Layout.Kind(point.X, point.Y);
        }

        /// <summary>隣のマス。壁や盤面の外なら -1。</summary>
        public int Neighbor(int cell, Direction direction)
        {
            if (cell < 0 || cell >= CellCount) { throw new ArgumentOutOfRangeException(nameof(cell)); }
            if (!direction.IsMove()) { throw new ArgumentOutOfRangeException(nameof(direction)); }
            return _neighbors[(cell * DirectionExtensions.Count) + (int)direction];
        }

        public bool CanEnter(int cell, Direction direction, Traversal traversal)
        {
            var next = Neighbor(cell, direction);
            return next >= 0 && (traversal == Traversal.Gate || Kind(next) == CellKind.Path);
        }

        /// <summary>通路として3方向以上に進めるマス。</summary>
        public bool IsJunction(int cell) => Degree(cell) >= 3;

        public int DistanceToHouse(int cell) => _toHouse[cell];

        public int DistanceToExit(int cell) => _toExit[cell];

        private int Degree(int cell)
        {
            var degree = 0;
            for (var i = 0; i < DirectionExtensions.Count; i++)
            {
                if (CanEnter(cell, (Direction)i, Traversal.Corridor)) { degree++; }
            }

            return degree;
        }

        private void BuildNeighbors()
        {
            for (var cell = 0; cell < CellCount; cell++)
            {
                var point = Point(cell);
                for (var i = 0; i < DirectionExtensions.Count; i++)
                {
                    var direction = (Direction)i;
                    var x = point.X + direction.Dx();
                    var y = point.Y + direction.Dy();
                    if (x < 0) { x = Width - 1; }
                    else if (x >= Width) { x = 0; }

                    var open = Layout.Kind(point.X, point.Y) != CellKind.Wall && Layout.Kind(x, y) != CellKind.Wall;
                    _neighbors[(cell * DirectionExtensions.Count) + i] = open ? CellId(x, y) : -1;
                }
            }
        }

        private int[] DistancesFrom(int origin, Traversal traversal)
        {
            var distances = new int[CellCount];
            Array.Fill(distances, Unreachable);
            distances[origin] = 0;
            var queue = new Queue<int>();
            queue.Enqueue(origin);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                for (var i = 0; i < DirectionExtensions.Count; i++)
                {
                    if (!CanEnter(cell, (Direction)i, traversal)) { continue; }
                    var next = Neighbor(cell, (Direction)i);
                    if (distances[next] != Unreachable) { continue; }
                    distances[next] = distances[cell] + 1;
                    queue.Enqueue(next);
                }
            }

            return distances;
        }

        private void Validate()
        {
            var fromPlayer = DistancesFrom(PlayerCell, Traversal.Corridor);
            var pellets = 0;
            for (var cell = 0; cell < CellCount; cell++)
            {
                var kind = Kind(cell);
                if (kind == CellKind.Wall) { continue; }
                if (_toHouse[cell] == Unreachable || _toExit[cell] == Unreachable) { throw new ArgumentException("寝床とつながっていないマスがあります: " + Point(cell)); }
                if (kind != CellKind.Path) { continue; }
                if (fromPlayer[cell] == Unreachable) { throw new ArgumentException("自機が到達できない通路があります: " + Point(cell)); }
                if (Degree(cell) < 2) { throw new ArgumentException("行き止まりがあります: " + Point(cell)); }
                var point = Point(cell);
                if (Layout.PelletAt(point.X, point.Y) != Pellet.None) { pellets++; }
            }

            if (pellets == 0) { throw new ArgumentException("餌がありません。"); }
        }
    }
}
