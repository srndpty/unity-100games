#nullable enable
using System;
using Game.App;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// <see cref="AudioCues"/> の累計の差から短い効果音を鳴らす（039）。音は小さく、バッチ実行・撮影中は鳴らさない。
    /// 実行時に作った <see cref="AudioClip"/> は <see cref="Dispose"/> で破棄する。
    /// </summary>
    public sealed class SekiteiAudio : IDisposable
    {
        private readonly AudioSource _source;
        private readonly AudioClip[] _clips;
        private readonly float[] _gains;
        private readonly int[] _seen;

        public SekiteiAudio(AudioSource source)
        {
            _source = source ? source : throw new ArgumentNullException(nameof(source));
            _source.playOnAwake = false;
            _clips = new[]
            {
                Click("Place", 0.035f, 1),
                Click("Capture", 0.03f, 3),
                Arpeggio("Pass", new[] { 523f, 392f }, 0.06f),
                Tone("Reject", 196f, 146f, 0.09f),
                Tone("Cursor", 660f, 660f, 0.02f),
                Arpeggio("Choose", new[] { 523f, 784f }, 0.05f),
                Arpeggio("End", new[] { 392f, 523f, 659f, 784f }, 0.09f),
            };
            _gains = new[] { 1f, 0.9f, 0.8f, 1f, 0.5f, 0.9f, 1f };
            _seen = new int[_clips.Length];
        }

        private static int[] Counts(AudioCues c) => new[] { c.Place, c.Capture, c.Pass, c.Reject, c.Cursor, c.Choose, c.End };

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
            for (var i = 0; i < _seen.Length; i++)
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

        /// <summary>碁石を置く音の代わりの、短く減衰する打音（決まった種の乱数のノイズ）。</summary>
        private static AudioClip Click(string name, float seconds, int repeats)
        {
            const int rate = 22050;
            var n = Mathf.Max(64, Mathf.RoundToInt(seconds * rate));
            var buffer = new float[n * repeats];
            uint state = 0x12345u;
            for (var k = 0; k < repeats; k++)
            {
                for (var i = 0; i < n; i++)
                {
                    state ^= state << 13;
                    state ^= state >> 17;
                    state ^= state << 5;
                    var noise = ((state & 0xFFFF) / 32768f) - 1f;
                    var t = i / (float)n;
                    var body = Mathf.Sin(2f * Mathf.PI * 900f * i / rate);
                    buffer[(k * n) + i] = ((0.6f * noise) + (0.4f * body)) * Mathf.Pow(1f - t, 4f);
                }
            }

            var clip = AudioClip.Create("Sekitei_" + name, buffer.Length, 1, rate, stream: false);
            clip.SetData(buffer, 0);
            return clip;
        }

        private static AudioClip Tone(string name, float startHz, float endHz, float seconds)
        {
            const int rate = 22050;
            var samples = Mathf.Max(64, Mathf.RoundToInt(seconds * rate));
            var buffer = new float[samples];
            var phase = 0f;
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)samples;
                phase += Mathf.Lerp(startHz, endHz, t) / rate;
                buffer[i] = (phase % 1f < 0.5f ? 1f : -1f) * (1f - t);
            }

            var clip = AudioClip.Create("Sekitei_" + name, samples, 1, rate, stream: false);
            clip.SetData(buffer, 0);
            return clip;
        }

        private static AudioClip Arpeggio(string name, float[] notes, float step)
        {
            const int rate = 22050;
            var n = Mathf.Max(64, Mathf.RoundToInt(step * rate));
            var buffer = new float[n * notes.Length];
            var phase = 0f;
            for (var k = 0; k < notes.Length; k++)
            {
                for (var i = 0; i < n; i++)
                {
                    phase += notes[k] / rate;
                    buffer[(k * n) + i] = (phase % 1f < 0.5f ? 0.9f : -0.9f) * (1f - (0.5f * i / n));
                }
            }

            var clip = AudioClip.Create("Sekitei_" + name, buffer.Length, 1, rate, stream: false);
            clip.SetData(buffer, 0);
            return clip;
        }
    }
}
