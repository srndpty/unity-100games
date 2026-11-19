using System.Linq;
using NUnit.Framework;
using static Game.Core.Tests.OthelloTestKit;

namespace Game.Core.Tests
{
    /// <summary>ビット演算の合法手生成と着手の適用を、64マス×8方向を歩く参照実装と全局面で照合する。</summary>
    public sealed class BruteForceTests
    {
        private const int Games = 300;

        [Test]
        public void LegalMovesAndFlipsMatchTheNaiveScanInEveryVisitedPosition()
        {
            var positions = 0;
            for (var seed = 1; seed <= Games; seed++)
            {
                RandomPlayout(seed, p =>
                {
                    positions++;
                    foreach (var side in new[] { Side.Black, Side.White })
                    {
                        var asSide = side == p.ToMove ? p : p.Pass();
                        Assert.That(asSide.LegalMoves(), Is.EqualTo(NaiveLegalMoves(asSide)), $"seed {seed} {side}");
                        for (var i = 0; i < Square.Count; i++)
                        {
                            var square = Square.FromIndex(i);
                            Assert.That(asSide.Flips(square), Is.EqualTo(NaiveFlips(asSide, square)), $"seed {seed} {side} {square}");
                        }
                    }
                });
            }

            Assert.That(positions, Is.GreaterThan(Games * 55), "各対局でほぼ盤が埋まるまで照合している");
        }

        [Test]
        public void PlayedPositionsKeepTheDiscCountInvariant()
        {
            for (var seed = 1; seed <= Games; seed++)
            {
                Position? previous = null;
                RandomPlayout(seed, p =>
                {
                    if (previous != null)
                    {
                        Assert.That(p.ToMove, Is.EqualTo(previous.ToMove.Opponent()));
                        if (p.EmptyCount == previous.EmptyCount)
                        {
                            // パスでは石が変わらない。
                            Assert.That((p.Black, p.White), Is.EqualTo((previous.Black, previous.White)));
                        }
                        else
                        {
                            // 着手では石が1つ増え、打った側の石は「返った数 + 1」増える。
                            var mover = previous.ToMove;
                            var flipped = previous.Count(mover.Opponent()) - p.Count(mover.Opponent());
                            Assert.That(p.EmptyCount, Is.EqualTo(previous.EmptyCount - 1));
                            Assert.That(flipped, Is.GreaterThan(0));
                            Assert.That(p.Count(mover), Is.EqualTo(previous.Count(mover) + flipped + 1));
                        }
                    }

                    previous = p;
                });
            }
        }

        [Test]
        public void RandomGamesEndOnlyWhenNeitherSideCanMove()
        {
            for (var seed = 1; seed <= Games; seed++)
            {
                var end = RandomPlayout(seed);
                Assert.That(end.LegalMoves(Side.Black), Is.EqualTo(0));
                Assert.That(end.LegalMoves(Side.White), Is.EqualTo(0));
            }
        }

        [Test]
        public void SomeSampledGamesContainPassesAndEarlyEnds()
        {
            // 照合したランダム対局に、パスと空きの残る終局の両方が含まれていること（参照実装との照合が手薄な場面を残さない）。
            var passes = 0;
            for (var seed = 1; seed <= Games; seed++)
            {
                RandomPlayout(seed, p =>
                {
                    if (!p.IsTerminal && p.LegalMoves() == 0) { passes++; }
                });
            }

            var earlyEnds = Enumerable.Range(1, Games).Count(seed => RandomPlayout(seed).EmptyCount > 0);
            Assert.That(passes, Is.GreaterThan(0));
            Assert.That(earlyEnds, Is.GreaterThan(0));
        }
    }
}
