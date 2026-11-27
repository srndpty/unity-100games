#nullable enable
using System;
using Game.App;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// <see cref="AudioCues"/> の累計の差から短い効果音を鳴らす（039）。バッチ実行・撮影中は鳴らさない。
    /// 実行時に作った <see cref="AudioClip"/> は <see cref="Dispose"/> で破棄する。
    /// </summary>
    public sealed class EscapeAudio : IDisposable
    {
        private readonly AudioSource _source;
        private readonly AudioClip[] _clips;
        private readonly float[] _gains;
        private readonly int[] _seen;

        public EscapeAudio(AudioSource source)
        {
            _source = source ? source : throw new ArgumentNullException(nameof(source));
            _source.playOnAwake = false;
            _clips = new[]
            {
                Tone("Cursor", 660f, 660f, 0.025f),
                Arpeggio("Choose", new[] { 523f, 784f }, 0.05f),
                Tone("Move", 330f, 392f, 0.05f),
                Tone("Click", 1175f, 1100f, 0.02f),
                Tone("Reject", 196f, 146f, 0.09f),
                Arpeggio("Pickup", new[] { 784f, 1047f }, 0.05f),
                Tone("Install", 262f, 523f, 0.08f),
                Tone("Mistake", 220f, 110f, 0.14f),
                Arpeggio("Clue", new[] { 1047f, 1319f }, 0.04f),
                Arpeggio("Solve", new[] { 523f, 659f, 784f, 1047f }, 0.07f),
                Arpeggio("Hint", new[] { 880f, 660f }, 0.05f),
                Arpeggio("Escape", new[] { 392f, 494f, 587f, 784f, 988f }, 0.12f),
            };
            _gains = new[] { 0.6f, 1f, 0.5f, 0.5f, 1f, 1f, 0.9f, 1f, 0.8f, 1f, 0.8f, 1f };
            _seen = new int[_clips.Length];
        }

        private static int[] Counts(AudioCues c) => new[] { c.Cursor, c.Choose, c.Move, c.Click, c.Reject, c.Pickup, c.Install, c.Mistake, c.Clue, c.Solve, c.Hint, c.Escape };

        /// <summary>今の数を「鳴らした」として覚える（Flow を作り直したとき）。</summary>
        public void SyncBaseline(AudioCues cues)
        {
            var counts = Counts(cues);
            Array.Copy(counts, _seen, _seen.Length);
        }

        public void PlayNew(AudioCues cues, float volume)
        {
            var counts = Counts(cues);
            var canPlay = !AudioPolicy.ShouldMute && volume > 0f;
            for (int i = 0; i < _seen.Length; i++)
            {
                if (counts[i] <= _seen[i]) { continue; }
                _seen[i] = counts[i];
                if (canPlay) { _source.PlayOneShot(_clips[i], Mathf.Clamp01(volume * _gains[i])); }
            }
        }

        public void Dispose()
        {
            foreach (var c in _clips)
            {
                if (c != null) { UnityEngine.Object.Destroy(c); }
            }
        }

        private static AudioClip Tone(string name, float startHz, float endHz, float seconds)
        {
            const int rate = 22050;
            var samples = Mathf.Max(64, Mathf.RoundToInt(seconds * rate));
            var buffer = new float[samples];
            var phase = 0f;
            for (int i = 0; i < samples; i++)
            {
                var t = i / (float)samples;
                phase += Mathf.Lerp(startHz, endHz, t) / rate;
                buffer[i] = (phase % 1f < 0.5f ? 1f : -1f) * (1f - t);
            }

            var clip = AudioClip.Create("Stargazer_" + name, samples, 1, rate, stream: false);
            clip.SetData(buffer, 0);
            return clip;
        }

        private static AudioClip Arpeggio(string name, float[] notes, float step)
        {
            const int rate = 22050;
            var n = Mathf.Max(64, Mathf.RoundToInt(step * rate));
            var buffer = new float[n * notes.Length];
            var phase = 0f;
            for (int k = 0; k < notes.Length; k++)
            {
                for (int i = 0; i < n; i++)
                {
                    phase += notes[k] / rate;
                    buffer[k * n + i] = (phase % 1f < 0.5f ? 0.9f : -0.9f) * (1f - 0.5f * i / n);
                }
            }

            var clip = AudioClip.Create("Stargazer_" + name, buffer.Length, 1, rate, stream: false);
            clip.SetData(buffer, 0);
            return clip;
        }
    }
}
