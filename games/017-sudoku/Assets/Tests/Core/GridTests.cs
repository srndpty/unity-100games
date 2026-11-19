using System;
using NUnit.Framework;
using static Game.Core.Tests.SudokuTestKit;

namespace Game.Core.Tests
{
    public sealed class GridTests
    {
        [Test]
        public void ParseAndToStringRoundTrip()
        {
            Assert.That(Easy.ToString(), Is.EqualTo(EasyText));
            Assert.That(Easy[0], Is.EqualTo(5));
            Assert.That(Easy[2], Is.EqualTo(0));
            Assert.That(Easy.FilledCount, Is.EqualTo(30));
        }

        [Test]
        public void ParseSkipsLayoutCharactersAndAcceptsZeroAsEmpty()
        {
            var text = "530|070|000\n" + EasyText.Substring(9).Replace('.', '0');
            Assert.That(Grid.Parse(text), Is.EqualTo(Easy));
        }

        [TestCase("")]
        [TestCase("123")]
        [TestCase(EasyText + "1")]
        [TestCase("x" + "................................................................................")]
        public void ParseRejectsMalformedText(string text)
        {
            Assert.Throws<FormatException>(() => Grid.Parse(text));
        }

        [Test]
        public void WithReturnsANewGridAndLeavesTheOriginal()
        {
            var original = Easy;
            var changed = original.With(2, 4);
            Assert.That(changed[2], Is.EqualTo(4));
            Assert.That(original[2], Is.EqualTo(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => original.With(81, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => original.With(-1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => original.With(0, 10));
        }

        [Test]
        public void ToDigitsReturnsACopy()
        {
            var grid = Easy;
            var digits = grid.ToDigits();
            digits[0] = 9;
            Assert.That(grid[0], Is.EqualTo(5));
        }

        [TestCase(2, 5, 0, TestName = "同じ行")]
        [TestCase(36, 5, 0, TestName = "同じ列")]
        [TestCase(20, 5, 0, TestName = "同じブロック")]
        public void ConflictIsFoundInTheRowColumnOrBox(int cell, int digit, int expectedPeer)
        {
            var grid = Grid.Empty.With(0, 5);
            Assert.That(grid.ConflictWith(cell, digit), Is.EqualTo(expectedPeer));
            Assert.That(grid.With(cell, digit).HasConflict, Is.True);
        }

        [Test]
        public void SameDigitOutsideTheUnitsIsNotAConflict()
        {
            var grid = Grid.Empty.With(0, 5);
            Assert.That(grid.ConflictWith(Board.Cell(4, 4), 5), Is.EqualTo(-1));
            Assert.That(grid.With(Board.Cell(4, 4), 5).HasConflict, Is.False);
            Assert.That(EasySolution.HasConflict, Is.False);
        }

        [Test]
        public void EqualityAndStableHashFollowTheDigits()
        {
            Assert.That(Easy, Is.EqualTo(Grid.Parse(EasyText)));
            Assert.That(Easy.StableHash(), Is.EqualTo(Grid.Parse(EasyText).StableHash()));
            Assert.That(Easy, Is.Not.EqualTo(Easy.With(2, 4)));
            Assert.That(Easy.StableHash(), Is.Not.EqualTo(Easy.With(2, 4).StableHash()));
        }
    }
}
