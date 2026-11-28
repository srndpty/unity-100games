#nullable enable
using System;
using System.Collections.Generic;
using Game.App;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 盤・81 の石・81 の地の印・最後の手の印・カーソル・置く前の影の Stage（Prefab）。画面の流れの状態を写すだけで、自分では状態を持たない。
    /// 石と印は交点の番号順に並べ、位置は <see cref="BoardProjection.World"/> と <see cref="Mismatch"/> で照合する。
    /// </summary>
    public sealed class SekiteiStage : MonoBehaviour
    {
        public const int BoardOrder = 0;
        public const int MarkOrder = 5;
        public const int StoneOrder = 10;
        public const int LastOrder = 20;
        public const int CursorOrder = 25;

        public static readonly Color BlackMark = new(0.08f, 0.08f, 0.10f, 0.85f);
        public static readonly Color WhiteMark = new(0.97f, 0.96f, 0.92f, 0.95f);

        [SerializeField] private SpriteRenderer? _board;
        [SerializeField] private SpriteRenderer[] _stones = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer[] _marks = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer? _last;
        [SerializeField] private SpriteRenderer? _cursor;
        [SerializeField] private SpriteRenderer? _ghost;
        [SerializeField] private Sprite? _blackStone;
        [SerializeField] private Sprite? _whiteStone;

        private readonly Stone[] _owners = new Stone[Points.Count];

        public SpriteRenderer Board => _board!;

        public IReadOnlyList<SpriteRenderer> Stones => _stones;

        public IReadOnlyList<SpriteRenderer> Marks => _marks;

        public SpriteRenderer Last => _last!;

        public SpriteRenderer Cursor => _cursor!;

        public SpriteRenderer Ghost => _ghost!;

        /// <summary>
        /// Flow の状態を写す。<paramref name="hover"/> はマウスの下の交点（なければ -1）で、プレイヤーが打てる点なら置く前の影を出す（撮影では -1）。
        /// </summary>
        public void Show(GoFlow flow, int hover)
        {
            if (flow == null) { throw new ArgumentNullException(nameof(flow)); }
            var game = flow.Game;
            var scoring = flow.Screen == FlowScreen.Scoring || (flow.Screen == FlowScreen.Result && flow.FinalScore != null);
            var dead = flow.DeadStones;
            if (scoring) { AreaScore.Owners(CellsWithoutDead(game!, dead), _owners); }

            for (var p = 0; p < Points.Count; p++)
            {
                var color = game?[p] ?? Stone.Empty;
                var stone = _stones[p];
                stone.enabled = color != Stone.Empty;
                if (stone.enabled)
                {
                    stone.sprite = color == Stone.Black ? _blackStone : _whiteStone;
                    stone.color = scoring && dead.Contains(p) ? new Color(1f, 1f, 1f, 0.4f) : Color.white;
                }

                var mark = _marks[p];
                var owner = scoring ? _owners[p] : Stone.Empty;
                mark.enabled = owner != Stone.Empty && (color == Stone.Empty || dead.Contains(p));
                if (mark.enabled) { mark.color = owner == Stone.Black ? BlackMark : WhiteMark; }
            }

            // 置いた石は演出の間だけ少し大きく見せる。
            var last = game?.LastMove;
            for (var p = 0; p < Points.Count; p++) { _stones[p].transform.localScale = Vector3.one; }
            var hasLast = last.HasValue && last.Value != Points.Pass && game![last.Value] != Stone.Empty;
            _last!.enabled = hasLast && !scoring;
            if (hasLast)
            {
                _last.transform.localPosition = BoardProjection.World(last!.Value);
                _last.color = game![last.Value] == Stone.Black ? WhiteMark : BlackMark;
                if (flow.BusyTicks > 0) { _stones[last.Value].transform.localScale = Vector3.one * 1.12f; }
            }

            var inBoard = game != null && flow.Screen is FlowScreen.Playing or FlowScreen.Scoring;
            _cursor!.enabled = inBoard && flow.Cursor.Visible;
            _cursor.transform.localPosition = BoardProjection.World(flow.Cursor.Point);

            var showGhost = hover >= 0 && flow.IsPlayerTurn && game!.Check(flow.MatchPlayerSide, hover) == MoveVerdict.Ok;
            _ghost!.enabled = showGhost;
            if (showGhost)
            {
                _ghost.sprite = flow.MatchPlayerSide == Stone.Black ? _blackStone : _whiteStone;
                _ghost.transform.localPosition = BoardProjection.World(hover);
            }
        }

        /// <summary>Prefab が期待する構成と合わなければ理由を返す。</summary>
        public string? Mismatch()
        {
            if (_board == null) { return "盤の SpriteRenderer がない"; }
            if (_blackStone == null || _whiteStone == null) { return "石のスプライトがない"; }
            if (_stones.Length != Points.Count || _marks.Length != Points.Count) { return $"石が {_stones.Length}・印が {_marks.Length}（{Points.Count}）"; }
            if (_last == null || _cursor == null || _ghost == null) { return "最後の手の印・カーソル・影のどれかがない"; }
            for (var p = 0; p < Points.Count; p++)
            {
                if (_stones[p] == null || _marks[p] == null) { return $"{Points.Name(p)} の部品がない"; }
                if ((_stones[p].transform.localPosition - BoardProjection.World(p)).sqrMagnitude > 1e-6f) { return $"{Points.Name(p)} の石の位置が {_stones[p].transform.localPosition}（{BoardProjection.World(p)}）"; }
                if ((_marks[p].transform.localPosition - BoardProjection.World(p)).sqrMagnitude > 1e-6f) { return $"{Points.Name(p)} の印の位置が違う"; }
                if (_stones[p].sortingOrder != StoneOrder || _marks[p].sortingOrder != MarkOrder) { return $"{Points.Name(p)} の描画順が違う"; }
            }

            if (_board.sortingOrder != BoardOrder) { return "盤の描画順が違う"; }
            if (_board.transform.localPosition.sqrMagnitude > 1e-6f) { return "盤が原点にない"; }
            if (_last.sortingOrder != LastOrder || _cursor.sortingOrder != CursorOrder || _ghost.sortingOrder != StoneOrder) { return "印の描画順が違う"; }
            return null;
        }

        private static Stone[] CellsWithoutDead(GoGame game, PointSet dead)
        {
            var cells = new Stone[Points.Count];
            for (var p = 0; p < Points.Count; p++) { cells[p] = dead.Contains(p) ? Stone.Empty : game[p]; }
            return cells;
        }
    }
}
