using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Thistle Settings")]
    public sealed class ThistleSettings : ScriptableObject
    {
        public KartConfig Rules = new();

        public TextAsset Track;

        [Range(0f, 1f)] public float Volume = 0.12f;

        public KartConfig Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            if (Track == null) { throw new InvalidOperationException("コースの TextAsset を指定してください。"); }
            if (!float.IsFinite(Volume) || Volume < 0f || Volume > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。");
            }

            return Rules.Validate();
        }
    }
}
