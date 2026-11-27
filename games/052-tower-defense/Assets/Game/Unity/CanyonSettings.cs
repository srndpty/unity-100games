using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    public sealed class CanyonSettings : ScriptableObject
    {
        public Rules Rules = new();
    }
}
