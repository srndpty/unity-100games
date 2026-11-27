#nullable enable
using System;
using Game.App;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>遊んで決める値（カメラの演出・知らせの時間・ドラッグの重さ・音量）。ルールと部屋は Core の StudyRoom にある。</summary>
    [CreateAssetMenu(menuName = "Stargazer/Settings", fileName = "EscapeSettings")]
    public sealed class EscapeSettings : ScriptableObject
    {
        [Tooltip("視点を移すカメラの演出の Tick（60Hz）")]
        public int TransitionTicks = 18;

        [Tooltip("短い知らせ（拾った・合わない）を出しておく Tick")]
        public int MessageTicks = 150;

        [Tooltip("ヒントの文を出しておく Tick")]
        public int HintTicks = 480;

        [Tooltip("脱出してから結果画面までの Tick")]
        public int ResultDelayTicks = 90;

        [Tooltip("手に取った物を 1 段回すドラッグの長さ（画面の縦の半分 = 1000）")]
        public int DragStepUnits = 180;

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
            TransitionTicks = TransitionTicks,
            MessageTicks = MessageTicks,
            HintTicks = HintTicks,
            ResultDelayTicks = ResultDelayTicks,
            DragStepUnits = DragStepUnits,
        };
    }
}
