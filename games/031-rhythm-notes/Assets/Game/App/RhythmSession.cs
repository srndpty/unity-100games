using System;
using System.Collections.Generic;
using Game.Core;

namespace Game.App
{
    public enum Screen { Select, Play, Paused, Result }

    public sealed class RhythmSession
    {
        // 打鍵を曲時刻順に並べてから採点するための保留時間。DSP 時刻の更新間隔（バッファ1つ分）より長く取る。
        public const int InputGraceMs = 50;
        private const int InputGrace = InputGraceMs * Song.SamplesPerMillisecond;
        private readonly RhythmRules _rules;
        private readonly List<(long Sample, Strike Strike, long Order)> _pending = new();
        private readonly List<long> _taps = new();
        private int _clockOffset;
        private long _nextOrder;
        private long _confirmed = -InputGrace;
        private long _played;
        private double _start;

        public RhythmSession(RhythmRules rules, int offsetMs)
        {
            rules.Validate();
            if (!Calibration.ValidOffset(offsetMs)) { throw new ArgumentOutOfRangeException(nameof(offsetMs)); }

            _rules = rules;
            Offset = offsetMs;
        }

        public Screen State { get; private set; }
        public Scorecard? Score { get; private set; }

        // 聞こえている曲の位置。補正値のぶん DSP 時刻より遅らせ、ノーツの表示と判定の両方に使う。
        public long Position { get; private set; }

        // 音ずれ補正（ms）。正の値は音が遅れて聞こえる環境で、ノーツと判定を遅らせる。
        public int Offset { get; private set; }

        // 補正を引く前の、音源の再生位置。曲の終わりはこちらで決める（補正で結果画面へ移る時刻がずれない）。
        public long Played => _played;

        public bool Calibrating { get; private set; }
        public int TapCount => _taps.Count;

        // 直前の補正テストで測ったずれ。測れなかったときは null。
        public int? Measured { get; private set; }

        public static double ToDspTime(double eventTime, double realNow, double dspNow) => dspNow + (eventTime - realNow);

        public bool ChangeOffset(int deltaMs)
        {
            if (State != Screen.Select) { return false; }

            var next = Calibration.ClampOffset(Offset + deltaMs);
            if (next == Offset || !Calibration.ValidOffset(next)) { return false; }

            Offset = next;
            return true;
        }

        public void Start(Song song, double startDsp) => Begin(song, startDsp, false);

        // 補正テストは補正なしの時計で打鍵を記録し、曲の終わりに補正値を測り直す。
        public void StartCalibration(Song song, double startDsp) => Begin(song, startDsp, true);

        public void StrikeAt(Strike strike, double dsp)
        {
            if (State != Screen.Play || Score == null || !double.IsFinite(dsp)) { return; }

            var sample = PlayedAt(dsp) - _clockOffset;
            if (sample < 0 || sample < _confirmed || sample >= Score.Song.Length) { return; }

            _pending.Add((sample, strike, _nextOrder++));
        }

        public void Advance(double dsp)
        {
            if (State != Screen.Play || Score == null || !double.IsFinite(dsp)) { return; }

            _played = Math.Max(_played, PlayedAt(dsp));
            Position = Math.Max(Position, _played - _clockOffset);
            _confirmed = Math.Max(_confirmed, Position - InputGrace);
            _pending.Sort(CompareInput);
            var consumed = 0;
            foreach (var input in _pending)
            {
                if (input.Sample > _confirmed) { break; }

                if (Calibrating) { _taps.Add(input.Sample); }
                else { Score.Hit(input.Sample, input.Strike); }
                consumed++;
            }

            _pending.RemoveRange(0, consumed);
            Score.Advance(_confirmed);
            if (_played >= Score.Song.Length && Score.Complete)
            {
                State = Screen.Result;
                _pending.Clear();
                if (Calibrating) { Finish(Score.Song); }
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

        private void Begin(Song song, double startDsp, bool calibrating)
        {
            if (State is not (Screen.Select or Screen.Result)) { return; }
            if (!double.IsFinite(startDsp)) { throw new ArgumentOutOfRangeException(nameof(startDsp)); }

            Score = new Scorecard(song, _rules);
            Calibrating = calibrating;
            Measured = null;
            _taps.Clear();
            _start = startDsp;
            _clockOffset = calibrating ? 0 : Offset * Song.SamplesPerMillisecond;
            _confirmed = -InputGrace;
            _played = -Song.SampleRate;
            Position = _played - _clockOffset;
            _pending.Clear();
            _nextOrder = 0;
            State = Screen.Play;
        }

        private void Finish(Song song)
        {
            Measured = Calibration.Estimate(song, _taps);
            if (Measured is { } measured) { Offset = measured; }
        }

        private long PlayedAt(double dsp)
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
