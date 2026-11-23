using System;
using System.Collections.Generic;

namespace Game.Core
{
    // 音ずれ補正の範囲と、補正テストの打鍵から補正値を求める規則。
    public static class Calibration
    {
        public const int MinOffsetMs = -200;
        public const int MaxOffsetMs = 300;
        public const int StepMs = 5;
        // 補正で時計がずれる最大量。譜面の前後に判定窓とこの幅の余白を求める。
        public const int OffsetLimitMs = -MinOffsetMs > MaxOffsetMs ? -MinOffsetMs : MaxOffsetMs;

        public static bool ValidOffset(int milliseconds) =>
            milliseconds is >= MinOffsetMs and <= MaxOffsetMs && milliseconds % StepMs == 0;

        public static int ClampOffset(int milliseconds) => Math.Clamp(milliseconds, MinOffsetMs, MaxOffsetMs);

        // 各ノーツに、隣のノーツとの間隔の半分未満で最も近い打鍵を1つだけ対応させ、ずれの中央値を補正値にする。
        // 対応した打鍵がノーツの半数に満たなければ測定できない（null）。
        public static int? Estimate(Song song, IReadOnlyList<long> taps)
        {
            var notes = song.Notes;
            var gap = long.MaxValue;
            for (var i = 1; i < notes.Count; i++) { gap = Math.Min(gap, notes[i].Sample - notes[i - 1].Sample); }

            var reach = notes.Count == 1 ? (long)(MaxOffsetMs + StepMs) * Song.SamplesPerMillisecond : (gap + 1) / 2;
            var errors = new List<long>();
            foreach (var note in notes)
            {
                long? best = null;
                foreach (var tap in taps)
                {
                    var error = tap - note.Sample;
                    if (Math.Abs(error) < reach && (best == null || Math.Abs(error) < Math.Abs(best.Value))) { best = error; }
                }

                if (best is { } found) { errors.Add(found); }
            }

            if (errors.Count * 2 < notes.Count) { return null; }

            errors.Sort();
            var middle = errors.Count / 2;
            var twice = errors.Count % 2 == 1 ? 2 * errors[middle] : errors[middle - 1] + errors[middle];
            var steps = Math.Round(twice / (2.0 * StepMs * Song.SamplesPerMillisecond), MidpointRounding.AwayFromZero);
            return ClampOffset((int)steps * StepMs);
        }
    }
}
