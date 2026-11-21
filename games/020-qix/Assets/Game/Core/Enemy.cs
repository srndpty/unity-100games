using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// 海を動き回る円の敵。位置・速度・半径は 1/<see cref="QixConfig.SubCell"/> セル単位の整数。
    /// 1 Tick の移動は 1 単位ずつの歩み（ブレゼンハム）に分け、歩むたびに陸での反射と線への接触を調べる。
    /// したがって接触は「触れた時点の位置」で決まり、Tick 終了時の位置だけを見る判定のような見逃しがない。
    /// </summary>
    public sealed class Enemy
    {
        private const int DirectionScale = 1024;

        // 軸に平行な向きを除いた 12 方向（22.5 度刻み）。cos・sin を 1024 倍した整数。
        private static readonly int[] _directionX = { 946, 724, 392, -392, -724, -946, -946, -724, -392, 392, 724, 946 };
        private static readonly int[] _directionY = { 392, 724, 946, 946, 724, 392, -392, -724, -946, -946, -724, -392 };

        private readonly SeededRandom _random;
        private readonly int _speed;
        private readonly int _turnMin;
        private readonly int _turnMax;
        private int _turnCountdown;

        internal Enemy(QixConfig config, SeededRandom random, int x, int y, int velocityX, int velocityY)
        {
            _random = random;
            _speed = config.EnemySpeed;
            _turnMin = config.EnemyTurnMinTicks;
            _turnMax = config.EnemyTurnMaxTicks;
            Radius = config.EnemyRadius;
            X = x;
            Y = y;
            VelocityX = velocityX;
            VelocityY = velocityY;
            ResetTurnCountdown();
        }

        /// <summary>中心の x（1/SubCell セル単位）。</summary>
        public int X { get; private set; }

        /// <summary>中心の y（1/SubCell セル単位）。</summary>
        public int Y { get; private set; }

        public int VelocityX { get; private set; }

        public int VelocityY { get; private set; }

        public int Radius { get; }

        /// <summary>乱数で向きと位置を決める。外周の陸に重ならない範囲に置く。</summary>
        internal static Enemy Spawn(QixConfig config, SeededRandom random)
        {
            const int s = QixConfig.SubCell;
            var r = config.EnemyRadius;
            var x = s + r + random.Next((config.Width - 2) * s - 2 * r + 1);
            var y = s + r + random.Next((config.Height - 2) * s - 2 * r + 1);
            var enemy = new Enemy(config, random, x, y, 0, 0);
            enemy.Turn();
            return enemy;
        }

        /// <summary>中心が (x, y) のとき、円がセルに重なるか（接するだけなら重ならない）。</summary>
        public bool Overlaps(int cellX, int cellY) => Overlaps(X, Y, cellX, cellY);

        /// <summary>円が重なるセルの番号を <paramref name="result"/> に入れる。</summary>
        public void OverlappedCells(CellGrid grid, List<int> result)
        {
            if (grid == null) { throw new ArgumentNullException(nameof(grid)); }
            if (result == null) { throw new ArgumentNullException(nameof(result)); }

            result.Clear();
            Bounds(grid, X, Y, out var x0, out var y0, out var x1, out var y1);
            for (var cy = y0; cy <= y1; cy++)
            {
                for (var cx = x0; cx <= x1; cx++)
                {
                    if (Overlaps(X, Y, cx, cy))
                    {
                        result.Add(grid.Index(cx, cy));
                    }
                }
            }
        }

        /// <summary>1 Tick 進める。線に触れたら、触れた位置で止まって true を返す。</summary>
        internal bool Step(CellGrid grid)
        {
            if (--_turnCountdown <= 0)
            {
                Turn();
            }

            var ax = Math.Abs(VelocityX);
            var ay = Math.Abs(VelocityY);
            var steps = Math.Max(ax, ay);
            var errorX = 0;
            var errorY = 0;
            for (var i = 0; i < steps; i++)
            {
                errorX += ax;
                if (errorX >= steps)
                {
                    errorX -= steps;
                    if (StepAxis(grid, horizontal: true)) { return true; }
                }

                errorY += ay;
                if (errorY >= steps)
                {
                    errorY -= steps;
                    if (StepAxis(grid, horizontal: false)) { return true; }
                }
            }

            return false;
        }

        /// <summary>状態のハッシュ（FNV-1a）。</summary>
        public uint Hash(uint hash)
        {
            unchecked
            {
                foreach (var value in new[] { X, Y, VelocityX, VelocityY, _turnCountdown })
                {
                    hash = (hash ^ (uint)value) * 16777619u;
                }
            }

            return hash;
        }

        private bool StepAxis(CellGrid grid, bool horizontal)
        {
            var nx = horizontal ? X + Math.Sign(VelocityX) : X;
            var ny = horizontal ? Y : Y + Math.Sign(VelocityY);
            var touchesTrail = false;
            var touchesLand = false;
            Bounds(grid, nx, ny, out var x0, out var y0, out var x1, out var y1);
            for (var cy = y0; cy <= y1; cy++)
            {
                for (var cx = x0; cx <= x1; cx++)
                {
                    var cell = grid[cx, cy];
                    if (cell == Cell.Open || !Overlaps(nx, ny, cx, cy))
                    {
                        continue;
                    }

                    if (cell == Cell.Trail) { touchesTrail = true; }
                    else { touchesLand = true; }
                }
            }

            // 線に触れるならそこで止まる（陸より優先）。陸に触れるなら進まずにその軸の向きを反転する。
            if (touchesTrail)
            {
                X = nx;
                Y = ny;
                return true;
            }

            if (touchesLand)
            {
                if (horizontal) { VelocityX = -VelocityX; }
                else { VelocityY = -VelocityY; }
                return false;
            }

            X = nx;
            Y = ny;
            return false;
        }

        private void Turn()
        {
            var direction = _random.Next(_directionX.Length);
            VelocityX = Scale(_directionX[direction]);
            VelocityY = Scale(_directionY[direction]);
            ResetTurnCountdown();
        }

        private int Scale(int component)
        {
            var value = component * _speed / DirectionScale;
            return value != 0 ? value : Math.Sign(component);
        }

        private void ResetTurnCountdown() => _turnCountdown = _turnMin + _random.Next(_turnMax - _turnMin + 1);

        private bool Overlaps(int centerX, int centerY, int cellX, int cellY)
        {
            const int s = QixConfig.SubCell;
            var nearestX = Math.Clamp(centerX, cellX * s, (cellX + 1) * s);
            var nearestY = Math.Clamp(centerY, cellY * s, (cellY + 1) * s);
            long dx = centerX - nearestX;
            long dy = centerY - nearestY;
            return dx * dx + dy * dy < (long)Radius * Radius;
        }

        private void Bounds(CellGrid grid, int centerX, int centerY, out int x0, out int y0, out int x1, out int y1)
        {
            const int s = QixConfig.SubCell;
            x0 = Math.Max(0, (centerX - Radius) / s);
            y0 = Math.Max(0, (centerY - Radius) / s);
            x1 = Math.Min(grid.Width - 1, (centerX + Radius) / s);
            y1 = Math.Min(grid.Height - 1, (centerY + Radius) / s);
        }
    }
}
