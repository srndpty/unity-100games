using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Invaders Settings")]
    public sealed class InvadersSettings : ScriptableObject
    {
        public InvadersConfig Game = new();
        [Range(0f, 1f)] public float Volume = .15f;
        public void Validate()
        {
            Game.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new System.ArgumentException("音量は0〜1の有限値で指定してください。"); }
        }
    }
}
