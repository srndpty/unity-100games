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

        // 白の地（E 列の壁の右）の隅に二眼（H2・J1）で生きた黒、白の地の中に眼のない黒の 2 子（H6・H7）。
        private const string TwoEyesAndADeadPair = @"
            . . . X O . . . .
            . . . X O . . . .
            . . . X O . . X .
            . . . X O . . X .
            . . . X O . . . .
            . . . X O O O O O
            . . . X O . X X X
            . . . X O . X . X
            . . . X O . X X .";

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void TwoEyedGroup_IsAlive_AndTheSurroundedPair_IsDeadAsAWholeChain(int seed)
        {
            var game = Lab.Game(TwoEyesAndADeadPair, Stone.White);
            var dead = Lab.ToSet(Ownership.EstimateDead(game, new MctsConfig(), seed));
            Assert.That(dead, Is.EquivalentTo(new[] { Points.Parse("H6"), Points.Parse("H7") }), $"seed {seed}");
        }

        [Test]
        public void TheSearch_DoesNotDependOnHowTheRunsAreSplit()
        {
            var game = Lab.Game(Invaded);
            var whole = new OwnershipSearch(game, 90, Points.Count * 3, new RandomSeries(6));
            Assert.That(whole.Run(1000), Is.EqualTo(90));
            Assert.That(whole.IsDone, Is.True);
            foreach (var chunk in new[] { 1, 7, 89 })
            {
                var split = new OwnershipSearch(game, 90, Points.Count * 3, new RandomSeries(6));
                Assert.That(() => split.EstimateDead(50), Throws.InvalidOperationException, "回し終える前");
                while (!split.IsDone) { split.Run(chunk); }
                Assert.That(split.BlackCounts(), Is.EqualTo(whole.BlackCounts()), $"{chunk} ずつ");
                Assert.That(split.WhiteCounts(), Is.EqualTo(whole.WhiteCounts()), $"{chunk} ずつ");
                Assert.That(split.EstimateDead(50), Is.EqualTo(whole.EstimateDead(50)));
            }
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
