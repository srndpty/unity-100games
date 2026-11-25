using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>遊んで決める値（飛行・ミッション・音量）。生成スクリプトは既にあるアセットを上書きしない。</summary>
    [CreateAssetMenu(menuName = "Game/Halcyon Settings")]
    public sealed class HalcyonSettings : ScriptableObject
    {
        public FlightConfig Flight = new();

        public MissionConfig Mission = new();

        [Range(0f, 1f)] public float Volume = 0.12f;

        /// <summary>検証済みの飛行とミッションの設定（複製）。</summary>
        public (FlightConfig Flight, MissionConfig Mission) Validate()
        {
            if (Flight == null) { throw new ArgumentNullException(nameof(Flight)); }
            if (Mission == null) { throw new ArgumentNullException(nameof(Mission)); }
            if (!float.IsFinite(Volume) || Volume < 0f || Volume > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(Volume), Volume, "0〜1 で指定してください。");
            }

            var flight = Flight.Validate();
            return (flight, Mission.Validate(flight));
        }
    }
}
