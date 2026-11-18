using System;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Harbor Shift Settings")]
    public sealed class HarborShiftSettings : ScriptableObject
    {
        private const float MaxSeconds = 1f;
        private const float MinRepeatSeconds = .03f;

        [Range(0, MaxSeconds)] public float SlideSeconds = .07f;
        [Range(MinRepeatSeconds, MaxSeconds)] public float RepeatDelay = .22f;
        [Range(MinRepeatSeconds, MaxSeconds)] public float RepeatInterval = .11f;
        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            RequireRange(SlideSeconds, 0, MaxSeconds, nameof(SlideSeconds));
            RequireRange(RepeatDelay, MinRepeatSeconds, MaxSeconds, nameof(RepeatDelay));
            RequireRange(RepeatInterval, MinRepeatSeconds, MaxSeconds, nameof(RepeatInterval));
            RequireRange(Volume, 0, 1, nameof(Volume));
        }

        private static void RequireRange(float value, float min, float max, string name)
        {
            if (!float.IsFinite(value) || value < min || value > max) { throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。"); }
        }
    }
}
