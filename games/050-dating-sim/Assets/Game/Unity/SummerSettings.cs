#nullable enable
using System;
using Game.App;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>遊んで決める値（文字送りの速さ・案内の長さ・音量）。ルールと台本は Assets/Game/Data にある。</summary>
    [CreateAssetMenu(menuName = "Meteor Summer/Settings", fileName = "SummerSettings")]
    public sealed class SummerSettings : ScriptableObject
    {
        [Tooltip("文字送りの 1 文字あたりの Tick（60Hz）。0 で一度に全文")]
        public int TicksPerChar = 2;

        [Tooltip("能力値の増減などの案内を出しておく Tick")]
        public int NoticeTicks = 150;

        [Tooltip("効果音の音量（0〜1、控えめが既定）")]
        public float Volume = 0.05f;

        public void Validate()
        {
            var errors = ToFlowSettings().Validate();
            if (errors.Count > 0) { throw new InvalidOperationException(string.Join("\n", errors)); }
            if (!float.IsFinite(Volume) || Volume < 0f || Volume > 1f) { throw new InvalidOperationException($"Volume が不正です: {Volume}"); }
        }

        public FlowSettings ToFlowSettings() => new()
        {
            TicksPerChar = TicksPerChar,
            NoticeTicks = NoticeTicks,
        };
    }
}
