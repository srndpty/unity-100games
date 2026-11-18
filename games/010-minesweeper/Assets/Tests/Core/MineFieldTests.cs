using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class MineFieldTests
    {
        private static readonly BoardConfig[] _configs =
        {
            new(),
            new() { Width = 4, Height = 4, MineCount = 1 },
            new() { Width = 4, Height = 4, MineCount = 7 },
            new() { Width = 9, Height = 5, MineCount = 36 },
            new() { Width = 32, Height = 32, MineCount = 1015 },
        };

        private static List<int> Mines(MineField field)
        {
            var mines = new List<int>();
            for (var cell = 0; cell < field.Grid.CellCount; cell++)
            {
                if (field.IsMine(cell)) { mines.Add(cell); }
            }

            return mines;
        }

        [Test]
        public void Generate_EveryFirstCell_KeepsProtectedCellsClearAndMineCountExact([ValueSource(nameof(_configs))] BoardConfig config)
        {
            var grid = new BoardGrid(config.Width, config.Height);
            for (var first = 0; first < grid.CellCount; first++)
            {
                var firstX = grid.X(first);
                var firstY = grid.Y(first);
                var field = MineField.Generate(config, first * 31, firstX, firstY);
                Assert.That(field.MineCount, Is.EqualTo(config.MineCount));
                Assert.That(Mines(field), Has.Count.EqualTo(config.MineCount));
                for (var y = firstY - 1; y <= firstY + 1; y++)
                {
                    for (var x = firstX - 1; x <= firstX + 1; x++)
                    {
                        if (grid.Contains(x, y)) { Assert.That(field.IsMine(grid.Index(x, y)), Is.False, $"初手({firstX},{firstY})の近傍({x},{y})"); }
                    }
                }
            }
        }

        [Test]
        public void Adjacent_MatchesBruteForceCount([ValueSource(nameof(_configs))] BoardConfig config)
        {
            for (var seed = 0; seed < 20; seed++)
            {
                var field = MineField.Generate(config, seed, seed % config.Width, seed % config.Height);
                var grid = field.Grid;
                for (var cell = 0; cell < grid.CellCount; cell++)
                {
                    var expected = 0;
                    for (var other = 0; other < grid.CellCount; other++)
                    {
                        var isNear = other != cell && Math.Abs(grid.X(other) - grid.X(cell)) <= 1 && Math.Abs(grid.Y(other) - grid.Y(cell)) <= 1;
                        if (isNear && field.IsMine(other)) { expected++; }
                    }

                    Assert.That(field.Adjacent(cell), Is.EqualTo(expected), $"セル {cell}");
                }
            }
        }

        [Test]
        public void Generate_SameSeedAndFirstCell_ReproducesBoard()
        {
            var config = new BoardConfig();
            Assert.That(Mines(MineField.Generate(config, 777, 3, 9)), Is.EqualTo(Mines(MineField.Generate(config, 777, 3, 9))));
        }

        [Test]
        public void Generate_DifferentSeed_ChangesBoard()
        {
            var config = new BoardConfig();
            Assert.That(Mines(MineField.Generate(config, 777, 3, 9)), Is.Not.EqualTo(Mines(MineField.Generate(config, 778, 3, 9))));
        }

        [Test]
        public void Generate_FixedSeed_MatchesKnownLayout()
        {
            var config = new BoardConfig { Width = 8, Height = 8, MineCount = 10 };
            Assert.That(string.Join(",", Mines(MineField.Generate(config, 12345, 0, 0))), Is.EqualTo("3,16,20,27,29,32,40,42,43,49"));
        }

        [Test]
        public void Generate_RejectsFirstCellOutsideBoard()
        {
            var config = new BoardConfig();
            Assert.Throws<ArgumentOutOfRangeException>(() => MineField.Generate(config, 1, -1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MineField.Generate(config, 1, 0, 16));
        }

        [TestCase(3, 16, 1)]
        [TestCase(33, 16, 1)]
        [TestCase(16, 3, 1)]
        [TestCase(16, 33, 1)]
        [TestCase(16, 16, 0)]
        [TestCase(16, 16, 248)]
        [TestCase(4, 4, 8)]
        public void Validate_RejectsOutOfRangeValues(int width, int height, int mines)
        {
            var config = new BoardConfig { Width = width, Height = height, MineCount = mines };
            Assert.Throws<ArgumentOutOfRangeException>(config.Validate);
        }

        [TestCase(4, 4, 7)]
        [TestCase(16, 16, 247)]
        [TestCase(32, 32, 1015)]
        public void Validate_AcceptsBoundaryValues(int width, int height, int mines)
        {
            var config = new BoardConfig { Width = width, Height = height, MineCount = mines };
            Assert.DoesNotThrow(config.Validate);
        }
    }
}
