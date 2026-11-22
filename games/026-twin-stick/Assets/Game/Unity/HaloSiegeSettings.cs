using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Halo Siege Settings")]
    public sealed class HaloSiegeSettings : ScriptableObject
    {
        /// <summary>
        /// 試合の設定（アリーナ・自機・弾・敵・出現の表・密度の上限・無敵）。シーン生成スクリプトはこのアセットを上書きしないので、
        /// コードの既定値を変えたらアセットも直す（024 の知見）。
        /// </summary>
        public SiegeConfig Rules = new();

        [Range(0, 1)] public float Volume = .12f;

        public void Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            Rules.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。"); }
        }
    }
}
