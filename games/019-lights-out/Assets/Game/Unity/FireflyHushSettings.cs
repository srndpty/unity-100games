using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Firefly Hush Settings")]
    public sealed class FireflyHushSettings : ScriptableObject
    {
        public const float MaxFadeSeconds = 1f;

        public LightsOutConfig Puzzle = new();
        [Range(0, 1)] public float Volume = .12f;

        /// <summary>灯がつく・消える演出の時間（秒）。見た目だけで、盤の状態は押した時点で変わる。</summary>
        [Range(0, MaxFadeSeconds)] public float FadeSeconds = .18f;

        public void Validate()
        {
            Puzzle.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。"); }
            if (!float.IsFinite(FadeSeconds) || FadeSeconds < 0 || FadeSeconds > MaxFadeSeconds)
            {
                throw new ArgumentOutOfRangeException(nameof(FadeSeconds), FadeSeconds, $"0〜{MaxFadeSeconds} で指定してください。");
            }
        }
    }
}
