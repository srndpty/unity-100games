#nullable enable
using System;
using Game.App;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>遊んで決める値（試合の長さ・選手と球の速さ・壁・相手の CPU の強さ・切替・音量）。ほかの値は <see cref="SoccerConfig"/> の既定値を使う。</summary>
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

        [Tooltip("壁で跳ね返るときに残す速さ（%）")]
        public int WallRestitutionPercent = 70;

        [Tooltip("相手の CPU の走る速さ（最高速の %）。小さいほど弱い")]
        public int CpuSpeedPercent = 75;

        [Tooltip("相手の CPU が役割を決め直す間隔（Tick）。大きいほど弱い")]
        public int CpuRoleTicks = 30;

        [Tooltip("相手の CPU の保持者が判断する間隔（Tick）")]
        public int CpuDecisionTicks = 8;

        [Tooltip("相手の CPU がタックルを始める距離を縮める量（単位）。大きいほど慎重")]
        public int CpuTackleMargin = 300;

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
            WallRestitutionPercent = WallRestitutionPercent,
        };

        public FlowSettings ToFlowSettings() => new()
        {
            AutoSwitchOnDefense = AutoSwitchOnDefense,
            Cpu = new AiSkill { SpeedPercent = CpuSpeedPercent, RoleTicks = CpuRoleTicks, DecisionTicks = CpuDecisionTicks, TackleMargin = CpuTackleMargin },
        };

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
