using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class BoardTests
    {
        [Test]
        public void EveryCellHasTwentyDistinctPeersInItsRowColumnAndBox()
        {
            for (var cell = 0; cell < Board.CellCount; cell++)
            {
                var peers = new HashSet<int>();
                for (var i = 0; i < Board.PeerCount; i++)
                {
                    var peer = Board.Peer(cell, i);
                    Assert.That(peer, Is.Not.EqualTo(cell));
                    Assert.That(Board.Row(peer) == Board.Row(cell) || Board.Column(peer) == Board.Column(cell) || Board.Box(peer) == Board.Box(cell), Is.True);
                    peers.Add(peer);
                }

                Assert.That(peers, Has.Count.EqualTo(Board.PeerCount));
            }
        }

        [Test]
        public void EachKindOfUnitCoversEveryCellOnce()
        {
            for (var kind = 0; kind < 3; kind++)
            {
                var seen = new int[Board.CellCount];
                for (var number = 0; number < Board.Size; number++)
                {
                    var unit = kind * Board.Size + number;
                    Assert.That(Board.KindOf(unit), Is.EqualTo((UnitKind)kind));
                    Assert.That(Board.NumberOf(unit), Is.EqualTo(number));
                    for (var i = 0; i < Board.Size; i++)
                    {
                        var cell = Board.UnitCell(unit, i);
                        seen[cell]++;
                        var expected = kind switch { 0 => Board.Row(cell), 1 => Board.Column(cell), _ => Board.Box(cell) };
                        Assert.That(expected, Is.EqualTo(number));
                    }
                }

                Assert.That(seen, Is.All.EqualTo(1));
            }
        }

        [TestCase(0, 0)]
        [TestCase(8, 2)]
        [TestCase(30, 4)]
        [TestCase(54, 6)]
        [TestCase(80, 8)]
        public void BoxNumbersRunLeftToRightThenTopToBottom(int cell, int box)
        {
            Assert.That(Board.Box(cell), Is.EqualTo(box));
        }

        [Test]
        public void DigitMasksCountAndFindTheLowestDigit()
        {
            var mask = Digits.Bit(3) | Digits.Bit(7) | Digits.Bit(9);
            Assert.That(Digits.Count(mask), Is.EqualTo(3));
            Assert.That(Digits.Lowest(mask), Is.EqualTo(3));
            Assert.That(Digits.Has(mask, 7), Is.True);
            Assert.That(Digits.Has(mask, 8), Is.False);
            Assert.That(Digits.Lowest(0), Is.EqualTo(0));
            Assert.That(Digits.Count(Digits.All), Is.EqualTo(9));
        }
    }
}
