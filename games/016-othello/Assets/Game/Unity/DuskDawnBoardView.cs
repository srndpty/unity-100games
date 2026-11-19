using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 盤の描画。Prefab に配置済みの SpriteRenderer を使い回し、Core の局面とフェーズの進み具合を読むだけで書き戻さない。
    /// マス (x, y) の石は常に同じ SpriteRenderer が描き、返す演出中だけ横幅を縮めて裏返して見せる。
    /// </summary>
    public sealed class DuskDawnBoardView : MonoBehaviour
    {
        private const float DiscScale = .86f;
        private const float PlacedStartScale = .55f;

        [SerializeField] private SpriteRenderer[] _discs;
        [SerializeField] private SpriteRenderer[] _hints;
        [SerializeField] private Sprite _blackSprite;
        [SerializeField] private Sprite _whiteSprite;
        [SerializeField] private SpriteRenderer _lastMove;
        [SerializeField] private SpriteRenderer _cursor;

        public bool IsCursorVisible => _cursor.enabled;
        public Vector3 CursorPosition => _cursor.transform.position;
        public bool IsLastMoveVisible => _lastMove.enabled;
        public Vector3 LastMovePosition => _lastMove.transform.position;

        public int ShownDiscs => CountEnabled(_discs);
        public int ShownHints => CountEnabled(_hints);

        public Sprite DiscSprite(Disc disc) => disc == Disc.Black ? _blackSprite : disc == Disc.White ? _whiteSprite : null;

        /// <summary>そのマスに表示中の石の絵。なければ null。</summary>
        public Sprite SpriteAt(Square square)
        {
            var disc = _discs[square.Index];
            return disc.enabled ? disc.sprite : null;
        }

        /// <summary>そのマスの石の横幅の倍率（1 で通常、0 で真横）。</summary>
        public float WidthAt(Square square) => _discs[square.Index].transform.localScale.x / DiscScale;

        /// <summary>そのマスの石の大きさの倍率（1 で通常）。</summary>
        public float HeightAt(Square square) => _discs[square.Index].transform.localScale.y / DiscScale;

        public bool IsHintShown(Square square) => _hints[square.Index].enabled;

        /// <summary>マスの中心のワールド座標。</summary>
        public Vector3 CellWorld(Square square) => transform.TransformPoint(CellLocal(square.X, square.Y));

        /// <summary>ワールド座標を盤の座標（左上の角が (0, 0)、1マスが 1、右と下が正）に直す。盤の外も範囲外の値で返す。</summary>
        public Vector2 ToBoard(Vector3 world)
        {
            var local = transform.InverseTransformPoint(world);
            return new Vector2(local.x + (Square.Size / 2f), (Square.Size / 2f) - local.y);
        }

        /// <summary>石と印をマスに並べる。シーン生成時に呼ぶ。</summary>
        public void Arrange()
        {
            for (var i = 0; i < Square.Count; i++)
            {
                var square = Square.FromIndex(i);
                _discs[i].transform.localPosition = CellLocal(square.X, square.Y);
                _hints[i].transform.localPosition = CellLocal(square.X, square.Y);
            }
        }

        /// <summary>動きのない局面を描く。タイトルの飾りに使う。</summary>
        public void RenderStill(Position position)
        {
            for (var i = 0; i < Square.Count; i++)
            {
                PlaceDisc(i, position.At(Square.FromIndex(i)), 1f, 1f);
                _hints[i].enabled = false;
            }

            _lastMove.enabled = false;
            _cursor.enabled = false;
        }

        /// <summary>試合の状態を描く。hints は合法手の印を出すマス、cursor はキー操作のカーソル。</summary>
        public void Render(OthelloGame game, ulong hints, Square? cursor)
        {
            var position = game.Position;
            var placing = game.Phase == GamePhase.Placing && game.LastMove.HasValue;
            var progress = game.PhaseDuration > 0 ? 1f - ((float)game.PhaseRemaining / game.PhaseDuration) : 1f;
            for (var i = 0; i < Square.Count; i++)
            {
                var disc = position.At(Square.FromIndex(i));
                var bit = 1UL << i;
                if (placing && (game.LastFlips & bit) != 0)
                {
                    // 前半は元の色のまま細くなり、後半は新しい色で太くなる。
                    var before = disc == Disc.Black ? Disc.White : Disc.Black;
                    PlaceDisc(i, progress < .5f ? before : disc, Mathf.Abs(1f - (2f * progress)), 1f);
                }
                else if (placing && game.LastMove!.Value.Index == i)
                {
                    var grow = Mathf.Lerp(PlacedStartScale, 1f, Mathf.Clamp01(progress * 2f));
                    PlaceDisc(i, disc, grow, grow);
                }
                else
                {
                    PlaceDisc(i, disc, 1f, 1f);
                }

                _hints[i].enabled = (hints & bit) != 0;
            }

            _lastMove.enabled = game.LastMove.HasValue;
            if (game.LastMove is Square last) { _lastMove.transform.localPosition = CellLocal(last.X, last.Y); }
            _cursor.enabled = cursor.HasValue;
            if (cursor is Square at) { _cursor.transform.localPosition = CellLocal(at.X, at.Y); }
        }

        private static Vector3 CellLocal(int x, int y) => new(x - ((Square.Size - 1) / 2f), ((Square.Size - 1) / 2f) - y, 0);

        private static int CountEnabled(SpriteRenderer[] renderers)
        {
            var count = 0;
            foreach (var renderer in renderers)
            {
                if (renderer.enabled) { count++; }
            }

            return count;
        }

        private void PlaceDisc(int index, Disc disc, float width, float height)
        {
            var renderer = _discs[index];
            renderer.enabled = disc != Disc.Empty;
            if (!renderer.enabled) { return; }
            renderer.sprite = DiscSprite(disc);
            renderer.transform.localScale = new Vector3(DiscScale * width, DiscScale * height, 1);
        }
    }
}
