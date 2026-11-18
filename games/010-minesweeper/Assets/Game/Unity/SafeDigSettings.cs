using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Safe Dig Settings")]
    public sealed class SafeDigSettings : ScriptableObject
    {
        public BoardConfig Board = new();
        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            Board.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new System.ArgumentOutOfRangeException(nameof(Volume)); }
        }
    }
}
