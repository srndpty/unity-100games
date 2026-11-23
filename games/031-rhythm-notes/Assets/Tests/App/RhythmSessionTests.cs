using System;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class RhythmSessionTests
    {
        [TestCase(-100)]
        [TestCase(0)]
        [TestCase(100)]
        public void TimestampConversionAndOffsetRecoverTheOriginalBeat(int offset)
        {
            var session = Start(offset);
            var input = RhythmSession.ToDspTime(31 + offset / 1000.0, 31.2, 11.2);
            session.StrikeAt(Strike.Pulse, input);
            session.Advance(11.3);
            Assert.That(session.Score?.Good, Is.EqualTo(1));
            Assert.That(session.Score?.Miss, Is.Zero);
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
            session.Start(song, 0, 20);
            Assert.That(session.Score?.Miss, Is.Zero);
            session.Select();
            session.Advance(999);
            Assert.That(session.State, Is.EqualTo(Screen.Select));
            Assert.That(session.Score, Is.Null);
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Start(song, 3, 20));
        }

        [Test]
        public void AllThreeChartsCanBePlayedForFullPoints()
        {
            foreach (var track in Arrangement.All)
            {
                var song = track.Song;
                RhythmSession session = new(new RhythmRules());
                session.Start(song, 0, 1);
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

        private static RhythmSession Start(int offset)
        {
            RhythmSession session = new(new RhythmRules());
            session.Start(MakeSong(), offset, 10);
            return session;
        }

        private static Song MakeSong() =>
            new("test", 3 * Song.SampleRate, new[] { new Note(Song.SampleRate, Strike.Pulse), new Note(2 * Song.SampleRate, Strike.Spark) });
    }
}
