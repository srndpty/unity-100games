using System;

namespace Game.Core
{
    /// <summary>爆弾の配置と隣接数。生成後は変わらない「答え」で、プレイヤーの操作や表示の状態は持たない。</summary>
    public sealed class MineField
    {
        private readonly bool[] _mines;
        private readonly byte[] _adjacent;

        private MineField(BoardGrid grid, bool[] mines)
        {
            Grid = grid;
            _mines = mines;
            _adjacent = new byte[grid.CellCount];
            var neighbors = new int[BoardGrid.MaxNeighbors];
            for (var cell = 0; cell < _mines.Length; cell++)
            {
                if (!_mines[cell]) { continue; }
                MineCount++;
                var count = grid.Neighbors(cell, neighbors);
                for (var i = 0; i < count; i++) { _adjacent[neighbors[i]]++; }
            }
        }

        public BoardGrid Grid { get; }
        public int MineCount { get; }

        /// <summary>初手のセルと周囲8セルを除いた候補から、シードで決まる爆弾配置を作る。</summary>
        public static MineField Generate(BoardConfig config, int seed, int safeX, int safeY)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            var grid = new BoardGrid(config.Width, config.Height);
            if (!grid.Contains(safeX, safeY)) { throw new ArgumentOutOfRangeException(nameof(safeX), "初手のセルが盤面の外です。"); }

            var candidates = new int[grid.CellCount];
            var candidateCount = 0;
            for (var cell = 0; cell < grid.CellCount; cell++)
            {
                if (Math.Abs(grid.X(cell) - safeX) > 1 || Math.Abs(grid.Y(cell) - safeY) > 1) { candidates[candidateCount++] = cell; }
            }

            // 部分 Fisher–Yates。先頭から爆弾の数だけ、未選択の候補から一様に選ぶ。
            var random = new SeededRandom(seed);
            var mines = new bool[grid.CellCount];
            for (var i = 0; i < config.MineCount; i++)
            {
                var pick = i + random.Next(candidateCount - i);
                (candidates[i], candidates[pick]) = (candidates[pick], candidates[i]);
                mines[candidates[i]] = true;
            }

            return new MineField(grid, mines);
        }

        /// <summary>爆弾の位置を直接指定して作る。テストと撮影の場面作り用。</summary>
        public static MineField FromMines(int width, int height, params int[] mineCells)
        {
            if (mineCells == null) { throw new ArgumentNullException(nameof(mineCells)); }
            var grid = new BoardGrid(width, height);
            var mines = new bool[grid.CellCount];
            foreach (var cell in mineCells)
            {
                if (cell < 0 || cell >= mines.Length) { throw new ArgumentOutOfRangeException(nameof(mineCells)); }
                mines[cell] = true;
            }

            return new MineField(grid, mines);
        }

        public bool IsMine(int cell) => _mines[cell];

        public int Adjacent(int cell) => _adjacent[cell];
    }
}
