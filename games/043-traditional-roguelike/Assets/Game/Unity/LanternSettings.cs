using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 遊んで決める値（規則・音量・連続移動の間合い・映す範囲）。シーン生成スクリプトはこのアセットを上書きしないので、
    /// コードの既定値を変えたらアセットも直す（024 の知見）。PlayMode のテストで検証する。
    /// </summary>
    [CreateAssetMenu(menuName = "Game/Mire Lantern Settings")]
    public sealed class LanternSettings : ScriptableObject
    {
        public CrawlConfig Rules = new();

        [Range(0, 1)] public float Volume = .1f;

        /// <summary>方向キーを押したままにしてから歩き続けるまでの秒数と、歩き続ける間隔（秒）。</summary>
        [Range(.05f, 1f)] public float RepeatDelay = .22f;

        [Range(.02f, .5f)] public float RepeatInterval = .07f;

        /// <summary>試合中に上の帯と下のメッセージ欄の間に映す行の数（自機を中心に周りを映す）。横は画面の比率で決まる。</summary>
        [Range(MinVisibleRows, MaxVisibleRows)] public int VisibleRows = 13;

        public const int MinVisibleRows = 7;
        public const int MaxVisibleRows = 27;

        public void Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            Rules.Validate();
            if (Rules.Width != 48 || Rules.Height != 27) { throw new ArgumentOutOfRangeException(nameof(Rules), "画面の配置は 48×27 の盤専用です（010 の知見）。"); }
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1) { throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。"); }
            if (!float.IsFinite(RepeatDelay) || RepeatDelay < .05f || RepeatDelay > 1) { throw new ArgumentOutOfRangeException(nameof(RepeatDelay)); }
            if (!float.IsFinite(RepeatInterval) || RepeatInterval < .02f || RepeatInterval > .5f) { throw new ArgumentOutOfRangeException(nameof(RepeatInterval)); }
            if (VisibleRows < MinVisibleRows || VisibleRows > MaxVisibleRows) { throw new ArgumentOutOfRangeException(nameof(VisibleRows), VisibleRows, $"{MinVisibleRows}〜{MaxVisibleRows} 行で指定してください。"); }
        }
    }
}
