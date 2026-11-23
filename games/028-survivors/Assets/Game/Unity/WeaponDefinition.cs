using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 武器 1 種の定義アセット。レベルの表（<see cref="Data"/>）は Core の <see cref="WeaponDef"/> そのもので、名前と説明を足す。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Thorn Tide Weapon")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        public string DisplayName = "";

        /// <summary>3 択のカードに出す 1 行の説明。</summary>
        public string Summary = "";

        public WeaponDef Data = new();
    }
}
