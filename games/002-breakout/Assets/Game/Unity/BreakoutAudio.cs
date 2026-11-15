using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>ゲームの出来事に合わせて、実行時に合成した矩形波の効果音を鳴らす。合成したクリップはこのクラスが所有し <see cref="Dispose"/> で破棄する。</summary>
    public sealed class BreakoutAudio : System.IDisposable
    {
        private const int SampleRate = 44100;
        // 矩形波は振幅の割に大きく聞こえるため、かなり小さめにする。
        private const float Volume = 0.05f;

        private readonly AudioSource _source;
        private readonly AudioClip _paddleHit = CreateTone("Paddle Hit", 440f, 0.06f);
        private readonly AudioClip _wallHit = CreateTone("Wall Hit", 220f, 0.04f);
        private readonly AudioClip _brickHit = CreateTone("Brick Hit", 330f, 0.05f);
        private readonly AudioClip _brickDestroyed = CreateTone("Brick Destroyed", 660f, 0.06f);
        private readonly AudioClip _powerUp = CreateTone("Power Up", 880f, 0.25f);
        private readonly AudioClip _lifeLost = CreateTone("Life Lost", 110f, 0.4f);
        private readonly AudioClip _stageCleared = CreateTone("Stage Cleared", 990f, 0.4f);
        private readonly AudioClip _gameOver = CreateTone("Game Over", 82f, 0.6f);

        public BreakoutAudio(AudioSource source)
        {
            _source = source;
            _source.mute = AudioPolicy.ShouldMute;
        }

        public void Play(GameEventKind kind)
        {
            var clip = kind switch
            {
                GameEventKind.PaddleHit => _paddleHit,
                GameEventKind.WallHit => _wallHit,
                GameEventKind.BrickHit => _brickHit,
                GameEventKind.BrickDestroyed => _brickDestroyed,
                GameEventKind.EffectStarted => _powerUp,
                GameEventKind.LifeLost => _lifeLost,
                GameEventKind.StageCleared => _stageCleared,
                GameEventKind.GameOver => _gameOver,
                // 音を鳴らさない出来事（勝利は面クリアの音で足りる）。
                GameEventKind.Launched or GameEventKind.CapsuleDropped or GameEventKind.EffectExpired
                    or GameEventKind.StageStarted or GameEventKind.GameWon => null,
                _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, null),
            };
            if (clip != null)
            {
                _source.PlayOneShot(clip);
            }
        }

        public void Dispose()
        {
            _source.Stop();
            Object.Destroy(_paddleHit);
            Object.Destroy(_wallHit);
            Object.Destroy(_brickHit);
            Object.Destroy(_brickDestroyed);
            Object.Destroy(_powerUp);
            Object.Destroy(_lifeLost);
            Object.Destroy(_stageCleared);
            Object.Destroy(_gameOver);
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
