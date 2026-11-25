#nullable enable
using System;
using Game.App;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// <see cref="AudioCueCounter"/> の累計差分から矩形波・三角波の効果音を再生する。
    /// 実行時に生成した <see cref="AudioClip"/> は <see cref="Dispose"/> で破棄する。
    /// </summary>
    public sealed class EmberlockAudio : IDisposable
    {
        private readonly AudioSource _source;
        private readonly AudioClip[] _clips;
        private int _cursorMove;
        private int _confirm;
        private int _cancel;
        private int _reject;
        private int _step;
        private int _portal;
        private int _chest;
        private int _doorUnlock;
        private int _attack;
        private int _spell;
        private int _damage;
        private int _heal;
        private int _levelUp;
        private int _victory;
        private int _defeat;
        private int _save;

        public EmberlockAudio(AudioSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _source.playOnAwake = false;
            _clips = new[]
            {
                MakeTone("Cursor", 660f, 660f, 0.025f, 0.7f),
                MakeTone("Confirm", 784f, 1046f, 0.055f, 0.9f),
                MakeTone("Cancel", 523f, 392f, 0.055f, 0.8f),
                MakeTone("Reject", 196f, 146f, 0.09f, 1.0f),
                MakeTone("Step", 240f, 180f, 0.02f, 0.45f),
                MakeTone("Portal", 330f, 660f, 0.14f, 0.85f),
                MakeTone("Chest", 523f, 1046f, 0.18f, 1.0f),
                MakeTone("Door", 220f, 440f, 0.16f, 0.95f),
                MakeTone("Attack", 480f, 180f, 0.06f, 0.95f),
                MakeTone("Spell", 587f, 1174f, 0.14f, 0.9f),
                MakeTone("Damage", 150f, 75f, 0.09f, 1.0f),
                MakeTone("Heal", 523f, 880f, 0.16f, 0.85f),
                MakeArpeggio("LevelUp", new[] { 523.25f, 659.25f, 783.99f, 1046.5f }, 0.065f),
                MakeArpeggio("Victory", new[] { 587.33f, 739.99f, 880f, 1174.66f }, 0.07f),
                MakeTone("Defeat", 261f, 110f, 0.28f, 1.0f),
                MakeArpeggio("Save", new[] { 659.25f, 783.99f, 987.77f }, 0.06f),
            };
        }

        public void SyncBaseline(AudioCueCounter cues)
        {
            if (cues == null)
            {
                return;
            }

            _cursorMove = cues.CursorMove;
            _confirm = cues.Confirm;
            _cancel = cues.Cancel;
            _reject = cues.Reject;
            _step = cues.Step;
            _portal = cues.Portal;
            _chest = cues.Chest;
            _doorUnlock = cues.DoorUnlock;
            _attack = cues.Attack;
            _spell = cues.Spell;
            _damage = cues.Damage;
            _heal = cues.Heal;
            _levelUp = cues.LevelUp;
            _victory = cues.Victory;
            _defeat = cues.Defeat;
            _save = cues.Save;
        }

        public void PlayNewCues(AudioCueCounter cues, float volume)
        {
            if (cues == null)
            {
                return;
            }

            var canPlay = !AudioPolicy.ShouldMute && volume > 0f;
            var vol = Mathf.Clamp01(volume);

            Consume(ref _cursorMove, cues.CursorMove, 0, canPlay, vol);
            Consume(ref _confirm, cues.Confirm, 1, canPlay, vol);
            Consume(ref _cancel, cues.Cancel, 2, canPlay, vol);
            Consume(ref _reject, cues.Reject, 3, canPlay, vol);
            Consume(ref _step, cues.Step, 4, canPlay, vol);
            Consume(ref _portal, cues.Portal, 5, canPlay, vol);
            Consume(ref _chest, cues.Chest, 6, canPlay, vol);
            Consume(ref _doorUnlock, cues.DoorUnlock, 7, canPlay, vol);
            Consume(ref _attack, cues.Attack, 8, canPlay, vol);
            Consume(ref _spell, cues.Spell, 9, canPlay, vol);
            Consume(ref _damage, cues.Damage, 10, canPlay, vol);
            Consume(ref _heal, cues.Heal, 11, canPlay, vol);
            Consume(ref _levelUp, cues.LevelUp, 12, canPlay, vol);
            Consume(ref _victory, cues.Victory, 13, canPlay, vol);
            Consume(ref _defeat, cues.Defeat, 14, canPlay, vol);
            Consume(ref _save, cues.Save, 15, canPlay, vol);
        }

        public void Dispose()
        {
            for (var i = 0; i < _clips.Length; i++)
            {
                if (_clips[i] != null)
                {
                    UnityEngine.Object.Destroy(_clips[i]);
                }
            }
        }

        private void Consume(ref int seen, int current, int clipIndex, bool canPlay, float vol)
        {
            if (current <= seen)
            {
                return;
            }

            seen = current;
            if (canPlay && clipIndex >= 0 && clipIndex < _clips.Length)
            {
                _source.PlayOneShot(_clips[clipIndex], vol);
            }
        }

        private static AudioClip MakeTone(string name, float startHz, float endHz, float seconds, float gain)
        {
            const int sampleRate = 22050;
            var samples = Mathf.Max(64, Mathf.RoundToInt(seconds * sampleRate));
            var buffer = new float[samples];
            var phase = 0f;
            for (var i = 0; i < samples; i++)
            {
                var t = i / (float)samples;
                var hz = Mathf.Lerp(startHz, endHz, t);
                phase += hz / sampleRate;
                var env = (1f - t) * gain;
                buffer[i] = (phase % 1f < 0.5f ? 1f : -1f) * env;
            }

            var clip = AudioClip.Create("Emberlock_" + name, samples, 1, sampleRate, stream: false);
            clip.SetData(buffer, 0);
            return clip;
        }

        private static AudioClip MakeArpeggio(string name, float[] notes, float stepSeconds)
        {
            const int sampleRate = 22050;
            var stepSamples = Mathf.Max(64, Mathf.RoundToInt(stepSeconds * sampleRate));
            var total = stepSamples * notes.Length;
            var buffer = new float[total];
            var phase = 0f;

            for (var n = 0; n < notes.Length; n++)
            {
                var hz = notes[n];
                for (var i = 0; i < stepSamples; i++)
                {
                    var t = i / (float)stepSamples;
                    phase += hz / sampleRate;
                    var env = 1f - (0.5f * t);
                    buffer[(n * stepSamples) + i] = (phase % 1f < 0.5f ? 0.9f : -0.9f) * env;
                }
            }

            var clip = AudioClip.Create("Emberlock_" + name, total, 1, sampleRate, stream: false);
            clip.SetData(buffer, 0);
            return clip;
        }
    }
}
