using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class RhythmSessionTests
    {
        private const int Ms = Song.SamplesPerMillisecond;

        [TestCase(-200)]
        [TestCase(0)]
        [TestCase(100)]
        [TestCase(300)]
        public void TimestampConversionAndOffsetRecoverTheOriginalBeat(int offset)
        {
            var session = Start(offset);
            var input = RhythmSession.ToDspTime(31 + offset / 1000.0, 31.2, 11.2);
            session.StrikeAt(Strike.Pulse, input);
            session.Advance(11.3 + offset / 1000.0);
            Assert.That(session.Score?.Good, Is.EqualTo(1));
            Assert.That(session.Score?.Miss, Is.Zero);
        }

        [TestCase(-200)]
        [TestCase(150)]
        public void OffsetDelaysTheVisibleChartByTheSameAmount(int offset)
        {
            var session = Start(offset);
            session.Advance(11);
            Assert.That(session.Position, Is.EqualTo(Song.SampleRate - offset * Ms));
        }

        [Test]
        public void LateOffsetDoesNotExpireTheNoteBeforeItIsHeard()
        {
            // 補正なしなら 1.33 秒の時点で 1 秒のノーツは窓（80 ms）を過ぎてミスになる。
            var session = Start(300);
            session.Advance(11.33);
            Assert.That(session.Score?.Miss, Is.Zero);
            session.StrikeAt(Strike.Pulse, 11.3);
            session.Advance(11.5);
            Assert.That(session.Score?.Good, Is.EqualTo(1));
        }

        [Test]
        public void OffsetChangesOnlyOnTheSelectScreenWithinTheRange()
        {
            RhythmSession session = new(new RhythmRules(), Calibration.MaxOffsetMs - 5);
            Assert.That(session.ChangeOffset(5), Is.True);
            Assert.That(session.ChangeOffset(5), Is.False);
            Assert.That(session.Offset, Is.EqualTo(Calibration.MaxOffsetMs));
            session.Start(MakeSong(), 10);
            Assert.That(session.ChangeOffset(-5), Is.False);
            Assert.That(session.Offset, Is.EqualTo(Calibration.MaxOffsetMs));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new RhythmSession(new RhythmRules(), 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new RhythmSession(new RhythmRules(), Calibration.MinOffsetMs - 5));
        }

        [Test]
        public void DelayedBatchesAreOrderedAndExpiredInputsCannotChangeResults()
        {
            var session = Start(0);
            session.StrikeAt(Strike.Spark, 12);
            session.StrikeAt(Strike.Pulse, 11);
            session.Advance(12.15);
            Assert.That(session.Score?.BestCombo, Is.EqualTo(2));
            session.StrikeAt(Strike.Pulse, 11);
            session.Advance(13);
            Assert.That(session.State, Is.EqualTo(Screen.Result));
            Assert.That(session.Score?.Good, Is.EqualTo(2));
            session.StrikeAt(Strike.Pulse, 14);
            Assert.That(session.Score?.Points, Is.EqualTo(200));
        }

        [Test]
        public void InputsArriveInOrderWhenTheyAreOlderThanTheGrace()
        {
            var session = Start(0);
            session.StrikeAt(Strike.Pulse, 11);
            session.Advance(11 + (RhythmSession.InputGraceMs - 1) / 1000.0);
            Assert.That(session.Score?.Good, Is.Zero);
            session.Advance(11 + RhythmSession.InputGraceMs / 1000.0);
            Assert.That(session.Score?.Good, Is.EqualTo(1));
        }

        [Test]
        public void PauseDiscardsPendingHitsAndReplayResetsScoring()
        {
            var session = Start(0);
            session.Advance(10.99);
            var position = session.Position;
            session.StrikeAt(Strike.Pulse, 11);
            session.TogglePause();
            session.Advance(500);
            session.StrikeAt(Strike.Pulse, 11);
            Assert.That(session.Position, Is.EqualTo(position));
            session.TogglePause();
            session.Advance(13);
            Assert.That(session.Score?.Miss, Is.EqualTo(2));

            var song = MakeSong();
            session.Start(song, 20);
            Assert.That(session.Score?.Miss, Is.Zero);
            session.Select();
            session.Advance(999);
            Assert.That(session.State, Is.EqualTo(Screen.Select));
            Assert.That(session.Score, Is.Null);
        }

        [Test]
        public void AllThreeChartsCanBePlayedForFullPoints()
        {
            foreach (var track in Arrangement.All)
            {
                var song = track.Song;
                RhythmSession session = new(new RhythmRules(), 0);
                session.Start(song, 1);
                foreach (var note in song.Notes)
                {
                    var time = 1 + (double)note.Sample / Song.SampleRate;
                    session.StrikeAt(note.Strike, time);
                    session.Advance(time + .101);
                }

                session.Advance(1 + (double)song.Length / Song.SampleRate);
                Assert.That(session.State, Is.EqualTo(Screen.Result), song.Name);
                Assert.That(session.Score?.Points, Is.EqualTo(song.Notes.Count * 100L), song.Name);
                Assert.That(session.Score?.BestCombo, Is.EqualTo(song.Notes.Count), song.Name);
            }
        }

        [TestCase(0, 150)]
        [TestCase(250, 150)]
        [TestCase(-100, -40)]
        public void CalibrationIgnoresTheCurrentOffsetAndAppliesTheMeasuredLag(int current, int lateMs)
        {
            RhythmSession session = new(new RhythmRules(), current);
            var song = PlayCalibration(session, i => lateMs / 1000.0);
            Assert.That(session.State, Is.EqualTo(Screen.Result));
            Assert.That(session.Calibrating, Is.True);
            Assert.That(session.TapCount, Is.EqualTo(song.Notes.Count));
            Assert.That(session.Measured, Is.EqualTo(lateMs));
            Assert.That(session.Offset, Is.EqualTo(lateMs));
            Assert.That(session.Score?.Points, Is.Zero);
        }

        [Test]
        public void CalibrationWithTooFewTapsKeepsTheOffset()
        {
            RhythmSession session = new(new RhythmRules(), 40);
            var song = Arrangement.Calibration.Song;
            PlayCalibration(session, i => i < song.Notes.Count / 2 - 1 ? .1 : (double?)null);
            Assert.That(session.Measured, Is.Null);
            Assert.That(session.Offset, Is.EqualTo(40));

            session.Start(MakeSong(), 100);
            Assert.That(session.Calibrating, Is.False);
            Assert.That(session.Measured, Is.Null);
        }

        private static Song PlayCalibration(RhythmSession session, Func<int, double?> late)
        {
            var song = Arrangement.Calibration.Song;
            session.StartCalibration(song, 1);
            for (var i = 0; i < song.Notes.Count; i++)
            {
                var time = 1 + (double)song.Notes[i].Sample / Song.SampleRate;
                if (late(i) is { } lag) { session.StrikeAt(i % 2 == 0 ? Strike.Pulse : Strike.Spark, time + lag); }
                session.Advance(time + .4);
            }

            session.Advance(1 + (double)song.Length / Song.SampleRate);
            return song;
        }

        private static RhythmSession Start(int offset)
        {
            RhythmSession session = new(new RhythmRules(), offset);
            session.Start(MakeSong(), 10);
            return session;
        }

        private static Song MakeSong() =>
            new("test", 3 * Song.SampleRate, new[] { new Note(Song.SampleRate, Strike.Pulse), new Note(2 * Song.SampleRate, Strike.Spark) },
                new[] { 0, Song.SampleRate });
    }
}
