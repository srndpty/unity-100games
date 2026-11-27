#nullable enable
using System;
using Game.App;
using Game.Core.Play;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>遊んで決める値（歩く速さ・台詞を出しておく時間・動詞の選び方・音量）。ルールは Assets/Game/Data にある。</summary>
    [CreateAssetMenu(menuName = "Tidewatch/Settings", fileName = "AdventureSettings")]
    public sealed class AdventureSettings : ScriptableObject
    {
        [Tooltip("1 Tick（60Hz）に歩く距離（画面の px）")]
        public int WalkSpeed = 10;

        [Tooltip("台詞を出しておく Tick（基本）")]
        public int LineBaseTicks = 90;

        [Tooltip("台詞を出しておく Tick（1 文字あたりの追加）")]
        public int LineTicksPerChar = 6;

        [Tooltip("行為を命じたら動詞と持ち物の選択を外す")]
        public bool ResetVerbAfterCommand = true;

        [Tooltip("効果音の音量（0〜1、控えめが既定）")]
        public float Volume = 0.05f;

        public void Validate()
        {
            ToWorldSettings().Validate();
            var errors = ToFlowSettings().Validate();
            if (errors.Count > 0) { throw new InvalidOperationException(string.Join("\n", errors)); }
            if (!float.IsFinite(Volume) || Volume < 0f || Volume > 1f) { throw new InvalidOperationException($"Volume が不正です: {Volume}"); }
        }

        public WorldSettings ToWorldSettings() => new() { WalkSpeed = WalkSpeed };

        public FlowSettings ToFlowSettings() => new()
        {
            LineBaseTicks = LineBaseTicks,
            LineTicksPerChar = LineTicksPerChar,
            ResetVerbAfterCommand = ResetVerbAfterCommand,
        };
    }
}
