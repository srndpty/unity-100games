using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Frogger Settings")]
    public sealed class FroggerSettings : ScriptableObject
    {
        public FroggerConfig Game = new();
        [Range(0, 1)] public float Volume = .12f;
        public void Validate()
        {
            Game.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new System.ArgumentOutOfRangeException(nameof(Volume)); }
        }
    }
}
