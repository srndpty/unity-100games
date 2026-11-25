#nullable enable
using Game.Core;
using UnityEngine;

namespace Game.Unity.Data
{
    public sealed class SpellAsset : ScriptableObject
    {
        public SpellDef Def = new();
    }
}
