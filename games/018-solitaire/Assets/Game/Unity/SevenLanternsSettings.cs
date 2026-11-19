using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Seven Lanterns Settings")]
    public sealed class SevenLanternsSettings : ScriptableObject
    {
        private const float MaxDoubleClickSeconds = 1f;
        private const float MaxSlideSharpness = 100f;

        [Tooltip("ルール。めくる枚数（1 または 3）と、手詰まりの探索で調べる局面数の上限。")]
        public SolitaireConfig Rules = new();

        [Tooltip("ダブルクリックとみなす 2 回のクリックの間隔（秒）。")]
        public float DoubleClickSeconds = .35f;

        [Tooltip("札が新しい位置へ寄る速さ（1 秒あたりの減衰の係数。大きいほど速い）。0 で瞬時に動く。")]
        public float SlideSharpness = 18f;

        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            Rules.Validate();
            RequireRange(DoubleClickSeconds, 0, MaxDoubleClickSeconds, nameof(DoubleClickSeconds));
            RequireRange(SlideSharpness, 0, MaxSlideSharpness, nameof(SlideSharpness));
            RequireRange(Volume, 0, 1, nameof(Volume));
        }

        private static void RequireRange(float value, float min, float max, string name)
        {
            if (!float.IsFinite(value) || value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{min}〜{max} の範囲で指定してください。");
            }
        }
    }
}
