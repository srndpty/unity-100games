using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Lantern Veil Settings")]
    public sealed class LanternVeilSettings : ScriptableObject
    {
        /// <summary>
        /// 試合の設定（自機・ショット・ボム・無敵・弾の容量・得点）。シーン生成スクリプトはこのアセットを上書きしないので、
        /// コードの既定値を変えたらアセットも直す（024 の知見）。
        /// </summary>
        public VeilConfig Rules = new();

        /// <summary>ボス 1 体目・2 体目の台本（弾道 DSL のテキスト）。差し替えれば弾幕が変わる。</summary>
        public TextAsset Boss1;

        public TextAsset Boss2;

        [Range(0, 1)] public float Volume = .12f;

        /// <summary>設定と台本を確かめ、読んだ台本を返す。誤りがあれば例外（台本の誤りは行・列付き）。</summary>
        public BossScript[] Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            Rules.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。"); }
            if (Boss1 == null || Boss2 == null) { throw new InvalidOperationException("ボスの台本（TextAsset）を 2 つ指定してください。"); }

            var bosses = new[]
            {
                ScriptParser.ParseOrThrow(Boss1.text, Boss1.name),
                ScriptParser.ParseOrThrow(Boss2.text, Boss2.name),
            };
            VeilConfig.ValidateBosses(bosses);
            return bosses;
        }
    }
}
