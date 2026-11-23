using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    public enum Screen { Select, Play, Paused, Result }

    public sealed class RhythmSession
    {
        private const int InputGrace = 100 * Song.SamplesPerMillisecond;
        private readonly RhythmRules _rules;
        private readonly List<(long Sample, Strike Strike, long Order)> _pending = new();
        private int _offset;
        private long _nextOrder;
        private long _confirmed = -InputGrace;
        private double _start;

        public RhythmSession(RhythmRules rules)
        {
            rules.Validate();
            _rules = rules;
        }

        public Screen State { get; private set; }
        public Scorecard? Score { get; private set; }
        public long Position { get; private set; }

        public static double ToDspTime(double eventTime, double realNow, double dspNow) => dspNow + (eventTime - realNow);

        public static bool ValidOffset(int milliseconds) => milliseconds is >= -100 and <= 100 && milliseconds % 5 == 0;

        public void Start(Song song, int offsetMs, double startDsp)
        {
            if (State is not (Screen.Select or Screen.Result)) { return; }
            if (!ValidOffset(offsetMs)) { throw new ArgumentOutOfRangeException(nameof(offsetMs)); }
            if (!double.IsFinite(startDsp)) { throw new ArgumentOutOfRangeException(nameof(startDsp)); }

            Score = new Scorecard(song, _rules);
            _start = startDsp;
            _offset = offsetMs * Song.SamplesPerMillisecond;
            _confirmed = -InputGrace;
            Position = -Song.SampleRate;
            _pending.Clear();
            _nextOrder = 0;
            State = Screen.Play;
        }

        public void StrikeAt(Strike strike, double dsp)
        {
            if (State != Screen.Play || Score == null || !double.IsFinite(dsp)) { return; }

            var sample = SampleAt(dsp);
            if (sample < 0 || sample < _confirmed || sample >= Score.Song.Length) { return; }

            _pending.Add((sample, strike, _nextOrder++));
        }

        public void Advance(double dsp)
        {
            if (State != Screen.Play || Score == null || !double.IsFinite(dsp)) { return; }

            Position = Math.Max(Position, SampleAt(dsp));
            _confirmed = Math.Max(_confirmed, Position - InputGrace);
            _pending.Sort(CompareInput);
            var consumed = 0;
            foreach (var input in _pending)
            {
                if (input.Sample > _confirmed) { break; }

                Score.Hit(input.Sample - _offset, input.Strike);
                consumed++;
            }

            _pending.RemoveRange(0, consumed);
            Score.Advance(_confirmed - _offset);
            if (Position >= Score.Song.Length && Score.Complete)
            {
                State = Screen.Result;
                _pending.Clear();
            }
        }

        public void TogglePause()
        {
            if (State == Screen.Play)
            {
                _pending.Clear();
                State = Screen.Paused;
            }
            else if (State == Screen.Paused)
            {
                State = Screen.Play;
            }
        }

        public void Select()
        {
            _pending.Clear();
            Score = null;
            State = Screen.Select;
        }

        private long SampleAt(double dsp)
        {
            var samples = (dsp - _start) * Song.SampleRate;
            return (long)Math.Clamp(Math.Round(samples, MidpointRounding.AwayFromZero), -Song.SampleRate, int.MaxValue);
        }

        private static int CompareInput(
            (long Sample, Strike Strike, long Order) left, (long Sample, Strike Strike, long Order) right)
        {
            var time = left.Sample.CompareTo(right.Sample);
            return time == 0 ? left.Order.CompareTo(right.Order) : time;
        }
    }
}
