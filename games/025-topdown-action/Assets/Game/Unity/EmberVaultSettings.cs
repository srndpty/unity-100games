using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Ember Vault Settings")]
    public sealed class EmberVaultSettings : ScriptableObject
    {
        /// <summary>
        /// 試合の設定（速さ・剣・ロール・無敵・敵とボス・切り替え）。シーン生成スクリプトはこのアセットを上書きしないので、
        /// コードの既定値を変えたらアセットも直す（024 の知見）。
        /// </summary>
        public VaultConfig Rules = new();

        [Range(0, 1)] public float Volume = .12f;

        /// <summary>鍵を取った・扉を開けたなどの案内を出しておく Tick 数。</summary>
        [Min(1)] public int ToastTicks = 150;

        public void Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            Rules.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。"); }
            if (ToastTicks < 1 || ToastTicks > 600) { throw new ArgumentOutOfRangeException(nameof(ToastTicks), ToastTicks, "1〜600 で指定してください。"); }
        }
    }
}
