using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class MazeGraphTests
    {
        private static readonly Direction[] _moves = { Direction.Up, Direction.Left, Direction.Down, Direction.Right };

        private static MazeLayout Parse(params string[] rows) => new(rows);

        [Test]
        public void Default_HasExpectedSize()
        {
            var graph = new MazeGraph(MazeLayout.CreateDefault());
            Assert.That(graph.Width, Is.EqualTo(29));
            Assert.That(graph.Height, Is.EqualTo(15));
        }

        [Test]
        public void Default_IsLeftRightSymmetric()
        {
            foreach (var row in MazeLayout.CopyDefaultRows())
            {
                for (var x = 0; x < row.Length; x++)
                {
                    var mirrored = row[row.Length - 1 - x];
                    Assert.That(row[x] == '#', Is.EqualTo(mirrored == '#'), row);
                }
            }
        }

        [Test]
        public void Default_CorridorsHaveNoDeadEnds()
        {
            var graph = new MazeGraph(MazeLayout.CreateDefault());
            for (var cell = 0; cell < graph.CellCount; cell++)
            {
                if (graph.Kind(cell) != CellKind.Path) { continue; }
                var exits = 0;
                foreach (var move in _moves) { if (graph.CanEnter(cell, move, Traversal.Corridor)) { exits++; } }
                Assert.That(exits, Is.GreaterThanOrEqualTo(2), graph.Point(cell).ToString());
            }
        }

        [Test]
        public void Default_EdgesAreSymmetric()
        {
            var graph = new MazeGraph(MazeLayout.CreateDefault());
            for (var cell = 0; cell < graph.CellCount; cell++)
            {
                foreach (var move in _moves)
                {
                    var next = graph.Neighbor(cell, move);
                    if (next >= 0) { Assert.That(graph.Neighbor(next, move.Opposite()), Is.EqualTo(cell)); }
                }
            }
        }

        [Test]
        public void Tunnel_WrapsBetweenEdges()
        {
            var graph = new MazeGraph(MazeLayout.CreateDefault());
            var left = graph.CellId(0, 7);
            var right = graph.CellId(28, 7);
            Assert.That(graph.Neighbor(left, Direction.Left), Is.EqualTo(right));
            Assert.That(graph.Neighbor(right, Direction.Right), Is.EqualTo(left));
        }

        [Test]
        public void Walls_HaveNoEdges()
        {
            var graph = new MazeGraph(MazeLayout.CreateDefault());
            for (var cell = 0; cell < graph.CellCount; cell++)
            {
                foreach (var move in _moves)
                {
                    var next = graph.Neighbor(cell, move);
                    if (graph.Kind(cell) == CellKind.Wall) { Assert.That(next, Is.EqualTo(-1)); }
                    else if (next >= 0) { Assert.That(graph.Kind(next), Is.Not.EqualTo(CellKind.Wall)); }
                }
            }
        }

        [Test]
        public void Door_OnlyGateTraversalMayEnter()
        {
            var graph = new MazeGraph(MazeLayout.CreateDefault());
            Assert.That(graph.CanEnter(graph.ExitCell, Direction.Down, Traversal.Corridor), Is.False);
            Assert.That(graph.CanEnter(graph.ExitCell, Direction.Down, Traversal.Gate), Is.True);
        }

        [Test]
        public void DistanceFields_ReachEveryOpenCell()
        {
            var graph = new MazeGraph(MazeLayout.CreateDefault());
            Assert.That(graph.DistanceToHouse(graph.HouseCell), Is.Zero);
            Assert.That(graph.DistanceToExit(graph.ExitCell), Is.Zero);
            for (var cell = 0; cell < graph.CellCount; cell++)
            {
                if (graph.Kind(cell) == CellKind.Wall) { continue; }
                Assert.That(graph.DistanceToHouse(cell), Is.GreaterThanOrEqualTo(0));
                Assert.That(graph.DistanceToExit(cell), Is.GreaterThanOrEqualTo(0));
            }
        }

        [Test]
        public void Junction_IsDetected()
        {
            var graph = new MazeGraph(MazeLayout.CreateDefault());
            Assert.That(graph.IsJunction(graph.CellId(9, 4)), Is.True);
            Assert.That(graph.IsJunction(graph.CellId(3, 1)), Is.False);
        }

        [Test]
        public void Layout_RejectsMalformedInput()
        {
            Assert.Throws<ArgumentException>(() => new MazeLayout(Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => Parse("#P=", "#"));
            Assert.Throws<ArgumentException>(() => Parse("#P=?"));
            Assert.Throws<ArgumentException>(() => Parse("####"));
        }

        [Test]
        public void Graph_RejectsDeadEnd()
        {
            var rows = MazeLayout.CopyDefaultRows();
            rows[2] = "#.#######.####.####.#######.#".Remove(9, 1).Insert(9, "#");
            rows[3] = "#.#######.####.####.#######.#".Remove(9, 1).Insert(9, ".");
            Assert.Throws<ArgumentException>(() => new MazeGraph(new MazeLayout(rows)));
        }

        [Test]
        public void Graph_RejectsMazeWithoutPellets()
        {
            var rows = MazeLayout.CopyDefaultRows();
            for (var y = 0; y < rows.Length; y++) { rows[y] = rows[y].Replace('.', ' ').Replace('o', ' '); }
            Assert.Throws<ArgumentException>(() => new MazeGraph(new MazeLayout(rows)));
        }
    }
}
