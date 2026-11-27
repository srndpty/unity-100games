#nullable enable
using System;
using Game.App;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>遊んで決める値（試合の長さ・選手と球の速さ・GK・切替・音量）。ほかの値は <see cref="SoccerConfig"/> の既定値を使う。</summary>
    [CreateAssetMenu(menuName = "Soccer/Settings", fileName = "SoccerSettings")]
    public sealed class SoccerSettings : ScriptableObject
    {
        [Tooltip("試合時間（分）")]
        public int MatchMinutes = 5;

        [Tooltip("選手の最高速（単位/Tick。1 m = 1000、60 Tick/秒）")]
        public int PlayerMaxSpeed = 100;

        [Tooltip("止められる球の相対速度の上限")]
        public int TrapSpeed = 420;

        [Tooltip("シュートの速さ")]
        public int ShotSpeed = 430;

        [Tooltip("パスが受け手の位置で残す速さ")]
        public int PassArriveSpeed = 70;

        [Tooltip("GK の速さ")]
        public int KeeperSpeed = 70;

        [Tooltip("GK がシュートへ動き出すまでの Tick")]
        public int KeeperReactionTicks = 12;

        [Tooltip("守備で相手が球を取ったら、球に近い方へ自動で切り替える")]
        public bool AutoSwitchOnDefense = true;

        [Tooltip("効果音の音量（0〜1、控えめが既定）")]
        public float Volume = 0.05f;

        public SoccerConfig ToConfig() => new()
        {
            MatchTicks = MatchMinutes * 60 * Pitch.TickRate,
            PlayerMaxSpeed = PlayerMaxSpeed,
            TrapSpeed = TrapSpeed,
            ShotSpeed = ShotSpeed,
            PassArriveSpeed = PassArriveSpeed,
            KeeperSpeed = KeeperSpeed,
            KeeperReactionTicks = KeeperReactionTicks,
        };

        public FlowSettings ToFlowSettings() => new() { AutoSwitchOnDefense = AutoSwitchOnDefense };

        public void Validate()
        {
            var errors = ToConfig().Validate();
            if (errors.Count > 0) { throw new InvalidOperationException(string.Join("\n", errors)); }
            var flow = ToFlowSettings().Validate();
            if (flow.Count > 0) { throw new InvalidOperationException(string.Join("\n", flow)); }
            if (!float.IsFinite(Volume) || Volume < 0f || Volume > 1f) { throw new InvalidOperationException($"Volume が不正です: {Volume}"); }
        }
    }
}
