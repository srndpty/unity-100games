using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class RhythmRulesTests
    {
        private const int Beat = Song.SampleRate;
        private const int SamplesPerMs = Song.SamplesPerMillisecond;
        private const int GoodWindow = 30 * SamplesPerMs;
        private const int FairWindow = 80 * SamplesPerMs;

        [TestCase(-(FairWindow + 1), Grade.None)]
        [TestCase(-FairWindow, Grade.Fair)]
        [TestCase(-(GoodWindow + 1), Grade.Fair)]
        [TestCase(-GoodWindow, Grade.Good)]
        [TestCase(GoodWindow, Grade.Good)]
        [TestCase(GoodWindow + 1, Grade.Fair)]
        [TestCase(FairWindow, Grade.Fair)]
        [TestCase(FairWindow + 1, Grade.None)]
        public void HitWindowsIncludeTheirEdges(int error, Grade expected)
        {
            var score = NewScore(new Note(Beat, Strike.Pulse));
            Assert.That(score.Hit(Beat + error, Strike.Pulse), Is.EqualTo(expected));
        }

        [Test]
        public void MatchingConsumesOnlyTheNearestSameStrikeAndBreaksTiesEarly()
        {
            var score = NewScore(new Note(Beat, Strike.Pulse), new Note(Beat + 40 * SamplesPerMs, Strike.Spark),
                new Note(Beat + 80 * SamplesPerMs, Strike.Pulse));
            Assert.That(score.Hit(Beat + 40 * SamplesPerMs, Strike.Pulse), Is.EqualTo(Grade.Fair));
            Assert.That(score.GradeAt(0), Is.EqualTo(Grade.Fair));
            Assert.That(score.GradeAt(1), Is.EqualTo(Grade.None));
            Assert.That(score.Hit(Beat + 80 * SamplesPerMs, Strike.Pulse), Is.EqualTo(Grade.Good));
            Assert.That(score.Hit(Beat + 80 * SamplesPerMs, Strike.Pulse), Is.EqualTo(Grade.None));
            Assert.That(score.Points, Is.EqualTo(150));
        }

        [Test]
        public void ExpirationIsStrictAndResetsCombo()
        {
            var score = NewScore(new Note(Beat, Strike.Pulse), new Note(2 * Beat, Strike.Spark));
            Assert.That(score.Hit(Beat, Strike.Pulse), Is.EqualTo(Grade.Good));
            score.Advance(2 * Beat + FairWindow);
            Assert.That(score.Combo, Is.EqualTo(1));
            Assert.That(score.Hit(2 * Beat + FairWindow, Strike.Pulse), Is.EqualTo(Grade.None));
            score.Advance(2 * Beat + FairWindow + 1);
            Assert.That(score.Miss, Is.EqualTo(1));
            Assert.That(score.Combo, Is.Zero);
            Assert.That(score.BestCombo, Is.EqualTo(1));
            Assert.That(score.Complete, Is.True);
        }

        [Test]
        public void InvalidChartsAndRulesAreRejected()
        {
            Assert.Throws<ArgumentException>(() =>
                new Song("test", 2 * Beat, new[] { new Note(Beat, Strike.Pulse), new Note(Beat, Strike.Spark) }));
            Assert.Throws<ArgumentException>(() =>
                new Song("test", 2 * Beat, new[] { new Note(Beat, (Strike)8) }));
            Assert.Throws<InvalidOperationException>(() => new RhythmRules { FairMs = 30 }.Validate());
        }

        private static Scorecard NewScore(params Note[] notes) => new(new Song("test", 3 * Beat, notes), new RhythmRules());
    }
}
