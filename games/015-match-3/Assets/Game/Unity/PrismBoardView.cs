using Game.App;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 盤面の描画。Prefab に配置済みの SpriteRenderer を使い回し、Core の状態とフェーズの進み具合を読むだけで書き戻さない。
    /// マス (x, y) の結晶は常に同じ SpriteRenderer が描き、演出中だけ位置と大きさをずらす。
    /// </summary>
    public sealed class PrismBoardView : MonoBehaviour
    {
        private const float PieceScale = .9f;
        private const float ClearMinScale = .2f;
        private const float ReshuffleMinScale = .3f;

        [SerializeField] private SpriteRenderer[] _cells;
        [SerializeField] private Sprite[] _sprites;
        [SerializeField] private SpriteRenderer _selection;
        [SerializeField] private SpriteRenderer _cursor;

        // 落下中の各マスの、落ち始めの段。落下しないマスは自分の段。
        private readonly int[] _fromY = new int[Board.CellCount];
        private int _maxFall;

        public int ShownPieces
        {
            get
            {
                var count = 0;
                foreach (var cell in _cells)
                {
                    if (cell.enabled) { count++; }
                }

                return count;
            }
        }

        public bool IsSelectionVisible => _selection.enabled;
        public bool IsCursorVisible => _cursor.enabled;
        public Vector3 SelectionPosition => _selection.transform.position;
        public Vector3 CursorPosition => _cursor.transform.position;

        public Sprite PieceSprite(Piece piece) => _sprites[(int)piece - 1];

        /// <summary>そのマスに表示中の結晶の絵。なければ null。</summary>
        public Sprite SpriteAt(int x, int y)
        {
            var cell = _cells[Board.Index(x, y)];
            return cell.enabled ? cell.sprite : null;
        }

        /// <summary>そのマスの結晶を描いている位置（演出中はずれる）。</summary>
        public Vector3 PiecePosition(int x, int y) => _cells[Board.Index(x, y)].transform.position;

        /// <summary>マスの中心のワールド座標。</summary>
        public Vector3 CellWorld(int x, int y) => transform.TransformPoint(CellLocal(x, y));

        /// <summary>ワールド座標を盤面座標（左下が (0, 0)、1マスが 1）に直す。盤面の外も範囲外の値で返す。</summary>
        public Vector2 ToBoard(Vector3 world)
        {
            var local = transform.InverseTransformPoint(world);
            return new Vector2(local.x + (Board.Width / 2f), local.y + (Board.Height / 2f));
        }

        /// <summary>盤面のマスを並べる。シーン生成時に呼ぶ。</summary>
        public void Arrange()
        {
            for (var y = 0; y < Board.Height; y++)
            {
                for (var x = 0; x < Board.Width; x++) { _cells[Board.Index(x, y)].transform.localPosition = CellLocal(x, y); }
            }
        }

        /// <summary>動きのない盤面を描く。タイトルの飾りに使う。</summary>
        public void RenderStill(Board board)
        {
            var cells = board.ToCells();
            for (var i = 0; i < Board.CellCount; i++) { Place(i, cells[i], i % Board.Width, i / Board.Width, 1f); }
            _selection.enabled = false;
            _cursor.enabled = false;
        }

        /// <summary>試合の状態を描く。input が null なら選択とカーソルを出さない。</summary>
        public void Render(Match3Game game, SwapInput input)
        {
            var board = game.Board;
            var progress = game.PhaseDuration > 0 ? 1f - ((float)game.PhaseRemaining / game.PhaseDuration) : 1f;
            PrepareFalls(game);
            var cells = board.ToCells();
            for (var i = 0; i < Board.CellCount; i++)
            {
                var x = i % Board.Width;
                var y = i / Board.Width;
                var scale = game.Phase == GamePhase.Reshuffling ? Mathf.Lerp(ReshuffleMinScale, 1f, progress) : 1f;
                var shownY = game.Phase == GamePhase.Falling ? Mathf.Max(y, _fromY[i] - (progress * _maxFall)) : y;
                Place(i, cells[i], x, shownY, scale);
            }

            if (game.ActiveSwap is Swap swap) { DrawSwap(game, swap, progress); }
            if (game.Phase == GamePhase.Clearing)
            {
                var scale = Mathf.Lerp(1f, ClearMinScale, progress);
                foreach (var point in game.ClearingCells) { _cells[Board.Index(point.X, point.Y)].transform.localScale = Vector3.one * (PieceScale * scale); }
            }

            DrawMarker(_selection, input?.Selected);
            DrawMarker(_cursor, input != null && input.KeyboardActive ? input.Cursor : null);
        }

        /// <summary>試合がないときの空の盤面。</summary>
        public void Clear()
        {
            foreach (var cell in _cells) { cell.enabled = false; }
            _selection.enabled = false;
            _cursor.enabled = false;
        }

        private static Vector3 CellLocal(float x, float y) => new(x - ((Board.Width - 1) / 2f), y - ((Board.Height - 1) / 2f), 0);

        private void Place(int index, Piece piece, float x, float y, float scale)
        {
            var renderer = _cells[index];
            renderer.enabled = piece != Piece.Empty;
            if (!renderer.enabled) { return; }
            renderer.sprite = PieceSprite(piece);
            renderer.transform.localPosition = CellLocal(x, y);
            renderer.transform.localScale = Vector3.one * (PieceScale * scale);
        }

        // 一定の速さで落ち、着いた段で止まる。いちばん長く落ちる結晶がフェーズの終わりに着く。
        // 補充の結晶は盤面の上から入り、マスクの外は見えない。
        private void PrepareFalls(Match3Game game)
        {
            _maxFall = 0;
            for (var i = 0; i < _fromY.Length; i++) { _fromY[i] = i / Board.Width; }
            foreach (var move in game.Falls)
            {
                _fromY[Board.Index(move.X, move.ToY)] = move.FromY;
                _maxFall = Mathf.Max(_maxFall, move.Distance);
            }
        }

        // 成立する入れ替えは片道、不成立は往復する。
        private void DrawSwap(Match3Game game, Swap swap, float progress)
        {
            var t = game.Phase == GamePhase.Reverting ? 1f - Mathf.Abs(1f - (2f * progress)) : progress;
            var a = _cells[Board.Index(swap.From.X, swap.From.Y)].transform;
            var b = _cells[Board.Index(swap.To.X, swap.To.Y)].transform;
            a.localPosition = Vector3.Lerp(CellLocal(swap.From.X, swap.From.Y), CellLocal(swap.To.X, swap.To.Y), t);
            b.localPosition = Vector3.Lerp(CellLocal(swap.To.X, swap.To.Y), CellLocal(swap.From.X, swap.From.Y), t);
        }

        private void DrawMarker(SpriteRenderer marker, Point? cell)
        {
            marker.enabled = cell.HasValue;
            if (cell is Point point) { marker.transform.localPosition = CellLocal(point.X, point.Y); }
        }
    }
}
