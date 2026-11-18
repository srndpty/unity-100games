using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>面の変化しないレイヤー（壁・床・目標）と初期配置。作成は <see cref="LevelParser"/> が行う。</summary>
    public sealed class LevelMap
    {
        public const int MaxWidth = 14;
        public const int MaxHeight = 10;
        public const int MaxBoxes = 8;

        private readonly bool[] _walls;
        private readonly bool[] _floors;
        private readonly bool[] _goals;

        internal LevelMap(int width, int height, bool[] walls, bool[] floors, bool[] goals, BoardState initial)
        {
            Width = width;
            Height = height;
            _walls = walls;
            _floors = floors;
            _goals = goals;
            Initial = initial;

            var goalCells = new List<Cell>();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (goals[(y * width) + x]) { goalCells.Add(new Cell(x, y)); }
                }
            }

            Goals = goalCells.AsReadOnly();
        }

        public int Width { get; }
        public int Height { get; }
        public BoardState Initial { get; }
        public IReadOnlyList<Cell> Goals { get; }

        public bool IsWall(Cell cell) => Contains(cell) && _walls[Index(cell)];

        /// <summary>壁に囲まれた内側のマス。目標も床に含む。外周より外の空白は床ではない。</summary>
        public bool IsFloor(Cell cell) => Contains(cell) && _floors[Index(cell)];

        public bool IsGoal(Cell cell) => Contains(cell) && _goals[Index(cell)];

        private bool Contains(Cell cell) => cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;

        private int Index(Cell cell) => (cell.Y * Width) + cell.X;
    }
}
