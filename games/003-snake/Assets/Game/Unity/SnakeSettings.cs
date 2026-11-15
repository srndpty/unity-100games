using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Snake Settings")]
    public sealed class SnakeSettings : ScriptableObject
    {
        public SnakeConfig Game = new();
        [Range(0f, 1f)] public float Volume = 0.15f;

        public void Validate()
        {
            Game.Validate();
            if (!float.IsFinite(Volume) || Volume < 0f || Volume > 1f)
            {
                throw new System.ArgumentException("音量は0〜1の有限値で指定してください。");
            }
        }
    }
}
