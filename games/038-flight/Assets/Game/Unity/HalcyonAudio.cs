using Game.App;
using Game.Core;
using Game.Unity.Evidence;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// エンジンはスロットルと速さで高さを変える。ロック中は短い音を繰り返し、LOCK で高い音、発射・命中・被弾は短い音。
    /// 自分を追うミサイルが近い間は警告音を鳴らす。音は起動時に合成し、撮影とバッチでは鳴らさない。
    /// </summary>
    public sealed class HalcyonAudio : MonoBehaviour
    {
        private AudioSource _engine;
        private AudioSource _oneShot;
        private AudioClip _tone;
        private AudioClip _noise;
        private float _volume = 0.12f;
        private int _lastBeepTick = NoBeep;
        private Mission _beepMission;

        private const int NoBeep = -1000;

        /// <summary>直近に繰り返し音を鳴らした Tick（テスト用）。試合が替わると <see cref="NoBeep"/> に戻る。</summary>
        public int LastBeepTick => _lastBeepTick;

        public void Bind(float volume)
        {
            _volume = volume;
            _tone = AudioClip.Create("halcyon-tone", 4096, 1, 22050, false);
            var samples = new float[4096];
            for (var i = 0; i < samples.Length; i++) { samples[i] = Mathf.Sign(Mathf.Sin(i * 0.25f)) * 0.05f; }
            _tone.SetData(samples, 0);
            _noise = AudioClip.Create("halcyon-noise", 8192, 1, 22050, false);
            var noise = new float[8192];
            uint state = 0x12345678u;
            for (var i = 0; i < noise.Length; i++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                var fade = 1f - (i / (float)noise.Length);
                noise[i] = (((state & 0xFFFF) / 32767.5f) - 1f) * 0.08f * fade;
            }

            _noise.SetData(noise, 0);
            _engine = gameObject.AddComponent<AudioSource>();
            _engine.clip = _noise;
            _engine.loop = true;
            _engine.spatialBlend = 0f;
            _oneShot = gameObject.AddComponent<AudioSource>();
            _oneShot.spatialBlend = 0f;
            ApplyMute();
            _engine.Play();
        }

        public void Play(SortieFlow flow, MissionMoment moments)
        {
            ApplyMute();
            if (_engine == null) { return; }
            var mission = flow.Mission;

            // 再挑戦・タイトルで試合が替わると Tick が 0 に戻るので、前の試合の最後の音の Tick を捨てる。
            if (!ReferenceEquals(mission, _beepMission))
            {
                _beepMission = mission;
                _lastBeepTick = NoBeep;
            }

            var view = mission.Player.View;
            var playing = flow.Screen == AppScreen.Playing && !mission.Player.Down;
            _engine.pitch = 0.4f + (view.Throttle / 1000f * 0.5f) + (view.Air.Speed / (float)(300 * Units.MetersPerSecond));
            _engine.volume = playing ? _volume * 0.6f : 0f;

            if ((moments & (MissionMoment.EnemyDown | MissionMoment.PlayerHit)) != 0) { Play(_noise, 0.45f, 1.6f); }
            else if ((moments & MissionMoment.PlayerFired) != 0) { Play(_noise, 1.4f, 1f); }
            else if ((moments & MissionMoment.Locked) != 0) { Play(_tone, 2.0f, 1f); }
            if (!playing) { return; }

            // ロック中・警告の繰り返し音は Tick で間隔を決める。
            var tick = mission.Tick;
            var interval = 0;
            var pitch = 1f;
            if (Incoming(mission)) { interval = 8; pitch = 2.4f; }
            else if (!mission.PlayerLocked && mission.Player.LockTarget > 0) { interval = 15; pitch = 1.2f; }
            if (interval > 0 && tick - _lastBeepTick >= interval)
            {
                _lastBeepTick = tick;
                Play(_tone, pitch, 0.6f);
            }
        }

        private static bool Incoming(Mission mission)
        {
            var position = mission.Player.View.Position;
            foreach (var missile in mission.Missiles)
            {
                if (missile.Active && missile.Tracking && missile.Target == 0 && (missile.Position - position).Length < HalcyonHud.MissileWarningRange) { return true; }
            }

            return false;
        }

        private void Play(AudioClip clip, float pitch, float scale)
        {
            if (AudioPolicy.ShouldMute || _oneShot == null) { return; }
            _oneShot.pitch = pitch;
            _oneShot.PlayOneShot(clip, _volume * scale);
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
            if (_noise != null) { Destroy(_noise); }
        }
    }
}
