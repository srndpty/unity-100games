#nullable enable
using Game.Core;
using UnityEngine;

namespace Game.Unity.Data
{
    public sealed class EncounterAsset : ScriptableObject
    {
        public EncounterTableDef Def = new();
    }
}
