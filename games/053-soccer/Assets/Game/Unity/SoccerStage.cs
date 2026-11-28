#nullable enable
using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using IntGeometry;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// コート・選手 6 人（体と向きの印）・球・影・操作中の輪・役割の目標の印の Stage（Prefab）。画面の流れの状態を写すだけで、自分では状態を持たない。
    /// 選手は試合の番号順に並べ、Prefab の数・描画順は <see cref="Mismatch"/> で照合する。
    /// </summary>
    public sealed class SoccerStage : MonoBehaviour
    {
        public static readonly Color Blue = new(0.25f, 0.55f, 1f, 1f);
        public static readonly Color Red = new(1f, 0.32f, 0.28f, 1f);

        /// <summary>タイトル・操作説明で選手を並べる位置（試合の外の飾り）。</summary>
        private static readonly Vec2i[] _idle =
        {
            new(-4000, -4000), new(-9000, 0), new(-4000, 4000),
            new(4000, -4000), new(9000, 0), new(4000, 4000),
        };

        [SerializeField] private SpriteRenderer? _pitch;
        [SerializeField] private SpriteRenderer[] _players = Array.Empty<SpriteRenderer>();
        [SerializeField] private Transform[] _facings = Array.Empty<Transform>();
        [SerializeField] private SpriteRenderer? _ball;
        [SerializeField] private SpriteRenderer? _shadow;
        [SerializeField] private SpriteRenderer? _marker;
        [SerializeField] private SpriteRenderer[] _targets = Array.Empty<SpriteRenderer>();

        public SpriteRenderer Pitch => _pitch!;

        public IReadOnlyList<SpriteRenderer> Players => _players;

        public SpriteRenderer Ball => _ball!;

        public SpriteRenderer Shadow => _shadow!;

        public SpriteRenderer Marker => _marker!;

        public IReadOnlyList<SpriteRenderer> Targets => _targets;

        /// <summary>役割の目標の印を出すか（F1 で切り替え・撮影で使う）。</summary>
        public bool ShowRoles { get; set; }

        public static Color ColorOf(int player) => Match.TeamOf(player) == Team.Blue ? Blue : Red;

        public void Show(SoccerFlow flow)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var m = flow.Match;
            for (int i = 0; i < _players.Length; i++)
            {
                var pos = m != null ? m.Players[i].Position : _idle[i];
                var facing = m != null ? m.Players[i].Facing : new Vec2i(Game.Core.Pitch.Direction(Match.TeamOf(i)) * 1000, 0);
                _players[i].transform.localPosition = PitchProjection.Ground(pos);
                _players[i].color = ColorOf(i);
                _facings[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(facing.Y, facing.X) * Mathf.Rad2Deg);
            }

            var ballPos = m?.Ball.Position ?? Vec2i.Zero;
            var height = m?.Ball.Height ?? 0;
            _ball!.transform.localPosition = PitchProjection.Lifted(ballPos, height);
            _ball.transform.localScale = Vector3.one * PitchProjection.BallScale(height);
            _shadow!.transform.localPosition = PitchProjection.Ground(ballPos);
            _shadow.transform.localScale = Vector3.one * PitchProjection.BallBase;
            _shadow.enabled = height > 0;

            var playing = m != null && flow.Screen is FlowScreen.Playing or FlowScreen.Paused;
            _marker!.enabled = playing;
            if (m != null) { _marker.transform.localPosition = PitchProjection.Ground(m.Players[flow.Controlled].Position); }

            for (int i = 0; i < _targets.Length; i++)
            {
                var ai = Match.TeamOf(i) == Team.Blue ? flow.BlueAi : flow.RedAi;
                var show = ShowRoles && playing && ai != null && ai.RoleOf(i) is not (Role.None or Role.Carrier);
                _targets[i].enabled = show;
                if (!show) { continue; }
                _targets[i].transform.localPosition = PitchProjection.Ground(ai!.TargetOf(i));
                _targets[i].color = ColorOf(i);
            }
        }

        /// <summary>Prefab が期待する構成と合わなければ理由を返す。</summary>
        public string? Mismatch()
        {
            if (_pitch == null) { return "コートの SpriteRenderer がない"; }
            if (_players.Length != Match.PlayerCount || _facings.Length != Match.PlayerCount) { return $"選手が {_players.Length}/{_facings.Length}（{Match.PlayerCount}）"; }
            if (_targets.Length != Match.PlayerCount) { return $"目標の印が {_targets.Length}（{Match.PlayerCount}）"; }
            if (_ball == null || _shadow == null || _marker == null) { return "球・影・操作中の輪のどれかがない"; }
            for (int i = 0; i < _players.Length; i++)
            {
                if (_players[i] == null || _facings[i] == null || _targets[i] == null) { return $"選手 {i} の部品がない"; }
                if (_players[i].sortingOrder <= _pitch.sortingOrder) { return $"選手 {i} がコートより奥に描かれる"; }
                if (_facings[i].parent != _players[i].transform) { return $"選手 {i} の向きの印が選手の子でない"; }
                if (!Mathf.Approximately(_players[i].transform.localScale.x, PitchProjection.PlayerScale)) { return $"選手 {i} の大きさが {_players[i].transform.localScale.x}（{PitchProjection.PlayerScale}）"; }
            }

            if (_shadow.sortingOrder <= _pitch.sortingOrder || _ball.sortingOrder <= _shadow.sortingOrder) { return "球・影の描画順が違う"; }
            if (_ball.sortingOrder <= _players[0].sortingOrder) { return "球が選手より奥に描かれる"; }
            if (_marker.sortingOrder >= _players[0].sortingOrder) { return "操作中の輪が選手より手前に描かれる"; }
            if (!Mathf.Approximately(_marker.transform.localScale.x, PitchProjection.PlayerScale)) { return "操作中の輪の大きさが選手と違う"; }
            return null;
        }
    }
}
