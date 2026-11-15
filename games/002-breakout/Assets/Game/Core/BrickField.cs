using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>ブロック 1 個。位置はマス目で決まり、当たると残り回数が減る。</summary>
    public sealed class Brick
    {
        internal Brick(int index, BrickPlacement placement, float minX, float minY, float maxX, float maxY)
        {
            Index = index;
            Column = placement.Column;
            Row = placement.Row;
            Kind = placement.Kind;
            HitsRemaining = placement.Kind.MaxHits();
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        /// <summary>面の中での番号（左上から行優先）。同時に当たったときの最後の決め手にもなる。</summary>
        public int Index { get; }

        public int Column { get; }

        public int Row { get; }

        public BrickKind Kind { get; }

        public int HitsRemaining { get; private set; }

        public bool IsAlive => HitsRemaining > 0;

        public float MinX { get; }

        public float MinY { get; }

        public float MaxX { get; }

        public float MaxY { get; }

        public Vec2 Center => new((MinX + MaxX) / 2f, (MinY + MaxY) / 2f);

        /// <summary>1 発当てる。壊れたら true。</summary>
        internal bool Hit()
        {
            HitsRemaining--;
            return HitsRemaining == 0;
        }

        /// <summary>残り回数に関係なく壊す（貫通弾）。</summary>
        internal void Destroy() => HitsRemaining = 0;
    }

    /// <summary>1 面ぶんのブロック。マス目の位置から隣のブロックを引ける。</summary>
    public sealed class BrickField
    {
        private readonly Brick?[,] _grid;
        private readonly List<Brick> _bricks = new();

        public BrickField(BreakoutConfig config, StageLayout layout)
        {
            _grid = new Brick?[layout.Columns, layout.Rows];
            var left = -config.FieldWidth / 2f;
            foreach (var placement in layout.Bricks)
            {
                var minX = left + (placement.Column * config.BrickWidth);
                var maxY = config.GridTop - (placement.Row * config.BrickHeight);
                var brick = new Brick(_bricks.Count, placement, minX, maxY - config.BrickHeight, minX + config.BrickWidth, maxY);
                _bricks.Add(brick);
                _grid[placement.Column, placement.Row] = brick;
            }

            AliveCount = _bricks.Count;
        }

        public IReadOnlyList<Brick> Bricks => _bricks;

        public int AliveCount { get; private set; }

        /// <summary>壊れていないブロックが (column, row) にあるか。範囲外は false。</summary>
        public bool HasAliveAt(int column, int row) =>
            column >= 0 && row >= 0 && column < _grid.GetLength(0) && row < _grid.GetLength(1) && _grid[column, row] is { IsAlive: true };

        /// <summary>1 発当てる。壊れたら true。</summary>
        internal bool Hit(Brick brick)
        {
            if (!brick.Hit())
            {
                return false;
            }

            AliveCount--;
            return true;
        }

        internal void Destroy(Brick brick)
        {
            brick.Destroy();
            AliveCount--;
        }
    }
}
