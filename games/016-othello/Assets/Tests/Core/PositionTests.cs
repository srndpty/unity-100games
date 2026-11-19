using NUnit.Framework;
using static Game.Core.Tests.OthelloTestKit;

namespace Game.Core.Tests
{
    public sealed class PositionTests
    {
        [Test]
        public void InitialPositionHasFourCentralDiscsAndBlackToMove()
        {
            var p = Position.Initial;
            Assert.That(p.ToMove, Is.EqualTo(Side.Black));
            Assert.That(p.At(S("d4")), Is.EqualTo(Disc.White));
            Assert.That(p.At(S("e5")), Is.EqualTo(Disc.White));
            Assert.That(p.At(S("e4")), Is.EqualTo(Disc.Black));
            Assert.That(p.At(S("d5")), Is.EqualTo(Disc.Black));
            Assert.That(p.EmptyCount, Is.EqualTo(60));
        }

        [Test]
        public void InitialLegalMovesAreTheFourStandardOpenings()
        {
            Assert.That(Position.Initial.LegalMoves(), Is.EqualTo(Bits("d3", "c4", "f5", "e6")));
            Assert.That(Position.Initial.LegalMoves(Side.White), Is.EqualTo(Bits("e3", "f4", "c5", "d6")));
        }

        [Test]
        public void PlayFlipsSandwichedDiscAndPassesTheTurn()
        {
            var after = Position.Initial.Play(S("d3"));
            Assert.That(after.ToMove, Is.EqualTo(Side.White));
            Assert.That(after.At(S("d3")), Is.EqualTo(Disc.Black));
            Assert.That(after.At(S("d4")), Is.EqualTo(Disc.Black));
            Assert.That(after.Count(Side.Black), Is.EqualTo(4));
            Assert.That(after.Count(Side.White), Is.EqualTo(1));
        }

        [Test]
        public void PlayDoesNotChangeTheOriginalPosition()
        {
            var before = Position.Initial;
            _ = before.Play(S("d3"));
            Assert.That(before, Is.EqualTo(Position.Initial));
            Assert.That(before.At(S("d3")), Is.EqualTo(Disc.Empty));
        }

        [Test]
        public void MoveThatSandwichesInAllEightDirectionsFlipsThemAll()
        {
            var p = Parse(Side.Black,
                "B..B..B.",
                ".W.W.W..",
                "..WWW...",
                "BWW.WWB.",
                "..WWW...",
                ".W.W.W..",
                "B..B..B.",
                "........");
            var flips = p.Flips(S("d4"));
            Assert.That(Game.Core.Bits.PopCount(flips), Is.EqualTo(16));
            var after = p.Play(S("d4"));
            Assert.That(after.Count(Side.White), Is.EqualTo(0));
            Assert.That(after.Count(Side.Black), Is.EqualTo(8 + 16 + 1));
        }

        [Test]
        public void OnlyClosedLinesAreFlipped()
        {
            // c4 から右は f4 の黒で閉じているので返るが、d3 から下は盤端まで白が続くので返らない。
            var p = Parse(Side.Black,
                "........",
                "........",
                "........",
                "...WWB..",
                "...W....",
                "...W....",
                "...W....",
                "...W....");
            Assert.That(p.Flips(S("c4")), Is.EqualTo(Bits("d4", "e4")));
            Assert.That(p.Flips(S("d3")), Is.EqualTo(0));
            var q = Parse(Side.Black,
                "........",
                "........",
                "........",
                "..WWB...",
                "........",
                "........",
                "........",
                "........");
            Assert.That(q.Flips(S("b4")), Is.EqualTo(Bits("c4", "d4")));
        }

        [Test]
        public void LinesDoNotWrapAroundTheBoardEdge()
        {
            // h1 の白の右は「次の行の a 列」ではなく盤外。a2 の黒で挟めたことにしてはいけない。
            var p = Parse(Side.Black,
                ".......W",
                "B.......",
                "........",
                "........",
                "........",
                "........",
                "........",
                "........");
            Assert.That(p.Flips(S("g1")), Is.EqualTo(0));
            Assert.That(p.LegalMoves(), Is.EqualTo(0));
            var q = Parse(Side.Black,
                ".....BW.",
                "W.......",
                "........",
                "........",
                "........",
                "........",
                "........",
                "........");
            Assert.That(q.LegalMoves(), Is.EqualTo(Bits("h1")));
            Assert.That(q.Flips(S("h1")), Is.EqualTo(Bits("g1")));
        }

        [Test]
        public void CornerAndEdgeMovesAreGenerated()
        {
            var p = Parse(Side.White,
                ".BBBBBBW",
                "B.......",
                "B.......",
                "B.......",
                "B.......",
                "B.......",
                "B.......",
                "W.......");
            Assert.That(p.Flips(S("a1")), Is.EqualTo(Bits("b1", "c1", "d1", "e1", "f1", "g1", "a2", "a3", "a4", "a5", "a6", "a7")));
        }

        [Test]
        public void IllegalMovesFlipNothingAndCannotBePlayed()
        {
            var p = Position.Initial;
            Assert.That(p.Flips(S("a1")), Is.EqualTo(0), "挟めない");
            Assert.That(p.Flips(S("d4")), Is.EqualTo(0), "埋まっている");
            Assert.That(p.Flips(new Square(8, 0)), Is.EqualTo(0), "盤外");
            Assert.That(p.Flips(new Square(-1, 3)), Is.EqualTo(0), "盤外");
            Assert.Throws<System.ArgumentException>(() => p.Play(S("a1")));
        }

        [Test]
        public void TerminalWhenNeitherSideCanMove()
        {
            var blocked = Parse(Side.Black,
                "B.......",
                "........",
                "........",
                "........",
                "........",
                "........",
                "........",
                ".......W");
            Assert.That(blocked.IsTerminal, Is.True);
            Assert.That(Position.Initial.IsTerminal, Is.False);
        }

        [Test]
        public void OverlappingStonesAreRejected()
        {
            Assert.Throws<System.ArgumentException>(() => new Position(1UL, 1UL, Side.Black));
        }

        [Test]
        public void BoardTextRoundTrips()
        {
            var after = Position.Initial.Play(S("f5"));
            Assert.That(BoardText.Parse(Side.White, BoardText.Format(after)), Is.EqualTo(after));
        }

        [Test]
        public void SquareNamesAndIndices()
        {
            Assert.That(S("a1").Index, Is.EqualTo(0));
            Assert.That(S("h1").Index, Is.EqualTo(7));
            Assert.That(S("a8").Index, Is.EqualTo(56));
            Assert.That(S("h8").Index, Is.EqualTo(63));
            Assert.That(Square.FromIndex(27).Name, Is.EqualTo("d4"));
            Assert.That(new Square(8, 0).IsInside, Is.False);
            Assert.Throws<System.FormatException>(() => Square.Parse("i1"));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Square.FromIndex(64));
        }

        [Test]
        public void BitHelpersMatchANaiveCount()
        {
            var random = new SeededRandom(7);
            for (var i = 0; i < 1000; i++)
            {
                var value = ((ulong)random.NextUInt() << 32) | random.NextUInt();
                var naive = 0;
                var lowest = -1;
                for (var b = 63; b >= 0; b--)
                {
                    if ((value & (1UL << b)) == 0) { continue; }
                    naive++;
                    lowest = b;
                }

                Assert.That(Game.Core.Bits.PopCount(value), Is.EqualTo(naive));
                if (value != 0) { Assert.That(Game.Core.Bits.LowestIndex(value), Is.EqualTo(lowest)); }
            }

            for (var b = 0; b < 64; b++) { Assert.That(Game.Core.Bits.LowestIndex(1UL << b), Is.EqualTo(b)); }
        }
    }
}
