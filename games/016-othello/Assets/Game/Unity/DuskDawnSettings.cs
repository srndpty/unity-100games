using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Dusk Dawn Settings")]
    public sealed class DuskDawnSettings : ScriptableObject
    {
        [Tooltip("演出の時間（60Hz の Tick 数）と、CPU の強さごとの探索深さ（1〜5）。")]
        public OthelloConfig Rules = new();

        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            Rules.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 の範囲で指定してください。"); }
        }
    }
}
