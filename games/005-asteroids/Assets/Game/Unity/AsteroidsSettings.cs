using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Asteroids Settings")]
    public sealed class AsteroidsSettings : ScriptableObject
    {
        public AsteroidsConfig Game = new();
        [Range(0f, 1f)] public float Volume = .15f;
        public void Validate()
        {
            Game.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new System.ArgumentException("音量は0〜1の有限値で指定してください。"); }
        }
    }
}
