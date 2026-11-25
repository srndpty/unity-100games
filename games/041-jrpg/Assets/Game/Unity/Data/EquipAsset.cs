#nullable enable
using Game.Core;
using UnityEngine;

namespace Game.Unity.Data
{
    public sealed class EquipAsset : ScriptableObject
    {
        public EquipDef Def = new();
    }
}
