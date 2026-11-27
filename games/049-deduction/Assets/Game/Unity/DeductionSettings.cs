using UnityEngine;

namespace Game.Unity
{
    public sealed class DeductionSettings : ScriptableObject
    {
        [Min(1)] public int Attempts = 3;
        [Range(0f, 1f)] public float Volume = 0.25f;
    }
}
