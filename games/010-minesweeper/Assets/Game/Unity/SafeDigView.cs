using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>盤面の描画。Core の <see cref="CellFace"/> だけを見てセルの絵を決める。</summary>
    public sealed class SafeDigView : MonoBehaviour
    {
        [SerializeField] private int _width;
        [SerializeField] private int _height;
        [SerializeField] private SpriteRenderer[] _cells;
        [SerializeField] private Sprite[] _faces;
        [SerializeField] private SpriteRenderer _cursor;

        public int Width => _width;
        public int Height => _height;
        public bool IsCursorVisible => _cursor.enabled;

        public Sprite FaceSprite(CellFace face) => _faces[(int)face];

        public Sprite CellSprite(int x, int y) => _cells[(y * _width) + x].sprite;

        public Vector3 CellWorld(int x, int y) => transform.TransformPoint(CellLocal(x, y));

        /// <summary>ワールド座標が盤面の内側なら、そのセルを返す。</summary>
        public bool TryCellAt(Vector2 world, out int x, out int y)
        {
            var local = transform.InverseTransformPoint(world);
            x = Mathf.FloorToInt(local.x + (_width / 2f));
            y = Mathf.FloorToInt((_height / 2f) - local.y);
            return x >= 0 && x < _width && y >= 0 && y < _height;
        }

        /// <summary>試合がないときは全セルを未公開で描く。</summary>
        public void Render(MinesweeperGame game, Vector2Int? cursor)
        {
            for (var y = 0; y < _height; y++)
            {
                for (var x = 0; x < _width; x++)
                {
                    _cells[(y * _width) + x].sprite = FaceSprite(game?.Face(x, y) ?? CellFace.Hidden);
                }
            }

            _cursor.enabled = cursor.HasValue;
            if (cursor.HasValue) { _cursor.transform.localPosition = CellLocal(cursor.Value.x, cursor.Value.y); }
        }

        private Vector3 CellLocal(int x, int y) => new(x - ((_width - 1) / 2f), ((_height - 1) / 2f) - y, 0);
    }
}
