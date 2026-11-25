#nullable enable
using Game.Core;
using UnityEngine;

namespace Game.Unity.Data
{
    public sealed class ShopAsset : ScriptableObject
    {
        public ShopDef Def = new();
    }
}
