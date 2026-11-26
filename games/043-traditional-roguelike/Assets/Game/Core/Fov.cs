using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>
    /// 視界。対称シャドウキャスティング（Albert Ford, 2021）を、傾きを整数の分数のまま計算して書いた。
    /// 床のマスどうしは「A から B が見える ⇔ B から A が見える」。壁は見えるが先を遮る。半径は dx² + dy² ≤ r² + r の円。
    /// 浮動小数点を使わないので Mono と .NET で同じ結果になる。
    /// </summary>
    public static class Fov
    {
        /// <summary><paramref name="visible"/>（盤の大きさの配列）を、<paramref name="origin"/> から見えるマスだけ true にする。</summary>
        public static void Compute(FloorGrid grid, Vec2i origin, int radius, bool[] visible)
        {
            if (grid == null) { throw new ArgumentNullException(nameof(grid)); }
            if (visible == null) { throw new ArgumentNullException(nameof(visible)); }
            if (visible.Length != grid.Width * grid.Height) { throw new ArgumentException("視界の配列の大きさが盤と違います。", nameof(visible)); }
            Array.Clear(visible, 0, visible.Length);
            if (!grid.InBounds(origin)) { return; }
            visible[grid.Index(origin)] = true;
            var limit = ((long)radius * radius) + radius;
            for (int quadrant = 0; quadrant < 4; quadrant++)
            {
                var scan = new Scanner(grid, origin, quadrant, radius, limit, visible);
                scan.Scan(1, -1, 1, 1, 1);
            }
        }

        /// <summary>1 つの象限（北・東・南・西）を走査する。depth は原点からの行、col は行の中の列。</summary>
        private readonly struct Scanner
        {
            private readonly FloorGrid _grid;
            private readonly Vec2i _origin;
            private readonly int _quadrant;
            private readonly int _radius;
            private readonly long _limit;
            private readonly bool[] _visible;

            public Scanner(FloorGrid grid, Vec2i origin, int quadrant, int radius, long limit, bool[] visible)
            {
                _grid = grid;
                _origin = origin;
                _quadrant = quadrant;
                _radius = radius;
                _limit = limit;
                _visible = visible;
            }

            /// <summary>行 depth を、傾き startNum/startDen から endNum/endDen まで走査する（分母は正）。</summary>
            public void Scan(int depth, long startNum, long startDen, long endNum, long endDen)
            {
                if (depth > _radius) { return; }

                // 列の範囲：round_ties_up(depth × start) から round_ties_down(depth × end)。
                var minCol = FloorDiv((2 * depth * startNum) + startDen, 2 * startDen);
                var maxCol = CeilDiv((2 * depth * endNum) - endDen, 2 * endDen);
                bool hasPrev = false;
                bool prevWall = false;
                for (var col = minCol; col <= maxCol; col++)
                {
                    var p = Transform(depth, (int)col);
                    var wall = !_grid.IsTransparent(p);

                    // 壁は範囲内なら見える。床は行の傾きの範囲に中心が入るときだけ（対称性）。
                    var symmetric = (col * startDen) >= (depth * startNum) && (col * endDen) <= (depth * endNum);
                    if ((wall || symmetric) && Within(depth, col) && _grid.InBounds(p))
                    {
                        _visible[_grid.Index(p)] = true;
                    }

                    if (hasPrev && prevWall && !wall)
                    {
                        // 新しい開始の傾き：(2col − 1) / (2depth)
                        startNum = (2 * col) - 1;
                        startDen = 2 * depth;
                    }

                    if (hasPrev && !prevWall && wall)
                    {
                        Scan(depth + 1, startNum, startDen, (2 * col) - 1, 2 * depth);
                    }

                    hasPrev = true;
                    prevWall = wall;
                }

                if (hasPrev && !prevWall)
                {
                    Scan(depth + 1, startNum, startDen, endNum, endDen);
                }
            }

            private bool Within(long depth, long col) => (depth * depth) + (col * col) <= _limit;

            private Vec2i Transform(int depth, int col) => _quadrant switch
            {
                0 => new Vec2i(_origin.X + col, _origin.Y - depth),
                1 => new Vec2i(_origin.X + depth, _origin.Y + col),
                2 => new Vec2i(_origin.X + col, _origin.Y + depth),
                _ => new Vec2i(_origin.X - depth, _origin.Y + col),
            };

            private static long FloorDiv(long a, long b)
            {
                var q = a / b;
                if ((a % b != 0) && ((a < 0) != (b < 0))) { q--; }
                return q;
            }

            private static long CeilDiv(long a, long b) => -FloorDiv(-a, b);
        }
    }
}
