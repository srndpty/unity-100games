using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>盤面の描画。Prefab に配置済みの SpriteRenderer を使い回し、Core の状態をそのまま描く。</summary>
    public sealed class StrataDropView : MonoBehaviour
    {
        /// <summary>描く段数。可視の20段と、出現位置の2段。</summary>
        public const int DrawnRows = 22;

        private const float BottomRowY = -10.5f;
        private const float PreviewScale = .72f;
        private const float GhostAlpha = .3f;
        private const float UsedHoldAlpha = .4f;
        private const float FlashBright = .85f;
        private const float FlashDim = .35f;
        private const int BlinkTicks = 3;

        [SerializeField] private Transform _boardRoot;
        [SerializeField] private SpriteRenderer[] _cells;
        [SerializeField] private SpriteRenderer[] _active;
        [SerializeField] private SpriteRenderer[] _ghost;
        [SerializeField] private SpriteRenderer[] _hold;
        [SerializeField] private SpriteRenderer[] _next;
        [SerializeField] private SpriteRenderer[] _flashes;
        [SerializeField] private Transform _holdAnchor;
        [SerializeField] private Transform[] _nextAnchors;
        [SerializeField] private Sprite[] _ores;

        private StrataGame _shownGame;
        private int _shownRevision = -1;
        private int _shownTick = -1;

        public int ShownLockedCells => CountShown(_cells);
        public int ShownActiveCells => CountShown(_active);
        public int ShownGhostCells => CountShown(_ghost);
        public int ShownHoldCells => CountShown(_hold);
        public int ShownNextCells => CountShown(_next);
        public int ShownFlashes => CountShown(_flashes);

        public Vector3 CellPosition(int x, int y) => _boardRoot.TransformPoint(CellLocal(x, y));

        public Vector3 ActiveCellPosition(int index) => _active[index].transform.position;

        /// <summary>そのマスに表示中の確定ブロックの絵。なければ null。</summary>
        public Sprite LockedSpriteAt(int x, int y)
        {
            var cell = _cells[(y * Board.Width) + x];
            return cell.enabled ? cell.sprite : null;
        }

        public Sprite OreSprite(PieceKind kind) => _ores[(int)kind];

        /// <summary>盤面のマスを並べる。シーン生成時に呼ぶ。</summary>
        public void Arrange()
        {
            for (var y = 0; y < DrawnRows; y++)
            {
                for (var x = 0; x < Board.Width; x++) { _cells[(y * Board.Width) + x].transform.localPosition = CellLocal(x, y); }
            }
        }

        /// <summary>試合の状態を描く。変化がなければ何もしない。null なら空の盤面を描く。</summary>
        public void Render(StrataGame game)
        {
            if (game == null)
            {
                if (_shownRevision != -1 || _shownGame != null) { Clear(); }
                return;
            }

            var blinking = game.Phase == GamePhase.Clearing && game.TickCount != _shownTick;
            if (game == _shownGame && game.Revision == _shownRevision && !blinking) { return; }
            _shownGame = game;
            _shownRevision = game.Revision;
            _shownTick = game.TickCount;
            DrawBoard(game.Board);
            DrawPiece(_ghost, game.Ghost, GhostAlpha);
            DrawPiece(_active, game.Active, 1);
            DrawHold(game);
            DrawNext(game);
            DrawFlashes(game);
        }

        /// <summary>何も置かれていない盤面にする。</summary>
        public void Clear()
        {
            _shownGame = null;
            _shownRevision = -1;
            _shownTick = -1;
            Hide(_cells);
            Hide(_active);
            Hide(_ghost);
            Hide(_hold);
            Hide(_next);
            Hide(_flashes);
        }

        private static Vector3 CellLocal(int x, int y) => new(x - ((Board.Width - 1) / 2f), y + BottomRowY, 0);

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

        private void DrawBoard(Board board)
        {
            for (var y = 0; y < DrawnRows; y++)
            {
                for (var x = 0; x < Board.Width; x++)
                {
                    var renderer = _cells[(y * Board.Width) + x];
                    var kind = board.At(x, y);
                    renderer.enabled = kind.HasValue;
                    if (kind.HasValue) { renderer.sprite = OreSprite(kind.Value); }
                }
            }
        }

        private void DrawPiece(SpriteRenderer[] renderers, ActivePiece? piece, float alpha)
        {
            if (piece is not ActivePiece value)
            {
                Hide(renderers);
                return;
            }

            for (var i = 0; i < PieceShapes.CellCount; i++)
            {
                var cell = value.CellAt(i);
                var renderer = renderers[i];
                renderer.enabled = cell.Y < DrawnRows;
                renderer.sprite = OreSprite(value.Kind);
                renderer.color = new Color(1, 1, 1, alpha);
                renderer.transform.localPosition = CellLocal(cell.X, cell.Y);
            }
        }

        private void DrawHold(StrataGame game)
        {
            if (game.HoldKind is PieceKind kind) { DrawPreview(_hold, 0, kind, _holdAnchor.position, game.CanHold ? 1 : UsedHoldAlpha); }
            else { Hide(_hold); }
        }

        private void DrawNext(StrataGame game)
        {
            for (var i = 0; i < StrataGame.PreviewCount; i++)
            {
                DrawPreview(_next, i * PieceShapes.CellCount, game.Preview(i), _nextAnchors[i].position, 1);
            }
        }

        // 種類ごとに形の大きさが違うので、形の中心を枠の中心に合わせる。
        private void DrawPreview(SpriteRenderer[] renderers, int start, PieceKind kind, Vector3 center, float alpha)
        {
            var cells = PieceShapes.Cells(kind, Rotation.North);
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < cells.Count; i++)
            {
                var point = new Vector2(cells[i].X, cells[i].Y);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }

            var middle = (min + max) / 2f;
            for (var i = 0; i < cells.Count; i++)
            {
                var renderer = renderers[start + i];
                var offset = new Vector2(cells[i].X, cells[i].Y) - middle;
                renderer.enabled = true;
                renderer.sprite = OreSprite(kind);
                renderer.color = new Color(1, 1, 1, alpha);
                renderer.transform.position = center + ((Vector3)offset * PreviewScale);
                renderer.transform.localScale = new Vector3(PreviewScale, PreviewScale, 1);
            }
        }

        // 点滅は Tick から決め、同じ状態なら撮影でも同じ絵になるようにする。
        private void DrawFlashes(StrataGame game)
        {
            Hide(_flashes);
            var rows = game.ClearingRows;
            var alpha = (game.TickCount / BlinkTicks) % 2 == 0 ? FlashBright : FlashDim;
            for (var i = 0; i < rows.Count; i++)
            {
                var flash = _flashes[i];
                flash.enabled = true;
                flash.color = new Color(1, 1, 1, alpha);
                flash.transform.localPosition = new Vector3(0, rows[i] + BottomRowY, 0);
            }
        }
    }
}
