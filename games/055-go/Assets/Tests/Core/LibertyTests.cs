using Game.Core;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>題材固有：取り石後の呼吸点数一致。差分で更新した連と呼吸点を、塗りつぶしの数え直しと比べる。</summary>
    public sealed class LibertyTests
    {
        [Test]
        public void CapturingAGroup_GivesItsPointsBackAsLiberties()
        {
            // 白 D5・E5 の 2 子を黒 F5 で取る。周りの黒の連の呼吸点に、取った跡が足される。
            var game = Lab.Game(@"
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . X X . . . .
                . . X O O . . . .
                . . . X X . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .");
            var c5 = Points.Parse("C5");
            var d6 = Points.Parse("D6");
            Assert.That(game.LibertyCount(c5), Is.EqualTo(3));
            Lab.Play(game, "F5");
            Assert.That(game[Points.Parse("D5")], Is.EqualTo(Stone.Empty));
            Assert.That(game[Points.Parse("E5")], Is.EqualTo(Stone.Empty));
            Assert.That(game.Captures(Stone.Black), Is.EqualTo(2));
            Assert.That(game.LibertyCount(c5), Is.EqualTo(4), "C5 の呼吸点に D5 が足される");
            Assert.That(game.LibertyCount(d6), Is.EqualTo(6), "D6・E6 の連の呼吸点に D5・E5 が足される");
            Assert.That(game.LibertyCount(Points.Parse("F5")), Is.EqualTo(4), "F5 の呼吸点に E5 が足される");
            Lab.AssertChainsMatchNaive(game.CopyBoard(), "取った後");
        }

        [Test]
        public void ConnectingChains_MergesStonesAndLiberties()
        {
            var game = Lab.Game(@"
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . X . X . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .");
            Lab.Play(game, "E5");
            Assert.That(game.ChainAt(Points.Parse("D5")).Count, Is.EqualTo(3));
            Assert.That(game.LibertyCount(Points.Parse("F5")), Is.EqualTo(8));
            Lab.AssertChainsMatchNaive(game.CopyBoard(), "つないだ後");
        }

        [Test]
        public void RandomGames_IncrementalChainsAlwaysMatchTheNaiveRecount([Range(1, 40)] int seed)
        {
            var captured = 0;
            var previous = new GoGame(Lab.Config).CopyBoard();
            Lab.RandomGame(seed, 250, game =>
            {
                var board = game.CopyBoard();
                Lab.AssertChainsMatchNaive(board, $"seed {seed} の {game.MoveCount} 手目");

                // 前の盤にあって今ない石は、取られた石。アゲハマの累計と一致する。
                for (var p = 0; p < Points.Count; p++)
                {
                    if (previous[p] != Stone.Empty && board[p] == Stone.Empty) { captured++; }
                }

                Assert.That(game.Captures(Stone.Black) + game.Captures(Stone.White), Is.EqualTo(captured), "アゲハマの累計");
                Assert.That(board.Hash, Is.EqualTo(board.Zobrist.Compute(p => board[p])), "Zobrist の差分更新");
                previous = board;
            });
        }

        [Test]
        public void FromCells_RejectsAChainWithoutLiberties()
        {
            Assert.That(() => BoardText.Parse(@"
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                . . . . . . . . .
                O . . . . . . . .
                X O . . . . . . ."), Throws.ArgumentException);
        }
    }
}
