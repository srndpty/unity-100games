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
        private static readonly int[] _noBars = Array.Empty<int>();

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

        [TestCase(-FairWindow, Timing.Fast)]
        [TestCase(-(GoodWindow + 1), Timing.Fast)]
        [TestCase(-GoodWindow, Timing.None)]
        [TestCase(0, Timing.None)]
        [TestCase(GoodWindow, Timing.None)]
        [TestCase(GoodWindow + 1, Timing.Slow)]
        [TestCase(FairWindow, Timing.Slow)]
        public void FairHitsAreMarkedFastOrSlow(int error, Timing expected)
        {
            var score = NewScore(new Note(Beat, Strike.Pulse));
            score.Hit(Beat + error, Strike.Pulse);
            Assert.That(score.LastTiming, Is.EqualTo(expected));
            Assert.That(score.Fast, Is.EqualTo(expected == Timing.Fast ? 1 : 0));
            Assert.That(score.Slow, Is.EqualTo(expected == Timing.Slow ? 1 : 0));
        }

        [Test]
        public void MissClearsTheTimingAndBreaksTheFullCombo()
        {
            var score = NewScore(new Note(Beat, Strike.Pulse), new Note(2 * Beat, Strike.Spark));
            score.Hit(Beat + FairWindow, Strike.Pulse);
            Assert.That(score.LastTiming, Is.EqualTo(Timing.Slow));
            Assert.That(score.FullCombo, Is.False, "判定前のノーツが残る");
            score.Advance(2 * Beat + FairWindow + 1);
            Assert.That(score.LastTiming, Is.EqualTo(Timing.None));
            Assert.That(score.Complete, Is.True);
            Assert.That(score.FullCombo, Is.False);
        }

        [Test]
        public void FullComboCountsFairHits()
        {
            var score = NewScore(new Note(Beat, Strike.Pulse), new Note(2 * Beat, Strike.Spark));
            score.Hit(Beat - FairWindow, Strike.Pulse);
            Assert.That(score.FullCombo, Is.False);
            score.Hit(2 * Beat, Strike.Spark);
            Assert.That(score.FullCombo, Is.True);
            Assert.That(score.Fast, Is.EqualTo(1));
            Assert.That(score.Slow, Is.Zero);
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
        public void LateConfirmedMissBreaksTheComboInChartOrder()
        {
            var score = NewScore(new Note(Beat, Strike.Pulse), new Note(Beat + 50 * SamplesPerMs, Strike.Spark),
                new Note(Beat + 100 * SamplesPerMs, Strike.Pulse));
            Assert.That(score.Hit(Beat + 50 * SamplesPerMs, Strike.Spark), Is.EqualTo(Grade.Good));
            Assert.That(score.Combo, Is.Zero, "前の未判定のノーツが決まるまで数えない");
            score.Advance(Beat + 90 * SamplesPerMs);
            Assert.That(score.GradeAt(0), Is.EqualTo(Grade.Miss));
            Assert.That(score.Combo, Is.EqualTo(1));
            Assert.That(score.Hit(Beat + 100 * SamplesPerMs, Strike.Pulse), Is.EqualTo(Grade.Good));
            Assert.That(score.Combo, Is.EqualTo(2));
            Assert.That(score.BestCombo, Is.EqualTo(2));
            Assert.That(score.Miss, Is.EqualTo(1));
        }

        [Test]
        public void EarlierNoteHitAfterALaterOneJoinsTheSameCombo()
        {
            var score = NewScore(new Note(Beat, Strike.Pulse), new Note(Beat + 20 * SamplesPerMs, Strike.Spark));
            Assert.That(score.Hit(Beat + 10 * SamplesPerMs, Strike.Spark), Is.EqualTo(Grade.Good));
            Assert.That(score.Hit(Beat + 30 * SamplesPerMs, Strike.Pulse), Is.EqualTo(Grade.Good));
            Assert.That(score.Combo, Is.EqualTo(2));
            Assert.That(score.BestCombo, Is.EqualTo(2));
            Assert.That(score.FullCombo, Is.True);
        }

        [Test]
        public void MissConfirmedAfterLaterHitsKeepsOnlyTheHitsAfterIt()
        {
            var score = NewScore(new Note(Beat, Strike.Pulse), new Note(Beat + 40 * SamplesPerMs, Strike.Spark),
                new Note(Beat + 60 * SamplesPerMs, Strike.Spark));
            score.Hit(Beat + 40 * SamplesPerMs, Strike.Spark);
            score.Hit(Beat + 60 * SamplesPerMs, Strike.Spark);
            Assert.That(score.Combo, Is.Zero);
            score.Advance(Beat + FairWindow + 1);
            Assert.That(score.Combo, Is.EqualTo(2), "旧実装は Good, Good, Miss の順で 0 になった");
            Assert.That(score.BestCombo, Is.EqualTo(2));
            Assert.That(score.Complete, Is.True);
            Assert.That(score.FullCombo, Is.False);
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
                new Song("test", 2 * Beat, new[] { new Note(Beat, Strike.Pulse), new Note(Beat, Strike.Spark) }, _noBars));
            Assert.Throws<ArgumentException>(() =>
                new Song("test", 2 * Beat, new[] { new Note(Beat, (Strike)8) }, _noBars));
            Assert.Throws<ArgumentException>(() =>
                new Song("test", 2 * Beat, new[] { new Note(Beat, Strike.Pulse) }, new[] { Beat, Beat }));
            Assert.Throws<ArgumentException>(() =>
                new Song("test", 2 * Beat, new[] { new Note(Beat, Strike.Pulse) }, new[] { 2 * Beat }));
            Assert.Throws<ArgumentException>(() =>
                new Song("test", 2 * Beat, new[] { new Note(Beat, Strike.Pulse) }, new[] { -1 }));
            Assert.Throws<InvalidOperationException>(() => new RhythmRules { FairMs = 30 }.Validate());
        }

        [Test]
        public void ChartsNeedRoomForTheLargestOffsetBeforeAndAfterTheNotes()
        {
            var margin = FairWindow + Calibration.OffsetLimitMs * SamplesPerMs;
            Assert.Throws<ArgumentException>(() => NewScore(new Note(margin, Strike.Pulse)));
            Assert.Throws<ArgumentException>(() => NewScore(new Note(3 * Beat - margin, Strike.Pulse)));
            Assert.DoesNotThrow(() => NewScore(new Note(margin + 1, Strike.Pulse), new Note(3 * Beat - margin - 1, Strike.Spark)));
        }

        private static Scorecard NewScore(params Note[] notes) => new(new Song("test", 3 * Beat, notes, _noBars), new RhythmRules());
    }
}
