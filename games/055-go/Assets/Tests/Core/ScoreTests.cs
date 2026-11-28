using Game.Core;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>中国式の面積計点。</summary>
    public sealed class ScoreTests
    {
        // 黒は E 列、白は F 列の壁。黒の面積は 9 × 5 = 45、白は 9 × 4 = 36。
        private const string Walls = @"
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .";

        [Test]
        public void EmptyBoard_IsAllNeutral_AndWhiteWinsByKomi()
        {
            var score = AreaScore.Compute(new Board(), 7);
            Assert.That(score.Neutral, Is.EqualTo(81));
            Assert.That(score.BlackArea, Is.EqualTo(0));
            Assert.That(score.Margin, Is.EqualTo(-7));
            Assert.That(score.Winner, Is.EqualTo(Stone.White));
        }

        [Test]
        public void Walls_CountStonesAndTerritory()
        {
            var score = AreaScore.Compute(BoardText.Parse(Walls), 7);
            Assert.That(score.BlackStones, Is.EqualTo(9));
            Assert.That(score.BlackTerritory, Is.EqualTo(36));
            Assert.That(score.WhiteStones, Is.EqualTo(9));
            Assert.That(score.WhiteTerritory, Is.EqualTo(27));
            Assert.That(score.Margin, Is.EqualTo(2));
            Assert.That(score.Winner, Is.EqualTo(Stone.Black));
        }

        [TestCase(8, Stone.Black, 1)]
        [TestCase(9, Stone.Empty, 0)]
        [TestCase(10, Stone.White, -1)]
        public void Komi_DecidesTheBoundary(int komi, Stone winner, int margin)
        {
            var score = AreaScore.Compute(BoardText.Parse(Walls), komi);
            Assert.That(score.Margin, Is.EqualTo(margin));
            Assert.That(score.Winner, Is.EqualTo(winner));
        }

        [Test]
        public void PointsTouchingBothColors_AreNeutral()
        {
            // F 列が空いている（ダメ・セキの共有の呼吸点と同じく、両方の色に接する）。
            var score = AreaScore.Compute(BoardText.Parse(@"
                . . . . X . O . .
                . . . . X . O . .
                . . . . X . O . .
                . . . . X . O . .
                . . . . X . O . .
                . . . . X . O . .
                . . . . X . O . .
                . . . . X . O . .
                . . . . X . O . ."), 7);
            Assert.That(score.Neutral, Is.EqualTo(9));
            Assert.That(score.BlackArea, Is.EqualTo(45));
            Assert.That(score.WhiteArea, Is.EqualTo(27));
        }

        [Test]
        public void DeadStones_AreRemovedBeforeCounting()
        {
            // 黒の地の中の白 B2。そのまま数えると黒の地は両色に接して中立になる。死石にすると黒の地。
            var board = BoardText.Parse(@"
                . . . . X O . . .
                . . . . X O . . .
                . . . . X O . . .
                . . . . X O . . .
                . . . . X O . . .
                . . . . X O . . .
                . . . . X O . . .
                . O . . X O . . .
                . . . . X O . . .");
            var alive = AreaScore.Compute(board, 7);
            Assert.That(alive.BlackTerritory, Is.EqualTo(0));
            Assert.That(alive.Neutral, Is.EqualTo(35));
            Assert.That(alive.WhiteArea, Is.EqualTo(37));

            var dead = AreaScore.Compute(board, board.ChainAt(Points.Parse("B2")), 7);
            Assert.That(dead.BlackTerritory, Is.EqualTo(36), "死石の跡も黒の地");
            Assert.That(dead.WhiteArea, Is.EqualTo(36));
            Assert.That(dead.Margin, Is.EqualTo(2));
        }

        [Test]
        public void DeadSet_WithAnEmptyPoint_IsRejected()
        {
            Assert.That(() => AreaScore.Compute(BoardText.Parse(Walls), PointSet.Of(Points.Parse("A1")), 7), Throws.ArgumentException);
        }
    }
}
