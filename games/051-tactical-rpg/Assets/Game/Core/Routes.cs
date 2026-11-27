using System;
using System.Collections.Generic;

namespace Game.Core
{
    public sealed class Routes
    {
        private readonly int[] _cost = new int[Battle.CellCount];
        private readonly int[] _parent = new int[Battle.CellCount];

        internal Routes(Battle battle, int id, int start, int budget)
            : this(battle, id, cell => cell == start, budget, reverse: false) { }

        internal Routes(Battle battle, int id, Func<int, bool> isSource, int budget, bool reverse)
        {
            Array.Fill(_cost, int.MaxValue);
            Array.Fill(_parent, -1);
            for (var cell = 0; cell < Battle.CellCount; cell++)
            {
                if (Passable(battle, id, cell) && isSource(cell)) { _cost[cell] = 0; }
            }
            var visited = new bool[Battle.CellCount];
            // 64マス固定なので最小候補を走査して取り出す。同コストではセル番号順。
            for (var i = 0; i < Battle.CellCount; i++)
            {
                var next = -1;
                var minimum = int.MaxValue;
                for (var cell = 0; cell < Battle.CellCount; cell++)
                {
                    if (visited[cell] || _cost[cell] >= minimum) { continue; }
                    minimum = _cost[cell];
                    next = cell;
                }

                if (next < 0) { break; }
                visited[next] = true;
                for (var cell = 0; cell < Battle.CellCount; cell++)
                {
                    if (Battle.Distance(next, cell) != 1 || !Passable(battle, id, cell)) { continue; }
                    var cost = minimum + (Battle.Terrain(reverse ? next : cell) == Ground.Woods ? 2 : 1);
                    if (cost > budget || cost >= _cost[cell]) { continue; }
                    _cost[cell] = cost;
                    _parent[cell] = next;
                }
            }
        }

        private static bool Passable(Battle battle, int id, int cell)
        {
            var occupant = battle.Occupant(cell);
            return Battle.Terrain(cell) != Ground.Rock && (occupant < 0 || occupant == id);
        }

        public int Cost(int cell) => Battle.ValidCell(cell) ? _cost[cell] : int.MaxValue;
        public bool Reaches(int cell) => Cost(cell) != int.MaxValue;

        public IReadOnlyList<int> Path(int cell)
        {
            if (!Reaches(cell)) { return Array.Empty<int>(); }
            var cells = new List<int>();
            for (var next = cell; next >= 0; next = _parent[next]) { cells.Add(next); }
            cells.Reverse();
            return cells.AsReadOnly();
        }
    }
}
