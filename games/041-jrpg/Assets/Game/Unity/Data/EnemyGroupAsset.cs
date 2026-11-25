#nullable enable
using Game.Core;
using UnityEngine;

namespace Game.Unity.Data
{
    public sealed class EnemyGroupAsset : ScriptableObject
    {
        public EnemyGroupDef Def = new();
    }
}
