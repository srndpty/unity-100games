using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Ink Vigil Settings")]
    public sealed class InkVigilSettings : ScriptableObject
    {
        /// <summary>
        /// 試合の設定（体力・列・難度ごとの段・同時数・手前までの Tick・規定数・出現の間隔）。
        /// シーン生成スクリプトはこのアセットを上書きしないので、コードの既定値を変えたらアセットも直す（024 の知見）。
        /// </summary>
        public VigilConfig Rules = new();

        /// <summary>単語帳（表示文 TAB 読み TAB 段）。</summary>
        public TextAsset Words;

        [Range(0, 1)] public float Volume = .12f;

        /// <summary>誤字・被弾・撃破の演出を見せる Tick 数。</summary>
        [Min(1)] public int EffectTicks = 24;

        /// <summary>設定と単語帳を検証し、単語帳を返す。</summary>
        public WordBook Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            if (Words == null) { throw new ArgumentNullException(nameof(Words)); }
            var book = WordBook.Production(Words.text);
            Rules.Validate(book);
            if (Rules.Lanes > InkVigilStage.MaxLanes) { throw new ArgumentOutOfRangeException(nameof(Rules), Rules.Lanes, $"列は描画できる {InkVigilStage.MaxLanes} までにしてください。"); }
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。"); }
            if (EffectTicks < 1 || EffectTicks > 120) { throw new ArgumentOutOfRangeException(nameof(EffectTicks), EffectTicks, "1〜120 で指定してください。"); }
            return book;
        }
    }
}
