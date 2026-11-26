#nullable enable
using System;
using Game.App;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>遊んで決める値（演出の速さ・音量）。ルールの値は Assets/Game/Data のデータにある。</summary>
    [CreateAssetMenu(menuName = "Thornwild/Settings", fileName = "KeeperSettings")]
    public sealed class KeeperSettings : ScriptableObject
    {
        [Tooltip("文のある戦闘の出来事を見せる Tick（60Hz）。決定キーで送れる")]
        public int TextTicks = 54;

        [Tooltip("HP だけが動く出来事の Tick")]
        public int QuietTicks = 16;

        [Tooltip("文が出てから決定キーで送れるようになるまでの Tick")]
        public int MinTicks = 6;

        [Tooltip("効果音の音量（0〜1、控えめが既定）")]
        public float Volume = 0.06f;

        public void Validate()
        {
            if (TextTicks < 1 || TextTicks > 600) { throw new InvalidOperationException($"TextTicks が範囲外です: {TextTicks}"); }
            if (QuietTicks < 1 || QuietTicks > 600) { throw new InvalidOperationException($"QuietTicks が範囲外です: {QuietTicks}"); }
            if (MinTicks < 0 || MinTicks > TextTicks) { throw new InvalidOperationException($"MinTicks は 0〜TextTicks です: {MinTicks}"); }
            if (!float.IsFinite(Volume) || Volume < 0f || Volume > 1f) { throw new InvalidOperationException($"Volume が不正です: {Volume}"); }
        }

        public FlowSettings ToFlowSettings() => new()
        {
            TextTicks = TextTicks,
            QuietTicks = QuietTicks,
            MinTicks = MinTicks,
        };
    }
}
