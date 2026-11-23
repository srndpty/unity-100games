using System;
using System.Collections.Generic;

namespace Game.Core
{
    public sealed class Arrangement
    {
        private Arrangement(string name, int tempo, string verse, string chorus, int[] melody)
        {
            Tempo = tempo;
            Melody = Array.AsReadOnly(melody);
            var notes = new List<Note>();
            for (var step = 2; step < 94; step++)
            {
                var pattern = step / 8 % 4 < 2 ? verse : chorus;
                var mark = pattern[step % 8];
                if (mark != '.') { notes.Add(new Note(SampleAt(step), mark == 'F' ? Strike.Pulse : Strike.Spark)); }
            }

            Song = new Song(name, SampleAt(96), notes);
        }

        public int Tempo { get; }
        public IReadOnlyList<int> Melody { get; }
        public Song Song { get; }

        public int SampleAt(int eighthNote) => (int)Math.Round(eighthNote * (30.0 * Song.SampleRate / Tempo));

        public static IReadOnlyList<Arrangement> All { get; } = Array.AsReadOnly(new Arrangement[]
        {
            new("港の灯", 96, "F.J.F.J.", "F.F.J.F.", new[] { 60, 64, 67, 69, 67, 64, 62, 67 }),
            new("雨上がり", 120, "F.JF..JF", "FJ..F.J.", new[] { 62, 65, 69, 72, 74, 72, 69, 65 }),
            new("夜の軌道", 144, "FJF.JFJ.", "F.JFJFJJ", new[] { 57, 60, 64, 67, 69, 67, 64, 62 }),
        });
    }
}
