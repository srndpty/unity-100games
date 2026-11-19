using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Prism Tide Settings")]
    public sealed class PrismTideSettings : ScriptableObject
    {
        [Tooltip("色数・手数・目標スコア・得点・演出時間などのルール。時間は 60Hz の Tick 数。")]
        public Match3Config Rules = new();

        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            Rules.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 の範囲で指定してください。"); }
        }
    }
}
