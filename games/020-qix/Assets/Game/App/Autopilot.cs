using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// 撮影とテストで使う操縦役。縁から海へ「コ」の字の線を引いて四角く切り取る。
    /// 敵から十分離れた切り取り方のうち面積が最大のものを選び、なければ縁を通って敵から最も遠い縁へ向かう。
    /// 盤と敵の状態だけで決まるので、同じ試合なら同じ動きになる。
    /// </summary>
    public sealed class Autopilot
    {
        private static readonly int[] _depths = { 1, 2, 3, 4, 6, 8, 12, 16, 20 };
        private static readonly int[] _lengths = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32 };
        private static readonly Direction[] _all = { Direction.Up, Direction.Down, Direction.Left, Direction.Right };

        private readonly List<Direction> _plan = new();
        private readonly List<(int X, int Y)> _waypoints = new();
        private readonly List<Direction> _candidate = new();
        private readonly List<(int X, int Y)> _candidateCells = new();
        private readonly HashSet<int> _visited = new();
        private int _next;

        /// <summary>敵の半径に足す余裕（セル）。線の長さに応じてさらに広げる。</summary>
        public int MarginCells { get; set; } = 3;

        public void Reset()
        {
            _plan.Clear();
            _waypoints.Clear();
            _next = 0;
        }

        /// <summary>この Tick の入力（向きと描画キー）。</summary>
        public (Direction Direction, bool Draw) Next(QixGame game)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            if (game.IsOver) { return (Direction.None, false); }

            var here = (game.PlayerX, game.PlayerY);
            while (_next < _plan.Count && _waypoints[_next] == here) { _next++; }

            // 1 歩目を出した後は、閉じるかミスするまで線を描いている。描いていなければ（閉じた先から縁へ移された、
            // ミスで戻されたなど）、いるはずの場所にいなければ、その計画は終わっている。
            var started = _next > 0;
            if (_next >= _plan.Count || (started && (!game.IsDrawing || _waypoints[_next - 1] != here)))
            {
                Reset();
            }

            if (_plan.Count == 0 && !game.IsDrawing && !PlanBite(game))
            {
                return (Patrol(game), false);
            }

            if (_plan.Count == 0)
            {
                // 描いている途中で計画を失った（通常は起きない）。描画キーを離して止まる。
                return (Direction.None, false);
            }

            return (_plan[_next], true);
        }

        private bool PlanBite(QixGame game)
        {
            var best = 0;
            foreach (var inward in _all)
            {
                QixGame.Offset(inward, out var ix, out var iy);
                var fx = game.PlayerX + ix;
                var fy = game.PlayerY + iy;
                if (!Inside(game, fx, fy) || game.CellAt(fx, fy) != Cell.Open) { continue; }

                foreach (var side in Perpendicular(inward))
                {
                    foreach (var depth in _depths)
                    {
                        foreach (var length in _lengths)
                        {
                            var area = depth * length;
                            if (area <= best || !Simulate(game, inward, side, depth, length)) { continue; }
                            best = area;
                            _plan.Clear();
                            _plan.AddRange(_candidate);
                            _waypoints.Clear();
                            _waypoints.AddRange(_candidateCells);
                        }
                    }
                }
            }

            _next = 0;
            return best > 0;
        }

        /// <summary>内側へ depth、横へ length、外側へ陸に着くまで進む線を試す。安全なら候補に入れる。</summary>
        private bool Simulate(QixGame game, Direction inward, Direction side, int depth, int length)
        {
            _candidate.Clear();
            _candidateCells.Clear();
            _visited.Clear();
            var x = game.PlayerX;
            var y = game.PlayerY;
            var legs = new[] { (inward, depth), (side, length), (Opposite(inward), game.Width + game.Height) };
            for (var leg = 0; leg < legs.Length; leg++)
            {
                var (direction, steps) = legs[leg];
                QixGame.Offset(direction, out var dx, out var dy);
                for (var i = 0; i < steps; i++)
                {
                    x += dx;
                    y += dy;
                    if (!Inside(game, x, y) || !_visited.Add(y * game.Width + x)) { return false; }
                    _candidate.Add(direction);
                    _candidateCells.Add((x, y));
                    var cell = game.CellAt(x, y);
                    if (cell == Cell.Claimed)
                    {
                        // 最後の辺で陸に着いたら閉じる。それより前に着く線は形が崩れるので選ばない。
                        return leg == legs.Length - 1 && IsSafe(game);
                    }

                    if (cell != Cell.Open) { return false; }
                }
            }

            return false;
        }

        /// <summary>線のどのセルも、敵の半径と余裕（線が長いほど広い）より離れているか。</summary>
        private bool IsSafe(QixGame game)
        {
            const int s = QixConfig.SubCell;
            var enemy = game.Enemy;
            var margin = (long)enemy.Radius + (MarginCells * s) + (_candidate.Count * s / 4);
            foreach (var (x, y) in _candidateCells)
            {
                long dx = (x * s) + (s / 2) - enemy.X;
                long dy = (y * s) + (s / 2) - enemy.Y;
                if (dx * dx + dy * dy < margin * margin) { return false; }
            }

            return true;
        }

        /// <summary>縁を通って、敵から最も遠い縁のセルへ向かう 1 歩目。</summary>
        private static Direction Patrol(QixGame game)
        {
            var start = game.PlayerY * game.Width + game.PlayerX;
            var first = new Dictionary<int, Direction> { [start] = Direction.None };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            var best = start;
            var bestDistance = Distance(game, game.PlayerX, game.PlayerY);
            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var x = index % game.Width;
                var y = index / game.Width;
                foreach (var direction in _all)
                {
                    QixGame.Offset(direction, out var dx, out var dy);
                    var nx = x + dx;
                    var ny = y + dy;
                    if (!Inside(game, nx, ny) || !game.IsBoundary(nx, ny)) { continue; }
                    var next = ny * game.Width + nx;
                    if (first.ContainsKey(next)) { continue; }
                    first[next] = index == start ? direction : first[index];
                    queue.Enqueue(next);
                    var distance = Distance(game, nx, ny);
                    if (distance > bestDistance)
                    {
                        best = next;
                        bestDistance = distance;
                    }
                }
            }

            return first[best];
        }

        private static long Distance(QixGame game, int x, int y)
        {
            const int s = QixConfig.SubCell;
            long dx = (x * s) + (s / 2) - game.Enemy.X;
            long dy = (y * s) + (s / 2) - game.Enemy.Y;
            return dx * dx + dy * dy;
        }

        private static bool Inside(QixGame game, int x, int y) => x >= 0 && y >= 0 && x < game.Width && y < game.Height;

        private static Direction Opposite(Direction direction) => direction switch
        {
            Direction.Up => Direction.Down,
            Direction.Down => Direction.Up,
            Direction.Left => Direction.Right,
            _ => Direction.Left,
        };

        private static Direction[] Perpendicular(Direction direction) =>
            direction == Direction.Up || direction == Direction.Down
                ? new[] { Direction.Left, Direction.Right }
                : new[] { Direction.Up, Direction.Down };
    }
}
