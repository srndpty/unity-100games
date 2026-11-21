using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class LightsBoardTests
    {
        [Test]
        public void PressFlipsOnlyTheCellAndItsOrthogonalNeighbors()
        {
            var random = new SeededRandom(19);
            for (var trial = 0; trial < 20; trial++)
            {
                var board = LightsTestKit.RandomBoard(random);
                for (var y = 0; y < LightsBoard.Size; y++)
                {
                    for (var x = 0; x < LightsBoard.Size; x++)
                    {
                        var grid = LightsTestKit.ToGrid(board);
                        LightsTestKit.NaivePress(grid, x, y);
                        Assert.That(
                            board.Press(LightsBoard.IndexOf(x, y)), Is.EqualTo(LightsTestKit.FromGrid(grid)), $"({x}, {y})");
                    }
                }
            }
        }

        [TestCase(0, 0, ExpectedResult = 3)]
        [TestCase(4, 0, ExpectedResult = 3)]
        [TestCase(0, 4, ExpectedResult = 3)]
        [TestCase(4, 4, ExpectedResult = 3)]
        [TestCase(2, 0, ExpectedResult = 4)]
        [TestCase(0, 2, ExpectedResult = 4)]
        [TestCase(4, 3, ExpectedResult = 4)]
        [TestCase(2, 2, ExpectedResult = 5)]
        [TestCase(1, 3, ExpectedResult = 5)]
        public int PressDoesNotWrapAroundTheEdges(int x, int y) => LightsBoard.Dark.Press(LightsBoard.IndexOf(x, y)).LitCount;

        [Test]
        public void PressOnCornerFlipsExpectedCells()
        {
            var board = LightsBoard.Dark.Press(LightsBoard.IndexOf(4, 0));
            Assert.That(board, Is.EqualTo(LightsTestKit.Parse(
                "...##",
                "....#",
                ".....",
                ".....",
                ".....")));
        }

        [Test]
        public void PressingTheSameCellTwiceRestoresTheBoard()
        {
            var random = new SeededRandom(7);
            for (var trial = 0; trial < 50; trial++)
            {
                var board = LightsTestKit.RandomBoard(random);
                for (var index = 0; index < LightsBoard.CellCount; index++)
                {
                    Assert.That(board.Press(index).Press(index), Is.EqualTo(board));
                }
            }
        }

        [Test]
        public void PressOrderDoesNotMatter()
        {
            var random = new SeededRandom(3);
            for (var trial = 0; trial < 200; trial++)
            {
                var board = LightsTestKit.RandomBoard(random);
                var sequence = new int[8];
                for (var i = 0; i < sequence.Length; i++)
                {
                    sequence[i] = random.Next(LightsBoard.CellCount);
                }

                var forward = board;
                foreach (var index in sequence) { forward = forward.Press(index); }
                var backward = board;
                for (var i = sequence.Length - 1; i >= 0; i--) { backward = backward.Press(sequence[i]); }

                Assert.That(backward, Is.EqualTo(forward));
            }
        }

        [Test]
        public void PressAllMatchesPressingEachCellOnce()
        {
            var random = new SeededRandom(11);
            for (var trial = 0; trial < 200; trial++)
            {
                var board = LightsTestKit.RandomBoard(random);
                var presses = random.NextUInt() & LightsBoard.AllCells;
                var expected = board;
                for (var index = 0; index < LightsBoard.CellCount; index++)
                {
                    if ((presses & (1u << index)) != 0) { expected = expected.Press(index); }
                }

                Assert.That(board.PressAll(presses), Is.EqualTo(expected));
            }
        }

        [Test]
        public void DarkBoardHasNoLights()
        {
            Assert.That(LightsBoard.Dark.IsDark, Is.True);
            Assert.That(LightsBoard.Dark.LitCount, Is.Zero);
            Assert.That(new LightsBoard(LightsBoard.AllCells).LitCount, Is.EqualTo(25));
            Assert.That(new LightsBoard(1).IsDark, Is.False);
        }

        [TestCase(-1)]
        [TestCase(25)]
        [TestCase(int.MaxValue)]
        public void OutOfRangeIndexIsRejected(int index)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LightsBoard.Dark.Press(index));
            Assert.Throws<ArgumentOutOfRangeException>(() => LightsBoard.Dark.IsLit(index));
            Assert.Throws<ArgumentOutOfRangeException>(() => PressMasks.Of(index));
        }

        [TestCase(-1, 0)]
        [TestCase(5, 0)]
        [TestCase(0, -1)]
        [TestCase(0, 5)]
        public void OutOfRangeCoordinatesAreRejected(int x, int y)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LightsBoard.IndexOf(x, y));
        }

        [Test]
        public void BitsBeyondTheBoardAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new LightsBoard(1u << 25));
            Assert.Throws<ArgumentOutOfRangeException>(() => LightsBoard.Dark.PressAll(1u << 25));
        }

        [Test]
        public void PressMaskTableCannotBeModified()
        {
            Assert.That(PressMasks.All, Has.Count.EqualTo(25));
            Assert.That(PressMasks.All, Is.Not.InstanceOf<uint[]>());
        }

        [Test]
        public void BitCountMatchesNaiveCount()
        {
            var random = new SeededRandom(5);
            for (var trial = 0; trial < 1000; trial++)
            {
                var value = random.NextUInt();
                Assert.That(BitCount.Of(value), Is.EqualTo(LightsTestKit.PopCount(value)));
            }

            Assert.That(BitCount.Of(0), Is.Zero);
            Assert.That(BitCount.Of(uint.MaxValue), Is.EqualTo(32));
        }
    }
}
