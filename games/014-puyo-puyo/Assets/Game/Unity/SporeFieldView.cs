using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>片側の盤面の描画。Prefab に配置済みの SpriteRenderer を使い回し、Core の状態を読むだけで書き戻さない。</summary>
    public sealed class SporeFieldView : MonoBehaviour
    {
        public const int PreviewPairs = 2;

        private const float PreviewScale = .8f;
        private const float PopMinScale = .35f;
        private const float PopDimAlpha = .55f;
        private const int BlinkTicks = 3;

        [SerializeField] private SpriteRenderer[] _cells;
        [SerializeField] private SpriteRenderer[] _active;
        [SerializeField] private SpriteRenderer[] _next;
        [SerializeField] private Transform[] _nextAnchors;
        [SerializeField] private Sprite[] _sprites;

        public int ShownCells => CountShown(_cells);
        public int ShownActive => CountShown(_active);
        public int ShownNext => CountShown(_next);

        public Vector3 CellPosition(int x, int y) => transform.TransformPoint(CellLocal(x, y));

        public Vector3 ActivePosition(int index) => _active[index].transform.position;

        /// <summary>そのマスに表示中の胞子の絵。なければ null。</summary>
        public Sprite SpriteAt(int x, int y)
        {
            var cell = _cells[Board.Index(x, y)];
            return cell.enabled ? cell.sprite : null;
        }

        public Sprite SporeSprite(Cell cell) => _sprites[(int)cell - 1];

        /// <summary>盤面のマスを並べる。シーン生成時に呼ぶ。</summary>
        public void Arrange()
        {
            for (var y = 0; y < Board.Height; y++)
            {
                for (var x = 0; x < Board.Width; x++) { _cells[Board.Index(x, y)].transform.localPosition = CellLocal(x, y); }
            }
        }

        /// <summary>盤面の状態を描く。null なら空の盤面を描く。</summary>
        public void Render(PlayerField field, int settleTicksPerRow, bool showNext)
        {
            if (field == null)
            {
                Hide(_cells);
                Hide(_active);
                Hide(_next);
                return;
            }

            DrawCells(field.Board);
            DrawFalls(field, settleTicksPerRow);
            DrawPopping(field);
            DrawActive(field.Active);
            if (showNext) { DrawNext(field); }
            else { Hide(_next); }
        }

        private static Vector3 CellLocal(float x, float y) => new(x - ((Board.Width - 1) / 2f), y - ((Board.VisibleHeight - 1) / 2f), 0);

        private static int CountShown(SpriteRenderer[] renderers)
        {
            var count = 0;
            foreach (var renderer in renderers)
            {
                if (renderer.enabled) { count++; }
            }

            return count;
        }

        private static void Hide(SpriteRenderer[] renderers)
        {
            foreach (var renderer in renderers) { renderer.enabled = false; }
        }

        private void DrawCells(Board board)
        {
            for (var y = 0; y < Board.Height; y++)
            {
                for (var x = 0; x < Board.Width; x++)
                {
                    var renderer = _cells[Board.Index(x, y)];
                    var cell = board.At(x, y);
                    renderer.enabled = cell != Cell.Empty && y < Board.VisibleHeight;
                    renderer.color = Color.white;
                    renderer.transform.localScale = Vector3.one;
                    renderer.transform.localPosition = CellLocal(x, y);
                    if (cell != Cell.Empty) { renderer.sprite = SporeSprite(cell); }
                }
            }
        }

        // Board は落下後の状態なので、残り時間ぶんだけ上へずらして描く。
        private void DrawFalls(PlayerField field, int settleTicksPerRow)
        {
            if (settleTicksPerRow <= 0) { return; }
            var remainingRows = field.PhaseRemaining / (float)settleTicksPerRow;
            var falls = field.Falls;
            for (var i = 0; i < falls.Count; i++)
            {
                var fall = falls[i];
                var shownY = fall.ToY + Mathf.Min(fall.Distance, remainingRows);
                var renderer = _cells[Board.Index(fall.X, fall.ToY)];
                renderer.enabled = shownY <= Board.VisibleHeight - 1;
                renderer.transform.localPosition = CellLocal(fall.X, shownY);
            }
        }

        // 演出は Tick から決め、同じ状態なら撮影でも同じ絵になるようにする。
        private void DrawPopping(PlayerField field)
        {
            var groups = field.PoppingGroups;
            if (groups.Count == 0 || field.PhaseDuration <= 0) { return; }
            var scale = Mathf.Lerp(PopMinScale, 1, field.PhaseRemaining / (float)field.PhaseDuration);
            var alpha = (field.TickCount / BlinkTicks) % 2 == 0 ? 1 : PopDimAlpha;
            for (var g = 0; g < groups.Count; g++)
            {
                var cells = groups[g].Cells;
                for (var i = 0; i < cells.Count; i++)
                {
                    var renderer = _cells[Board.Index(cells[i].X, cells[i].Y)];
                    renderer.transform.localScale = new Vector3(scale, scale, 1);
                    renderer.color = new Color(1, 1, 1, alpha);
                }
            }
        }

        private void DrawActive(ActivePair? active)
        {
            if (active is not ActivePair pair)
            {
                Hide(_active);
                return;
            }

            DrawActiveCell(_active[0], pair.Pair.Pivot, pair.X, pair.Y);
            DrawActiveCell(_active[1], pair.Pair.Child, pair.ChildX, pair.ChildY);
        }

        private void DrawActiveCell(SpriteRenderer renderer, Cell cell, int x, int y)
        {
            renderer.enabled = y < Board.VisibleHeight;
            renderer.sprite = SporeSprite(cell);
            renderer.transform.localPosition = CellLocal(x, y);
        }

        private void DrawNext(PlayerField field)
        {
            for (var i = 0; i < PreviewPairs; i++)
            {
                var pair = field.Next(i);
                DrawPreview(_next[i * 2], pair.Pivot, _nextAnchors[i].position + (Vector3.down * PreviewScale / 2));
                DrawPreview(_next[(i * 2) + 1], pair.Child, _nextAnchors[i].position + (Vector3.up * PreviewScale / 2));
            }
        }

        private void DrawPreview(SpriteRenderer renderer, Cell cell, Vector3 position)
        {
            renderer.enabled = true;
            renderer.sprite = SporeSprite(cell);
            renderer.transform.position = position;
            renderer.transform.localScale = new Vector3(PreviewScale, PreviewScale, 1);
        }
    }
}
