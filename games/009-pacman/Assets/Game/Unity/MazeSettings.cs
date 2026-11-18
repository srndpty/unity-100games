using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Maze Settings")]
    public sealed class MazeSettings : ScriptableObject
    {
        public MazeGameConfig Game = new();
        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            Game.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new System.ArgumentOutOfRangeException(nameof(Volume)); }
        }
    }
}
