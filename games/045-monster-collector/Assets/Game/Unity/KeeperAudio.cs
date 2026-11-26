#nullable enable
using System;
using Game.App;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// <see cref="AudioCues"/> の累計の差から矩形波の効果音を鳴らす（039）。バッチ実行・撮影中は鳴らさない。
    /// 実行時に作った <see cref="AudioClip"/> は <see cref="Dispose"/> で破棄する。
    /// </summary>
    public sealed class KeeperAudio : IDisposable
    {
        private readonly AudioSource _source;
        private readonly AudioClip[] _clips;
        private readonly int[] _seen;

        public KeeperAudio(AudioSource source)
        {
            _source = source ? source : throw new ArgumentNullException(nameof(source));
            _source.playOnAwake = false;
            _clips = new[]
            {
                Tone("Cursor", 660f, 660f, 0.025f),
                Tone("Confirm", 784f, 1046f, 0.05f),
                Tone("Cancel", 523f, 392f, 0.05f),
                Tone("Reject", 196f, 146f, 0.09f),
                Tone("Bump", 150f, 120f, 0.04f),
                Arpeggio("Encounter", new[] { 392f, 523f, 659f, 784f }, 0.05f),
                Tone("Hit", 420f, 160f, 0.07f),
                Tone("SuperHit", 640f, 120f, 0.12f),
                Tone("WeakHit", 260f, 200f, 0.06f),
                Tone("Status", 330f, 290f, 0.12f),
                Tone("Faint", 300f, 80f, 0.25f),
                Tone("Ball", 900f, 500f, 0.08f),
                Arpeggio("Capture", new[] { 659f, 784f, 988f, 1318f }, 0.06f),
                Tone("Heal", 523f, 880f, 0.15f),
                Arpeggio("LevelUp", new[] { 523f, 659f, 784f, 1046f }, 0.06f),
                Arpeggio("Victory", new[] { 587f, 740f, 880f, 1175f }, 0.07f),
                Tone("Defeat", 261f, 110f, 0.28f),
                Arpeggio("Save", new[] { 659f, 784f, 988f }, 0.06f),
            };
            _seen = new int[_clips.Length];
        }

        private static int[] Counts(AudioCues c) => new[]
        {
            c.Cursor, c.Confirm, c.Cancel, c.Reject, c.Bump, c.Encounter, c.Hit, c.SuperHit, c.WeakHit, c.Status, c.Faint, c.Ball, c.Capture,
            c.Heal, c.LevelUp, c.Victory, c.Defeat, c.Save,
        };

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
                if (canPlay) { _source.PlayOneShot(_clips[i], Mathf.Clamp01(volume)); }
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

            var clip = AudioClip.Create("Thornwild_" + name, samples, 1, rate, stream: false);
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

            var clip = AudioClip.Create("Thornwild_" + name, buffer.Length, 1, rate, stream: false);
            clip.SetData(buffer, 0);
            return clip;
        }
    }
}
