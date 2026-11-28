using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>終局後の死石の推定。</summary>
    public sealed class OwnershipTests
    {
        // 黒の地の中に白 B2、白の地の中に黒 H8。壁はどちらも生き。
        private const string Invaded = @"
            . . . . X O . . .
            . . . . X O . X .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . . . . X O . . .
            . O . . X O . . .
            . . . . X O . . .";

        [Test]
        public void LoneStonesInsideTerritory_AreDead_AndWallsAlive()
        {
            var game = Lab.Game(Invaded);
            var dead = Ownership.EstimateDead(game, new MctsConfig(), 1);
            Assert.That(Lab.ToSet(dead), Is.EquivalentTo(new[] { Points.Parse("B2"), Points.Parse("H8") }));
        }

        [Test]
        public void Estimate_IsReproducible_ForTheSameSeed()
        {
            var game = Lab.RandomGame(21, 120);
            if (game.IsOver) { game = Lab.RandomGame(21, 100); }
            var config = new MctsConfig { OwnershipPlayouts = 200 };
            Assert.That(Ownership.EstimateDead(game, config, 5), Is.EqualTo(Ownership.EstimateDead(game, config, 5)));
        }

        [Test]
        public void Counts_CoverEveryPlayout()
        {
            var game = Lab.Game(Invaded);
            var (black, white) = Ownership.Count(game, 50, Points.Count * 3, new RandomSeries(3));
            for (var p = 0; p < Points.Count; p++)
            {
                Assert.That(black[p] + white[p], Is.LessThanOrEqualTo(50));
            }

            Assert.That(black[Points.Parse("A1")], Is.GreaterThan(25), "黒の地の隅（過半）");
            Assert.That(white[Points.Parse("J1")], Is.GreaterThan(25), "白の地の隅（過半）");
        }

        [Test]
        public void DeadStonesScoreAsTheOpponentsArea()
        {
            var game = Lab.Game(Invaded);
            var dead = Ownership.EstimateDead(game, new MctsConfig(), 1);
            var score = AreaScore.Compute(game.CopyBoard(), dead, 7);
            Assert.That(score.BlackArea, Is.EqualTo(45));
            Assert.That(score.WhiteArea, Is.EqualTo(36));
        }
    }
}
