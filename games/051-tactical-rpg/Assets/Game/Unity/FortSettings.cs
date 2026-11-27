using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    public sealed class FortSettings : ScriptableObject
    {
        public Stats[] Units = Battle.Defaults();
        [Min(0.05f)] public float EnemyInterval = 0.35f;
    }
}
