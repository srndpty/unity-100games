#nullable enable
using System;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Emberlock/Settings", fileName = "EmberlockSettings")]
    public sealed class EmberlockSettings : ScriptableObject
    {
        [Tooltip("1 マス移動にかける Tick 数（60Hz 基準で 8 = 約 0.13 秒）")]
        public int StepTicks = 8;

        [Tooltip("戦闘メッセージ 1 件あたりの表示 Tick 数")]
        public int EventDurationTicks = 18;

        [Tooltip("エンカウント率に掛ける全体倍率（1.0 が標準）")]
        public float EncounterRateMultiplier = 1f;

        [Tooltip("効果音の音量（0.0〜1.0、控えめな 0.05 を既定）")]
        public float Volume = 0.05f;

        public void Validate()
        {
            if (StepTicks < 1 || StepTicks > 60)
            {
                throw new InvalidOperationException($"StepTicks が範囲外です: {StepTicks}");
            }

            if (EventDurationTicks < 1 || EventDurationTicks > 180)
            {
                throw new InvalidOperationException($"EventDurationTicks が範囲外です: {EventDurationTicks}");
            }

            if (!float.IsFinite(EncounterRateMultiplier) || EncounterRateMultiplier <= 0f || EncounterRateMultiplier > 5f)
            {
                throw new InvalidOperationException($"EncounterRateMultiplier が不正です: {EncounterRateMultiplier}");
            }

            if (!float.IsFinite(Volume) || Volume < 0f || Volume > 1f)
            {
                throw new InvalidOperationException($"Volume が不正です: {Volume}");
            }
        }
    }
}
