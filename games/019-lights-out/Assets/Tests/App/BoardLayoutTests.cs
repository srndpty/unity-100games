using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class BoardLayoutTests
    {
        private static readonly BoardLayout _layout = new(2f, 1.6f);

        [Test]
        public void CentersAreSymmetricAroundTheOrigin()
        {
            Assert.That(_layout.CellCenter(12), Is.EqualTo((0f, 0f)));
            Assert.That(_layout.CellCenter(0), Is.EqualTo((-4f, 4f)), "行 0 は最上段");
            Assert.That(_layout.CellCenter(24), Is.EqualTo((4f, -4f)));
            Assert.That(_layout.Extent, Is.EqualTo(9.6f).Within(1e-5f));
        }

        [Test]
        public void EveryCellCenterHitsItself()
        {
            for (var cell = 0; cell < LightsBoard.CellCount; cell++)
            {
                var (x, y) = _layout.CellCenter(cell);
                Assert.That(_layout.TryCellAt(x, y, out var hit), Is.True);
                Assert.That(hit, Is.EqualTo(cell));
                Assert.That(_layout.TryCellAt(x + 0.79f, y - 0.79f, out hit), Is.True, "マスの縁の内側");
                Assert.That(hit, Is.EqualTo(cell));
            }
        }

        [TestCase(1f, 0f)]
        [TestCase(0f, -1f)]
        [TestCase(-5f, 0f)]
        [TestCase(0f, 5.1f)]
        [TestCase(100f, 100f)]
        [TestCase(float.NaN, 0f)]
        [TestCase(0f, float.PositiveInfinity)]
        public void GapsAndOutsideAreNotCells(float x, float y)
        {
            Assert.That(_layout.TryCellAt(x, y, out var cell), Is.False);
            Assert.That(cell, Is.EqualTo(-1));
        }

        [TestCase(0f, 1f)]
        [TestCase(-1f, 1f)]
        [TestCase(2f, 2.5f)]
        [TestCase(float.NaN, 1f)]
        [TestCase(2f, float.PositiveInfinity)]
        public void InvalidSizesAreRejected(float pitch, float hitSize)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new BoardLayout(pitch, hitSize));
        }

        [Test]
        public void OutOfRangeCellIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _layout.CellCenter(25));
        }
    }
}
