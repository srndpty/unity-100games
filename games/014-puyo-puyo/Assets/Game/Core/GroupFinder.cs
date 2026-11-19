using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>消える条件を満たした同色の連結。</summary>
    public sealed class SporeGroup
    {
        public SporeGroup(Cell color, IReadOnlyList<CellPos> cells)
        {
            Color = color;
            Cells = cells ?? throw new ArgumentNullException(nameof(cells));
        }

        public Cell Color { get; }
        public IReadOnlyList<CellPos> Cells { get; }
    }

    /// <summary>上下左右の連結を探す。非表示の段は連結にも消去にも数えない。</summary>
    public static class GroupFinder
    {
        public const int MinGroupSize = 4;

        private static readonly IReadOnlyList<SporeGroup> _none = Array.AsReadOnly(Array.Empty<SporeGroup>());

        public static IReadOnlyList<SporeGroup> Find(Board board)
        {
            if (board == null) { throw new ArgumentNullException(nameof(board)); }
            var visited = new bool[Board.Width * Board.VisibleHeight];
            var groups = new List<SporeGroup>();
            var stack = new Stack<CellPos>();
            for (var y = 0; y < Board.VisibleHeight; y++)
            {
                for (var x = 0; x < Board.Width; x++)
                {
                    var color = board.At(x, y);
                    if (!color.IsSpore() || visited[Board.Index(x, y)]) { continue; }
                    var cells = Collect(board, color, new CellPos(x, y), visited, stack);
                    if (cells.Count >= MinGroupSize) { groups.Add(new SporeGroup(color, cells.AsReadOnly())); }
                }
            }

            return groups.Count == 0 ? _none : groups.AsReadOnly();
        }

        private static List<CellPos> Collect(Board board, Cell color, CellPos start, bool[] visited, Stack<CellPos> stack)
        {
            var cells = new List<CellPos>();
            visited[Board.Index(start.X, start.Y)] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var cell = stack.Pop();
                cells.Add(cell);
                Visit(board, color, cell.X - 1, cell.Y, visited, stack);
                Visit(board, color, cell.X + 1, cell.Y, visited, stack);
                Visit(board, color, cell.X, cell.Y - 1, visited, stack);
                Visit(board, color, cell.X, cell.Y + 1, visited, stack);
            }

            return cells;
        }

        private static void Visit(Board board, Cell color, int x, int y, bool[] visited, Stack<CellPos> stack)
        {
            if (x < 0 || x >= Board.Width || y < 0 || y >= Board.VisibleHeight) { return; }
            if (visited[Board.Index(x, y)] || board.At(x, y) != color) { return; }
            visited[Board.Index(x, y)] = true;
            stack.Push(new CellPos(x, y));
        }
    }
}
