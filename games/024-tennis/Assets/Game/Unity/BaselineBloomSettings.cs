using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    [CreateAssetMenu(menuName = "Game/Baseline Bloom Settings")]
    public sealed class BaselineBloomSettings : ScriptableObject
    {
        /// <summary>
        /// 試合の設定（試合の長さ・球・打点・ショット・サーブ・CPU の強さ）。ショットの値を変えると、設定の検証が
        /// 「狙いどおりの時機で打てば入る」を実際に球を飛ばして確かめる。CPU の強さは主に <see cref="TennisConfig.CpuTimingJitter"/>。
        /// </summary>
        public TennisConfig Rules = new();

        [Range(0, 1)] public float Volume = .12f;

        /// <summary>プレイヤーが打ったときに、早い・ちょうど・遅いの案内を出しておく Tick 数。</summary>
        [Min(1)] public int TimingHintTicks = 75;

        public void Validate()
        {
            if (Rules == null) { throw new ArgumentNullException(nameof(Rules)); }
            Rules.Validate();
            if (!float.IsFinite(Volume) || Volume < 0 || Volume > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。");
            }

            if (TimingHintTicks < 1 || TimingHintTicks > 600)
            {
                throw new ArgumentOutOfRangeException(nameof(TimingHintTicks), TimingHintTicks, "1〜600 で指定してください。");
            }
        }
    }
}
