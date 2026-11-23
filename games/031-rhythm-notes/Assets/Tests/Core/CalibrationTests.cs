using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class CalibrationTests
    {
        private const int Ms = Song.SamplesPerMillisecond;
        private static readonly Song _chart = Arrangement.Calibration.Song;

        [Test]
        public void CalibrationTrackIsACountInBarAndEightBarsOfQuarterNotes()
        {
            var track = Arrangement.Calibration;
            Assert.That(_chart.Notes.Count, Is.EqualTo(32));
            Assert.That(_chart.Bars.Count, Is.EqualTo(10));
            Assert.That(track.Melody, Is.Empty);
            Assert.That(_chart.Notes[0].Sample, Is.EqualTo(_chart.Bars[1]));
            for (var i = 0; i < _chart.Notes.Count; i++)
            {
                Assert.That(_chart.Notes[i].Sample, Is.EqualTo(track.SampleAt(8 + 2 * i)));
            }

            Assert.DoesNotThrow(() => _ = new Scorecard(_chart, new RhythmRules()));
            Assert.That(_chart.Length * 2, Is.LessThan(2000 * 1024 - 44), "16ビットモノラルの WAV がコミットの容量上限に収まる");
        }

        [Test]
        public void SongsHaveABarLineEveryFourBeats()
        {
            foreach (var track in Arrangement.All)
            {
                Assert.That(track.Song.Bars.Count, Is.EqualTo(12), track.Song.Name);
                for (var i = 0; i < track.Song.Bars.Count; i++)
                {
                    Assert.That(track.Song.Bars[i], Is.EqualTo(track.SampleAt(8 * i)), track.Song.Name);
                }
            }
        }

        [TestCase(0, 0)]
        [TestCase(150, 150)]
        [TestCase(-40, -40)]
        [TestCase(152, 150)]
        [TestCase(153, 155)]
        [TestCase(-153, -155)]
        public void MedianLagIsRoundedToTheOffsetStep(int lateMs, int expected)
        {
            Assert.That(Calibration.Estimate(_chart, Taps(i => lateMs * Ms)), Is.EqualTo(expected));
        }

        [Test]
        public void HalfRoundingIsAwayFromZero()
        {
            Assert.That(Calibration.Estimate(_chart, Taps(i => 2.5 * Ms)), Is.EqualTo(5));
            Assert.That(Calibration.Estimate(_chart, Taps(i => -2.5 * Ms)), Is.EqualTo(-5));
            Assert.That(Calibration.Estimate(_chart, Taps(i => 2.5 * Ms - 1)), Is.EqualTo(0));
        }

        [Test]
        public void MedianIgnoresStrayAndDoubledTaps()
        {
            var taps = Taps(i => i % 5 == 0 ? 90 * Ms : 120 * Ms).ToList();
            taps.Add(_chart.Notes[3].Sample + 121 * Ms);
            taps.Add(_chart.Notes[0].Sample - 900 * Ms);
            Assert.That(Calibration.Estimate(_chart, taps), Is.EqualTo(120));
        }

        [Test]
        public void EvenCountUsesTheMeanOfTheMiddlePair()
        {
            var taps = Taps(i => i < 16 ? 100 * Ms : 110 * Ms);
            Assert.That(Calibration.Estimate(_chart, taps), Is.EqualTo(105));
        }

        [Test]
        public void TapsMustBeCloserThanHalfTheGapAndCoverHalfTheNotes()
        {
            var song = new Song("test", 5 * Song.SampleRate,
                new[] { new Note(Song.SampleRate, Strike.Pulse), new Note(3 * Song.SampleRate, Strike.Spark) }, Array.Empty<int>());
            Assert.That(Calibration.Estimate(song, new long[] { 2 * Song.SampleRate }), Is.Null, "両隣のちょうど中間");
            Assert.That(Calibration.Estimate(song, new long[] { 2 * Song.SampleRate - 1 }), Is.EqualTo(Calibration.MaxOffsetMs));
            Assert.That(Calibration.Estimate(song, new long[] { 2 * Song.SampleRate + 1 }), Is.EqualTo(Calibration.MinOffsetMs));

            var enough = Taps(i => i < 16 ? 50 * Ms : (double?)null);
            Assert.That(Calibration.Estimate(_chart, enough), Is.EqualTo(50));
            var tooFew = Taps(i => i < 15 ? 50 * Ms : (double?)null);
            Assert.That(Calibration.Estimate(_chart, tooFew), Is.Null);
            Assert.That(Calibration.Estimate(_chart, Array.Empty<long>()), Is.Null);
        }

        [Test]
        public void MeasuredLagIsClampedToTheOffsetRange()
        {
            Assert.That(Calibration.Estimate(_chart, Taps(i => 320 * Ms)), Is.EqualTo(Calibration.MaxOffsetMs));
            Assert.That(Calibration.Estimate(_chart, Taps(i => -220 * Ms)), Is.EqualTo(Calibration.MinOffsetMs));
        }

        [Test]
        public void OffsetRangeIsInStepsAndTheLimitCoversBothEnds()
        {
            Assert.That(Calibration.ValidOffset(Calibration.MinOffsetMs), Is.True);
            Assert.That(Calibration.ValidOffset(Calibration.MaxOffsetMs), Is.True);
            Assert.That(Calibration.ValidOffset(Calibration.MinOffsetMs - Calibration.StepMs), Is.False);
            Assert.That(Calibration.ValidOffset(Calibration.MaxOffsetMs + Calibration.StepMs), Is.False);
            Assert.That(Calibration.ValidOffset(7), Is.False);
            Assert.That(Calibration.OffsetLimitMs, Is.GreaterThanOrEqualTo(-Calibration.MinOffsetMs));
            Assert.That(Calibration.OffsetLimitMs, Is.GreaterThanOrEqualTo(Calibration.MaxOffsetMs));
            var gap = _chart.Notes[1].Sample - _chart.Notes[0].Sample;
            Assert.That((Calibration.MaxOffsetMs + Calibration.StepMs) * Ms, Is.LessThan(gap / 2), "補正の上限まで測れる拍の間隔");
        }

        private static List<long> Taps(Func<int, double?> late)
        {
            var taps = new List<long>();
            for (var i = 0; i < _chart.Notes.Count; i++)
            {
                if (late(i) is { } lag) { taps.Add(_chart.Notes[i].Sample + (long)Math.Round(lag)); }
            }

            return taps;
        }
    }
}
