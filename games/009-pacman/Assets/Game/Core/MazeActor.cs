using System;

namespace Game.Core
{
    /// <summary>
    /// 迷路上の位置。「最後に通ったマス中心」「進行方向」「次のマスまでの進捗」で表す。
    /// 進捗が 0 のときはマス中心にいる。すべて整数なので Mono と .NET で結果が一致する。
    /// </summary>
    public sealed class MazeActor
    {
        public const int UnitsPerCell = 600;

        public MazeActor(int cell, Direction heading)
        {
            Reset(cell, heading);
        }

        public int Cell { get; private set; }
        public Direction Heading { get; private set; }
        public int Progress { get; private set; }

        public void Reset(int cell, Direction heading)
        {
            if (!heading.IsMove()) { throw new ArgumentOutOfRangeException(nameof(heading)); }
            Cell = cell;
            Heading = heading;
            Progress = 0;
        }

        /// <summary>マス中心にいるときだけ進行方向を変えられる。</summary>
        public void Turn(Direction heading)
        {
            if (!heading.IsMove()) { throw new ArgumentOutOfRangeException(nameof(heading)); }
            if (Progress != 0) { throw new InvalidOperationException("マスの途中では曲がれません。"); }
            Heading = heading;
        }

        /// <summary>次のマス中心を上限に進み、使い切れなかった移動量を返す。</summary>
        public int Advance(MazeGraph graph, int units, out bool arrived)
        {
            arrived = false;
            var remaining = UnitsPerCell - Progress;
            if (units < remaining)
            {
                Progress += units;
                return 0;
            }

            var next = graph.Neighbor(Cell, Heading);
            if (next < 0) { throw new InvalidOperationException("壁へ進もうとしました: " + graph.Point(Cell) + " " + Heading); }
            Cell = next;
            Progress = 0;
            arrived = true;
            return units - remaining;
        }

        /// <summary>
        /// その場で反転する。マスの途中なら来た辺を戻る。
        /// マス中心では、逆方向へ進める場合だけ向きを変える。
        /// </summary>
        public void Reverse(MazeGraph graph, Traversal traversal)
        {
            var opposite = Heading.Opposite();
            if (Progress > 0)
            {
                Cell = graph.Neighbor(Cell, Heading);
                Heading = opposite;
                Progress = UnitsPerCell - Progress;
            }
            else if (graph.CanEnter(Cell, opposite, traversal))
            {
                Heading = opposite;
            }
        }

        /// <summary>盤面上の連続座標（単位：1マス=600）。周回を考慮して [0, 幅) に正規化する。</summary>
        public int WorldX(MazeGraph graph)
        {
            var span = graph.Width * UnitsPerCell;
            var x = (graph.Point(Cell).X * UnitsPerCell) + (Heading.Dx() * Progress);
            return ((x % span) + span) % span;
        }

        public int WorldY(MazeGraph graph) => (graph.Point(Cell).Y * UnitsPerCell) + (Heading.Dy() * Progress);

        /// <summary>周回を考慮した2物体の距離が半径以内か。</summary>
        public bool Touches(MazeActor other, MazeGraph graph, int radius)
        {
            var span = graph.Width * UnitsPerCell;
            long dx = Math.Abs(WorldX(graph) - other.WorldX(graph));
            dx = Math.Min(dx, span - dx);
            long dy = WorldY(graph) - other.WorldY(graph);
            return (dx * dx) + (dy * dy) <= (long)radius * radius;
        }
    }
}
