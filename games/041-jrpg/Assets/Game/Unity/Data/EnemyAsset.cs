#nullable enable
using Game.Core;
using UnityEngine;

namespace Game.Unity.Data
{
    public sealed class EnemyAsset : ScriptableObject
    {
        public EnemyDef Def = new();
        public Sprite? BattleSprite;
    }
}
