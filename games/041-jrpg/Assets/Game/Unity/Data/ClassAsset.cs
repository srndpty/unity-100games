#nullable enable
using Game.Core;
using UnityEngine;

namespace Game.Unity.Data
{
    public sealed class ClassAsset : ScriptableObject
    {
        public ClassDef Def = new();
    }
}
