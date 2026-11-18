using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>盤面の描画。状態は Core で確定済みで、ここでは移動の結果を見た目として再生するだけ。</summary>
    public sealed class TwinForgeView : MonoBehaviour
    {
        private const float MergePop = .16f;

        [SerializeField] private float _cellPitch = 3.7f;
        [SerializeField] private float _tileScale = 3.5f;
        [SerializeField] private SpriteRenderer[] _tiles;
        [SerializeField] private Sprite[] _plates;

        private readonly SpriteRenderer[] _tileAtCell = new SpriteRenderer[BoardState.CellCount];
        private MoveOutcome _outcome;
        private SpawnedTile _spawn;
        private BoardState _final;
        private float _slideSeconds;
        private float _popSeconds;
        private float _elapsed;

        public bool IsAnimating => _outcome != null;
        public float BoardHalfWidth => _cellPitch * BoardState.Size / 2f;

        public Sprite PlateSprite(int value) => _plates[PlateIndex(value)];

        /// <summary>そのマスに表示中の板の絵。空きマスなら null。</summary>
        public Sprite SpriteAt(int cell) => _tileAtCell[cell] == null ? null : _tileAtCell[cell].sprite;

        /// <summary>演出なしで盤面を表示する。試合がないときは空の盤面を描く。</summary>
        public void Show(BoardState board)
        {
            _outcome = null;
            Draw(board ?? BoardState.Empty);
        }

        /// <summary>移動の演出を始める。再生中の演出は打ち切り、新しい移動の開始位置から描き直す。</summary>
        public void Play(MoveOutcome outcome, SpawnedTile spawn, BoardState final, float slideSeconds, float popSeconds)
        {
            _outcome = outcome;
            _spawn = spawn;
            _final = final;
            _slideSeconds = slideSeconds;
            _popSeconds = popSeconds;
            _elapsed = 0;
            Tick(0);
        }

        public void Tick(float deltaTime)
        {
            if (_outcome == null) { return; }
            _elapsed += deltaTime;
            if (_elapsed < _slideSeconds) { DrawSlide(_elapsed / _slideSeconds); }
            else if (_elapsed < _slideSeconds + _popSeconds) { DrawPop((_elapsed - _slideSeconds) / _popSeconds); }
            else { Snap(); }
        }

        /// <summary>演出を飛ばして最終状態を描く。</summary>
        public void Snap()
        {
            if (_outcome == null) { return; }
            _outcome = null;
            Draw(_final);
        }

        private void DrawSlide(float t)
        {
            System.Array.Clear(_tileAtCell, 0, _tileAtCell.Length);
            var eased = 1f - ((1f - t) * (1f - t));
            var moves = _outcome.Moves;
            for (var i = 0; i < _tiles.Length; i++)
            {
                var visible = i < moves.Count;
                _tiles[i].enabled = visible;
                if (!visible) { continue; }
                var move = moves[i];
                Place(_tiles[i], Vector3.Lerp(CellLocal(move.From), CellLocal(move.To), eased), move.Value, 1);
            }
        }

        private void DrawPop(float t)
        {
            Draw(_final);
            var pop = 1f + (MergePop * Mathf.Sin(t * Mathf.PI));
            foreach (var move in _outcome.Moves)
            {
                if (move.Merges) { SetScale(_tileAtCell[move.To], pop); }
            }

            if (_spawn != null) { SetScale(_tileAtCell[_spawn.Cell], t); }
        }

        private void Draw(BoardState board)
        {
            var used = 0;
            for (var cell = 0; cell < BoardState.CellCount; cell++)
            {
                var value = board[cell];
                _tileAtCell[cell] = value == 0 ? null : _tiles[used++];
                if (value != 0) { Place(_tileAtCell[cell], CellLocal(cell), value, 1); }
            }

            for (var i = 0; i < _tiles.Length; i++) { _tiles[i].enabled = i < used; }
        }

        private void Place(SpriteRenderer tile, Vector3 position, int value, float scale)
        {
            tile.sprite = PlateSprite(value);
            tile.transform.localPosition = position;
            SetScale(tile, scale);
        }

        private void SetScale(SpriteRenderer tile, float scale) =>
            tile.transform.localScale = new Vector3(_tileScale * scale, _tileScale * scale, 1);

        private Vector3 CellLocal(int cell)
        {
            var center = (BoardState.Size - 1) / 2f;
            var x = cell % BoardState.Size;
            var y = cell / BoardState.Size;
            return new Vector3((x - center) * _cellPitch, (center - y) * _cellPitch, 0);
        }

        // 2 → 0、4 → 1 … 2048 → 10。
        private static int PlateIndex(int value) => Mathf.RoundToInt(Mathf.Log(value, 2)) - 1;
    }
}
