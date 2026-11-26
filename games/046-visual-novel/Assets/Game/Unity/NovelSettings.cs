#nullable enable
using System;
using Game.App;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>遊んで決める値（文字送りの速さ・スキップの間隔・音量）。ルールとシナリオは Assets/Game/Data にある。</summary>
    [CreateAssetMenu(menuName = "Lantern Wake/Settings", fileName = "NovelSettings")]
    public sealed class NovelSettings : ScriptableObject
    {
        [Tooltip("文字送りの 1 文字あたりの Tick（60Hz）。0 で一度に全文")]
        public int TicksPerChar = 2;

        [Tooltip("スキップで既読の文を 1 つ送る間隔（Tick）")]
        public int SkipIntervalTicks = 4;

        [Tooltip("「保存しました」などの案内を出しておく Tick")]
        public int NoticeTicks = 150;

        [Tooltip("1 文の上限字数（テキスト枠に収まる数。シナリオの検証に使う）")]
        public int MaxTextLength = 80;

        [Tooltip("効果音の音量（0〜1、控えめが既定）")]
        public float Volume = 0.05f;

        public void Validate()
        {
            var errors = ToFlowSettings().Validate();
            if (errors.Count > 0) { throw new InvalidOperationException(string.Join("\n", errors)); }
            if (MaxTextLength < 20 || MaxTextLength > 90) { throw new InvalidOperationException($"MaxTextLength は 20〜90 です（テキスト枠に 3 行で収まる数）: {MaxTextLength}"); }
            if (!float.IsFinite(Volume) || Volume < 0f || Volume > 1f) { throw new InvalidOperationException($"Volume が不正です: {Volume}"); }
        }

        public FlowSettings ToFlowSettings() => new()
        {
            TicksPerChar = TicksPerChar,
            SkipIntervalTicks = SkipIntervalTicks,
            NoticeTicks = NoticeTicks,
        };
    }
}
