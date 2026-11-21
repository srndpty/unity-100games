using System;
using Game.App;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 盤の描画。マスごとに石（Tile）・灯（Glow）・解答の印（Mark）を持つ。
    /// 灯の明るさは盤の点灯へ向けて短く寄せるだけで、盤の状態は Core が押した時点で変えている。
    /// </summary>
    public sealed class FireflyHushBoardView : MonoBehaviour
    {
        [SerializeField] private float _pitch = 1.9f;
        [SerializeField] private float _hitSize = 1.7f;
        [SerializeField] private SpriteRenderer[] _tiles;
        [SerializeField] private SpriteRenderer[] _glows;
        [SerializeField] private SpriteRenderer[] _marks;
        [SerializeField] private SpriteRenderer _cursor;
        [SerializeField] private Color _tileDark = new(.2f, .24f, .3f);
        [SerializeField] private Color _tileLit = new(.42f, .44f, .3f);
        [SerializeField] private Color _glow = new(.96f, 1f, .62f);

        private readonly float[] _levels = new float[LightsBoard.CellCount];
        private BoardLayout _layout;

        public BoardLayout Layout => _layout ??= new BoardLayout(_pitch, _hitSize);

        public bool IsCursorVisible => _cursor.enabled;

        public int CursorCell { get; private set; } = -1;

        /// <summary>灯の明るさ（0〜1）。</summary>
        public float GlowLevel(int cell) => _levels[LightsBoard.RequireIndex(cell)];

        public bool IsMarked(int cell) => _marks[LightsBoard.RequireIndex(cell)].enabled;

        /// <summary>すべての灯が盤の点灯に追いついたか。</summary>
        public bool IsSettledTo(LightsBoard board)
        {
            for (var cell = 0; cell < LightsBoard.CellCount; cell++)
            {
                if (_levels[cell] != (board.IsLit(cell) ? 1f : 0f)) { return false; }
            }

            return true;
        }

        public Vector3 CellWorld(int cell)
        {
            var (x, y) = Layout.CellCenter(cell);
            return transform.TransformPoint(new Vector3(x, y, 0));
        }

        /// <summary>ワールド座標にあるマス。マスの間の隙間や盤の外なら false。</summary>
        public bool TryCellAt(Vector2 world, out int cell)
        {
            var local = transform.InverseTransformPoint(world);
            return Layout.TryCellAt(local.x, local.y, out cell);
        }

        /// <summary>
        /// board の点灯を描く。cursor が負なら枠を隠す。answer のビットのマスに印を出す。
        /// fadeStep は今回寄せる量（経過時間 ÷ 演出時間）。1 以上ならその状態をそのまま描く（撮影はこれを使う）。
        /// </summary>
        public void Render(LightsBoard board, int cursor, uint answer, float fadeStep)
        {
            if (!float.IsFinite(fadeStep) || fadeStep < 0) { throw new ArgumentOutOfRangeException(nameof(fadeStep)); }
            for (var cell = 0; cell < LightsBoard.CellCount; cell++)
            {
                var target = board.IsLit(cell) ? 1f : 0f;
                var level = Mathf.MoveTowards(_levels[cell], target, fadeStep);
                _levels[cell] = level;
                _tiles[cell].color = Color.Lerp(_tileDark, _tileLit, level);
                var glow = _glow;
                glow.a = level;
                _glows[cell].color = glow;
                _glows[cell].transform.localScale = Vector3.one * Mathf.Lerp(1.4f, 2.2f, level);
                _marks[cell].enabled = (answer & (1u << cell)) != 0;
            }

            _cursor.enabled = cursor >= 0;
            CursorCell = cursor;
            if (cursor >= 0)
            {
                var (x, y) = Layout.CellCenter(cursor);
                _cursor.transform.localPosition = new Vector3(x, y, 0);
            }
        }
    }
}
