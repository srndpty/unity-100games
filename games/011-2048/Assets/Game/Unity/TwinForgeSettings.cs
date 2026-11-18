using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Twin Forge Settings")]
    public sealed class TwinForgeSettings : ScriptableObject
    {
        private const float MaxAnimationSeconds = 1f;

        public ForgeRules Rules = new();
        [Range(0, MaxAnimationSeconds)] public float SlideSeconds = .09f;
        [Range(0, MaxAnimationSeconds)] public float PopSeconds = .11f;
        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            Rules.Validate();
            RequireRange(SlideSeconds, MaxAnimationSeconds, nameof(SlideSeconds));
            RequireRange(PopSeconds, MaxAnimationSeconds, nameof(PopSeconds));
            RequireRange(Volume, 1, nameof(Volume));
        }

        private static void RequireRange(float value, float max, string name)
        {
            if (!float.IsFinite(value) || value < 0 || value > max) { throw new ArgumentOutOfRangeException(name, value, $"0〜{max} の範囲で指定してください。"); }
        }
    }
}
