using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    /// <summary>
    /// テストと撮影で共有する操縦役。危険な猫の近くを避けながら最寄りの餌へ最短経路で向かう。
    /// 往復に陥らないよう、探索の方向順をシード付き乱数でばらつかせる。
    /// </summary>
    public sealed class Autopilot
    {
        private const int DangerRadius = 3;
        private const int CloseRadius = 1;
        private const int HuntMarginTicks = 60;
        private readonly SeededRandom _random;
        private readonly Queue<int> _queue = new();
        private int[] _firstStep = Array.Empty<int>();

        public Autopilot(int seed)
        {
            _random = new SeededRandom(seed);
        }

        public Direction Next(MazeGameState game)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            var graph = game.Graph;
            var player = game.Player;
            var origin = player.Progress == 0 ? player.Cell : graph.Neighbor(player.Cell, player.Heading);
            if (player.Progress > 0 && IsDangerous(game, origin, CloseRadius)) { return player.Heading.Opposite(); }

            var toward = game.PowerTicksLeft > HuntMarginTicks ? Search(game, origin, true, DangerRadius) : Direction.None;
            if (toward == Direction.None) { toward = Search(game, origin, false, DangerRadius); }

            // 猫のそばに餌が残ると安全な経路がなくなるので、避ける範囲を狭めて探し直す。
            if (toward == Direction.None) { toward = Search(game, origin, false, CloseRadius); }
            return toward != Direction.None ? toward : Flee(game, origin);
        }

        private static bool IsGoal(MazeGameState game, int cell, bool hunting)
        {
            if (!hunting) { return game.PelletAt(cell) != Pellet.None; }
            for (var i = 0; i < MazeGameConfig.CatCount; i++)
            {
                var cat = game.Cat(i);
                if (cat.Mode == CatMode.Frightened && cat.Actor.Cell == cell) { return true; }
            }

            return false;
        }

        private Direction Search(MazeGameState game, int origin, bool hunting, int avoidRadius)
        {
            var graph = game.Graph;
            if (_firstStep.Length != graph.CellCount) { _firstStep = new int[graph.CellCount]; }
            Array.Fill(_firstStep, -1);
            _queue.Clear();
            _queue.Enqueue(origin);
            _firstStep[origin] = DirectionExtensions.Count;
            var offset = _random.Next(DirectionExtensions.Count);
            while (_queue.Count > 0)
            {
                var cell = _queue.Dequeue();
                if (cell != origin && IsGoal(game, cell, hunting)) { return (Direction)_firstStep[cell]; }
                for (var i = 0; i < DirectionExtensions.Count; i++)
                {
                    var direction = (Direction)((i + offset) % DirectionExtensions.Count);
                    if (!graph.CanEnter(cell, direction, Traversal.Corridor)) { continue; }
                    var next = graph.Neighbor(cell, direction);
                    if (_firstStep[next] >= 0 || IsDangerous(game, next, avoidRadius)) { continue; }
                    _firstStep[next] = cell == origin ? (int)direction : _firstStep[cell];
                    _queue.Enqueue(next);
                }
            }

            return Direction.None;
        }

        private static Direction Flee(MazeGameState game, int origin)
        {
            var graph = game.Graph;
            var best = Direction.None;
            var bestDistance = -1;
            for (var i = 0; i < DirectionExtensions.Count; i++)
            {
                var direction = (Direction)i;
                if (!graph.CanEnter(origin, direction, Traversal.Corridor)) { continue; }
                var distance = NearestDanger(game, graph.Neighbor(origin, direction));
                if (distance > bestDistance)
                {
                    best = direction;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static bool IsDangerous(MazeGameState game, int cell, int radius) => NearestDanger(game, cell) <= radius;

        private static int NearestDanger(MazeGameState game, int cell)
        {
            var graph = game.Graph;
            var point = graph.Point(cell);
            var nearest = int.MaxValue;
            for (var i = 0; i < MazeGameConfig.CatCount; i++)
            {
                var cat = game.Cat(i);
                if (!cat.IsDangerous) { continue; }
                var other = graph.Point(cat.Actor.Cell);
                var dx = Math.Abs(point.X - other.X);
                dx = Math.Min(dx, graph.Width - dx);
                nearest = Math.Min(nearest, dx + Math.Abs(point.Y - other.Y));
            }

            return nearest;
        }
    }
}
