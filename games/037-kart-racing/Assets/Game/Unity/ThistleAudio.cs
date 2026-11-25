using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>エンジンは速さで高さを変える。ドリフト、ブースト、周回、結果、自分の車の接触は短い音。撮影とバッチでは鳴らさない。</summary>
    public sealed class ThistleAudio : MonoBehaviour
    {
        private AudioSource _engine;
        private AudioSource _oneShot;
        private AudioClip _tone;
        private float _volume = 0.12f;

        public void Bind(float volume)
        {
            _volume = volume;
            _tone = AudioClip.Create("thistle-tone", 4096, 1, 22050, false);
            var samples = new float[4096];
            for (var i = 0; i < samples.Length; i++) { samples[i] = Mathf.Sin(i * 0.28f) * 0.05f; }
            _tone.SetData(samples, 0);
            _engine = gameObject.AddComponent<AudioSource>();
            _engine.clip = _tone;
            _engine.loop = true;
            _engine.spatialBlend = 0f;
            _oneShot = gameObject.AddComponent<AudioSource>();
            _oneShot.spatialBlend = 0f;
            ApplyMute();
            _engine.Play();
        }

        public void Play(Race race, RaceMoment moment)
        {
            ApplyMute();
            if (_engine == null) { return; }
            var speed = Mathf.Abs(race.KartAt(0).Speed);
            _engine.pitch = 0.55f + (speed / 400f);
            _engine.volume = _volume * (race.Phase == RacePhase.Racing ? 1f : 0.35f);
            if ((moment & RaceMoment.Boost) != 0) { Blip(1.6f); }
            else if ((moment & RaceMoment.Lap) != 0) { Blip(1.1f); }
            else if ((moment & RaceMoment.Finished) != 0 || (moment & RaceMoment.Over) != 0) { Blip(0.7f); }
            else if ((moment & RaceMoment.Bump) != 0 && race.KartAt(0).Bumped) { Blip(0.5f); }
            else if (race.KartAt(0).DriftTicks > 0) { Blip(0.9f); }
        }

        private void Blip(float pitch)
        {
            if (AudioPolicy.ShouldMute || _oneShot == null) { return; }
            _oneShot.pitch = pitch;
            _oneShot.PlayOneShot(_tone, _volume);
        }

        private void ApplyMute()
        {
            var mute = AudioPolicy.ShouldMute;
            if (_engine != null) { _engine.mute = mute; }
            if (_oneShot != null) { _oneShot.mute = mute; }
        }

        private void OnDestroy()
        {
            if (_tone != null) { Destroy(_tone); }
        }
    }
}
