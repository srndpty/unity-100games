using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>試合の出来事に合わせて、実行時に合成した矩形波の効果音を鳴らす。</summary>
    public sealed class PongAudio
    {
        private const int SampleRate = 44100;
        // 矩形波は振幅の割に大きく聞こえるため、かなり小さめにする。
        private const float Volume = 0.05f;

        private readonly AudioSource _source;
        private readonly AudioClip _paddleHit = CreateTone("Paddle Hit", 440f, 0.06f);
        private readonly AudioClip _wallHit = CreateTone("Wall Hit", 220f, 0.05f);
        private readonly AudioClip _scored = CreateTone("Scored", 130f, 0.35f);
        private readonly AudioClip _matchWon = CreateTone("Match Won", 660f, 0.5f);

        public PongAudio(AudioSource source)
        {
            _source = source;

            // テストなど画面なしのバッチ実行では、裏で音が鳴らないように消音する。
            _source.mute = Application.isBatchMode;
        }

        public void Play(MatchEventKind kind)
        {
            var clip = kind switch
            {
                MatchEventKind.PaddleHit => _paddleHit,
                MatchEventKind.WallHit => _wallHit,
                MatchEventKind.Scored => _scored,
                MatchEventKind.MatchWon => _matchWon,
                _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
            _source.PlayOneShot(clip);
        }

        private static AudioClip CreateTone(string name, float frequency, float seconds)
        {
            var length = Mathf.CeilToInt(SampleRate * seconds);
            var samples = new float[length];
            for (var i = 0; i < length; i++)
            {
                var phase = (i * frequency / SampleRate) % 1f;
                var envelope = 1f - ((float)i / length);
                samples[i] = (phase < 0.5f ? 1f : -1f) * envelope * Volume;
            }

            var clip = AudioClip.Create(name, length, channels: 1, SampleRate, stream: false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
