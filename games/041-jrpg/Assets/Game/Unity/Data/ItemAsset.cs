#nullable enable
using Game.Core;
using UnityEngine;

namespace Game.Unity.Data
{
    public sealed class ItemAsset : ScriptableObject
    {
        public ItemDef Def = new();
    }
}
