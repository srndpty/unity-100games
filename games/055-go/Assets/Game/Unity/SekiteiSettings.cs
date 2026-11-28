#nullable enable
using System;
using Game.App;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>遊んで決める値（コミ・CPU の強さ・考える時間・死石の推定・音量）。ほかの値は Core と App の既定値を使う。</summary>
    [CreateAssetMenu(menuName = "Sekitei/Settings", fileName = "SekiteiSettings")]
    public sealed class SekiteiSettings : ScriptableObject
    {
        [Tooltip("白に足すコミ（目、整数）")]
        public int Komi = 7;

        [Tooltip("この手数（パスを含む）で終局する")]
        public int MoveLimit = 300;

        [Tooltip("CPU「弱い」のプレイアウトの数")]
        public int WeakPlayouts = 500;

        [Tooltip("CPU「普通」のプレイアウトの数")]
        public int NormalPlayouts = 2000;

        [Tooltip("CPU「強い」のプレイアウトの数")]
        public int StrongPlayouts = 8000;

        [Tooltip("CPU が手番を受けてから打つまでの最小の Tick（60 Tick/秒）")]
        public int CpuThinkTicks = 30;

        [Tooltip("撮影（とテスト）で 1 Tick に回すプレイアウトの数。ふつうの対局では Tick では回さず、フレームの予算で回す")]
        public int PlayoutsPerTick = 20;

        [Tooltip("1 フレームの処理（入力・Tick・CPU の探索・死石の推定）の予算（ミリ秒）。探索はこの残りで回す")]
        public float ThinkMillisecondsPerFrame = 8f;

        [Tooltip("CPU が投了する勝率（‰）。0 なら投了しない")]
        public int ResignPermille = 100;

        [Tooltip("死石の推定に回すプレイアウトの数")]
        public int OwnershipPlayouts = 600;

        [Tooltip("効果音の音量（0〜1、控えめが既定）")]
        public float Volume = 0.05f;

        public GoConfig ToConfig() => new() { Komi = Komi, MoveLimit = MoveLimit };

        public MctsConfig ToMcts() => new()
        {
            WeakPlayouts = WeakPlayouts,
            NormalPlayouts = NormalPlayouts,
            StrongPlayouts = StrongPlayouts,
            ResignPermille = ResignPermille,
            OwnershipPlayouts = OwnershipPlayouts,
        };

        public FlowSettings ToFlowSettings() => new() { CpuThinkTicks = CpuThinkTicks, PlayoutsPerTick = PlayoutsPerTick };

        public void Validate()
        {
            var errors = new System.Collections.Generic.List<string>();
            errors.AddRange(ToConfig().Validate());
            errors.AddRange(ToMcts().Validate());
            errors.AddRange(ToFlowSettings().Validate());
            if (!float.IsFinite(ThinkMillisecondsPerFrame) || ThinkMillisecondsPerFrame < 1f || ThinkMillisecondsPerFrame > 30f) { errors.Add($"ThinkMillisecondsPerFrame は 1〜30（{ThinkMillisecondsPerFrame}）"); }
            if (!float.IsFinite(Volume) || Volume < 0f || Volume > 1f) { errors.Add($"Volume は 0〜1（{Volume}）"); }
            if (errors.Count > 0) { throw new InvalidOperationException(string.Join("\n", errors)); }
        }
    }
}
