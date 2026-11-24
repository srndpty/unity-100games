using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Pebble Siege Settings")]
    public sealed class PebbleSiegeSettings : ScriptableObject
    {
        /// <summary>
        /// 射出・素材・弾・爆発・静止の設定。
        /// シーン生成スクリプトはこのアセットを上書きしないので、コードの既定値を変えたらアセットも直す（024 の知見）。
        /// </summary>
        public SiegeConfig Rules = new();

        /// <summary>面のデータ（<see cref="StageBook"/> の書式）。</summary>
        public TextAsset Stages;

        /// <summary>解答の手順（撮影とテストだけが使う）。</summary>
        public TextAsset Solutions;

        [Range(0, 1)] public float Volume = .12f;

        /// <summary>爆発・破壊の演出を見せる Tick 数。</summary>
        [Min(1)] public int EffectTicks = 24;

        /// <summary>設定と面のデータを検証し、面の集まりを返す。</summary>
        public StageBook Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            if (Stages == null) { throw new ArgumentNullException(nameof(Stages)); }
            var book = StageBook.Production(Stages.text, Rules);
            var capacity = PebbleSiegeStage.Capacity(book, Rules);
            if (capacity != null) { throw new ArgumentOutOfRangeException(nameof(Stages), capacity); }
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。"); }
            if (EffectTicks < 1 || EffectTicks > 120) { throw new ArgumentOutOfRangeException(nameof(EffectTicks), EffectTicks, "1〜120 で指定してください。"); }
            return book;
        }
    }
}
