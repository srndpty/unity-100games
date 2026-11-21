using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Glint Ascent Settings")]
    public sealed class GlintAscentSettings : ScriptableObject
    {
        /// <summary>
        /// 操作の設定（速さ・猶予・バッファの Tick 数など）。既定値を変えると撮影とテストの入力の列（App の Routes）が
        /// 合わなくなるので、App テストの RouteFinder で作り直す。
        /// </summary>
        public PrecisionConfig Rules = new();

        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            Rules.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。");
            }
        }
    }
}
