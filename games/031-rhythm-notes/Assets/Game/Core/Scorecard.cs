using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum Strike { Pulse, Spark }

    public enum Grade { None, Good, Fair, Miss }

    // 「可」の打鍵がノーツより早かったか遅かったか。「良」とミスには付けない。
    public enum Timing { None, Fast, Slow }

    public readonly struct Note
    {
        public Note(int sample, Strike strike)
        {
            Sample = sample;
            Strike = strike;
        }

        public int Sample { get; }
        public Strike Strike { get; }
    }

    public sealed class Song
    {
        public const int SampleRate = 32000;
        public const int SamplesPerMillisecond = SampleRate / 1000;

        public Song(string name, int length, IEnumerable<Note> notes, IEnumerable<int> bars)
        {
            var copy = new List<Note>(notes);
            var previous = 0;
            foreach (var note in copy)
            {
                if (note.Sample <= previous || note.Sample >= length || !IsStrike(note.Strike))
                {
                    throw new ArgumentException("譜面の時刻または打種が無効です。", nameof(notes));
                }

                previous = note.Sample;
            }

            if (copy.Count == 0) { throw new ArgumentException("譜面が空です。", nameof(notes)); }

            var barCopy = new List<int>(bars);
            for (var i = 0; i < barCopy.Count; i++)
            {
                if (barCopy[i] < 0 || barCopy[i] >= length || (i > 0 && barCopy[i] <= barCopy[i - 1]))
                {
                    throw new ArgumentException("小節線の時刻が無効です。", nameof(bars));
                }
            }

            Name = name;
            Length = length;
            Notes = copy.AsReadOnly();
            Bars = barCopy.AsReadOnly();
        }

        public string Name { get; }
        public int Length { get; }
        public IReadOnlyList<Note> Notes { get; }
        public IReadOnlyList<int> Bars { get; }

        internal static bool IsStrike(Strike strike) => strike is Strike.Pulse or Strike.Spark;
    }

    [Serializable]
    public sealed class RhythmRules
    {
        public int GoodMs = 30;
        public int FairMs = 80;
        public int GoodPoints = 100;
        public int FairPoints = 50;

        public void Validate()
        {
            if (GoodMs <= 0 || FairMs <= GoodMs || FairMs > 200 ||
                FairPoints <= 0 || GoodPoints <= FairPoints || GoodPoints > 10000)
            {
                throw new InvalidOperationException("判定窓または配点が無効です。");
            }
        }
    }

    public sealed class Scorecard
    {
        private readonly Grade[] _grades;
        private readonly int _goodWindow;
        private readonly int _fairWindow;
        private readonly int _goodPoints;
        private readonly int _fairPoints;
        private int _first;

        public Scorecard(Song song, RhythmRules rules)
        {
            rules.Validate();
            Song = song;
            _goodWindow = rules.GoodMs * Song.SamplesPerMillisecond;
            _fairWindow = rules.FairMs * Song.SamplesPerMillisecond;
            _goodPoints = rules.GoodPoints;
            _fairPoints = rules.FairPoints;
            var margin = _fairWindow + Calibration.OffsetLimitMs * Song.SamplesPerMillisecond;
            if (song.Notes[0].Sample <= margin || song.Length - song.Notes[song.Notes.Count - 1].Sample <= margin)
            {
                throw new ArgumentException("譜面の前後に判定窓と補正幅を確保してください。", nameof(song));
            }

            _grades = new Grade[song.Notes.Count];
        }

        public Song Song { get; }
        public long Points { get; private set; }
        public int Good { get; private set; }
        public int Fair { get; private set; }
        public int Miss { get; private set; }
        public int Combo { get; private set; }
        public int BestCombo { get; private set; }
        public int Fast { get; private set; }
        public int Slow { get; private set; }
        public Grade Last { get; private set; }
        public Timing LastTiming { get; private set; }
        public bool Complete => _first == _grades.Length;

        // 全ノーツを判定し終え、ミスが1つもない。
        public bool FullCombo => Complete && Miss == 0;

        public Grade GradeAt(int index) => _grades[index];

        public void Advance(long sample)
        {
            while (_first < _grades.Length && sample > (long)Song.Notes[_first].Sample + _fairWindow)
            {
                Resolve(_first, Grade.Miss, Timing.None);
            }
        }

        public Grade Hit(long sample, Strike strike)
        {
            if (!Song.IsStrike(strike) || sample < 0 || sample >= Song.Length)
            {
                return Grade.None;
            }

            Advance(sample);
            var index = -1;
            var best = (long)_fairWindow + 1;
            var error = 0L;
            for (var i = _first; i < _grades.Length; i++)
            {
                var note = Song.Notes[i];
                if (note.Sample > sample + _fairWindow) { break; }

                var distance = Math.Abs(sample - note.Sample);
                if (_grades[i] == Grade.None && note.Strike == strike && distance < best)
                {
                    index = i;
                    best = distance;
                    error = sample - note.Sample;
                }
            }

            if (index < 0) { return Grade.None; }

            var grade = best <= _goodWindow ? Grade.Good : Grade.Fair;
            Resolve(index, grade, grade == Grade.Fair ? (error < 0 ? Timing.Fast : Timing.Slow) : Timing.None);
            return grade;
        }

        private void Resolve(int index, Grade grade, Timing timing)
        {
            _grades[index] = grade;
            Last = grade;
            LastTiming = timing;
            if (timing == Timing.Fast) { Fast++; }
            if (timing == Timing.Slow) { Slow++; }
            switch (grade)
            {
                case Grade.Good:
                    Good++;
                    Points += _goodPoints;
                    break;
                case Grade.Fair:
                    Fair++;
                    Points += _fairPoints;
                    break;
                default:
                    Miss++;
                    Combo = 0;
                    break;
            }

            if (grade != Grade.Miss)
            {
                Combo++;
                BestCombo = Math.Max(Combo, BestCombo);
            }

            while (_first < _grades.Length && _grades[_first] != Grade.None) { _first++; }
        }
    }
}
