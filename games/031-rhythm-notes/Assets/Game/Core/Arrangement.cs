using System;
using System.Collections.Generic;

namespace Game.Core
{
    public sealed class Arrangement
    {
        private const int EighthsPerBar = 8;

        private Arrangement(string name, int tempo, int eighths, int[] melody, Func<int, Strike?> mark)
        {
            Tempo = tempo;
            Eighths = eighths;
            Melody = Array.AsReadOnly(melody);
            var notes = new List<Note>();
            for (var step = 0; step < eighths; step++)
            {
                if (mark(step) is { } strike) { notes.Add(new Note(SampleAt(step), strike)); }
            }

            var bars = new List<int>();
            for (var step = 0; step < eighths; step += EighthsPerBar) { bars.Add(SampleAt(step)); }

            Song = new Song(name, SampleAt(eighths), notes, bars);
        }

        public int Tempo { get; }
        public int Eighths { get; }
        public IReadOnlyList<int> Melody { get; }
        public Song Song { get; }

        public int SampleAt(int eighthNote) => (int)Math.Round(eighthNote * (30.0 * Song.SampleRate / Tempo));

        public static IReadOnlyList<Arrangement> All { get; } = Array.AsReadOnly(new Arrangement[]
        {
            Chart("港の灯", 96, "F.J.F.J.", "F.F.J.F.", new[] { 60, 64, 67, 69, 67, 64, 62, 67 }),
            Chart("雨上がり", 120, "F.JF..JF", "FJ..F.J.", new[] { 62, 65, 69, 72, 74, 72, 69, 65 }),
            Chart("夜の軌道", 144, "FJF.JFJ.", "F.JFJFJJ", new[] { 57, 60, 64, 67, 69, 67, 64, 62 }),
        });

        // 補正テスト：1小節のカウントの後、4分音符を8小節。旋律はなく、拍のクリックだけを鳴らす。
        public static Arrangement Calibration { get; } = new(
            "補正テスト", 90, 10 * EighthsPerBar, Array.Empty<int>(),
            step => step >= EighthsPerBar && step < 9 * EighthsPerBar && step % 2 == 0 ? Strike.Pulse : null);

        private static Arrangement Chart(string name, int tempo, string verse, string chorus, int[] melody) =>
            new(name, tempo, 12 * EighthsPerBar, melody, step =>
            {
                if (step < 2 || step >= 94) { return null; }

                var pattern = step / 8 % 4 < 2 ? verse : chorus;
                return pattern[step % 8] switch
                {
                    'F' => Strike.Pulse,
                    'J' => Strike.Spark,
                    _ => null,
                };
            });
    }
}
