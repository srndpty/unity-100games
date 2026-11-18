using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>盤面の描画。レイヤーごとの SpriteRenderer を使い回す。状態は Core で確定済みで、移動の補間は見た目だけ。</summary>
    public sealed class HarborShiftView : MonoBehaviour
    {
        private const float FitWidth = 17.2f;
        private const float FitHeight = 15.5f;
        private const float MaxCellSize = 2.2f;

        // 隣り合うタイルの間に背景が透けないよう、わずかに重ねる。
        private const float TileOverlap = 1.01f;

        [SerializeField] private Transform _boardRoot;
        [SerializeField] private SpriteRenderer[] _floors;
        [SerializeField] private SpriteRenderer[] _walls;
        [SerializeField] private SpriteRenderer[] _goals;
        [SerializeField] private SpriteRenderer[] _boxes;
        [SerializeField] private SpriteRenderer _player;
        [SerializeField] private Sprite _boxSprite;
        [SerializeField] private Sprite _boxPlacedSprite;

        private LevelMap _map;
        private Transform _movingBox;
        private Vector3 _playerFrom;
        private Vector3 _playerTo;
        private Vector3 _boxFrom;
        private Vector3 _boxTo;
        private float _seconds;
        private float _elapsed;
        private bool _animating;

        public bool IsAnimating => _animating;
        public float BoardHalfWidth => _map == null ? 0 : _map.Width * _boardRoot.localScale.x / 2f;
        public float BoardHalfHeight => _map == null ? 0 : _map.Height * _boardRoot.localScale.y / 2f;
        public Vector3 PlayerPosition => _player.transform.position;
        public Quaternion PlayerRotation => _player.transform.localRotation;
        public Sprite BoxSprite => _boxSprite;
        public Sprite BoxPlacedSprite => _boxPlacedSprite;
        public int ShownFloors => CountShown(_floors);
        public int ShownWalls => CountShown(_walls);
        public int ShownGoals => CountShown(_goals);
        public int ShownBoxes => CountShown(_boxes);

        public Vector3 CellPosition(Cell cell) => _boardRoot.TransformPoint(CellLocal(cell));

        /// <summary>そのマスに表示中の箱の絵。箱がなければ null。</summary>
        public Sprite BoxSpriteAt(Cell cell)
        {
            var position = CellPosition(cell);
            foreach (var box in _boxes)
            {
                if (box.enabled && (box.transform.position - position).sqrMagnitude < .0001f) { return box.sprite; }
            }

            return null;
        }

        /// <summary>演出なしで面全体を描く。</summary>
        public void Show(LevelMap map, BoardState board, Direction facing)
        {
            _animating = false;
            _movingBox = null;
            _map = map;
            var size = Mathf.Min(FitWidth / map.Width, FitHeight / map.Height, MaxCellSize);
            _boardRoot.localScale = new Vector3(size, size, 1);
            DrawLayers(map);
            DrawPieces(board, facing);
        }

        /// <summary>1手の演出を始める。再生中の演出は打ち切り、新しい手の開始位置から描き直す。</summary>
        public void Play(MoveRecord record, bool reversed, BoardState board, Direction facing, float seconds)
        {
            DrawPieces(board, facing);
            _playerFrom = CellLocal(reversed ? record.PlayerTo : record.PlayerFrom);
            _playerTo = CellLocal(reversed ? record.PlayerFrom : record.PlayerTo);
            _movingBox = null;
            if (record.IsPush)
            {
                var from = reversed ? record.BoxTo.Value : record.BoxFrom.Value;
                var to = reversed ? record.BoxFrom.Value : record.BoxTo.Value;
                _boxFrom = CellLocal(from);
                _boxTo = CellLocal(to);
                for (var i = 0; i < board.Boxes.Count; i++)
                {
                    if (board.Boxes[i] == to) { _movingBox = _boxes[i].transform; }
                }
            }

            _seconds = seconds;
            _elapsed = 0;
            _animating = seconds > 0;
            if (_animating) { DrawSlide(0); }
        }

        public void Tick(float deltaTime)
        {
            if (!_animating) { return; }
            _elapsed += deltaTime;
            if (_elapsed < _seconds) { DrawSlide(_elapsed / _seconds); }
            else { Snap(); }
        }

        /// <summary>演出を飛ばして最終状態を描く。</summary>
        public void Snap()
        {
            if (!_animating) { return; }
            _animating = false;
            DrawSlide(1);
        }

        private void DrawSlide(float t)
        {
            _player.transform.localPosition = Vector3.Lerp(_playerFrom, _playerTo, t);
            if (_movingBox != null) { _movingBox.localPosition = Vector3.Lerp(_boxFrom, _boxTo, t); }
        }

        private void DrawLayers(LevelMap map)
        {
            var floors = 0;
            var walls = 0;
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    var cell = new Cell(x, y);
                    if (map.IsFloor(cell)) { PlaceTile(_floors[floors++], cell); }
                    if (map.IsWall(cell)) { PlaceTile(_walls[walls++], cell); }
                }
            }

            HideFrom(_floors, floors);
            HideFrom(_walls, walls);
            for (var i = 0; i < map.Goals.Count; i++)
            {
                _goals[i].enabled = true;
                _goals[i].transform.localPosition = CellLocal(map.Goals[i]);
            }

            HideFrom(_goals, map.Goals.Count);
        }

        private void DrawPieces(BoardState board, Direction facing)
        {
            for (var i = 0; i < board.Boxes.Count; i++)
            {
                var cell = board.Boxes[i];
                _boxes[i].enabled = true;
                _boxes[i].sprite = _map.IsGoal(cell) ? _boxPlacedSprite : _boxSprite;
                _boxes[i].transform.localPosition = CellLocal(cell);
            }

            HideFrom(_boxes, board.Boxes.Count);
            _player.enabled = true;
            _player.transform.localPosition = CellLocal(board.Player);
            _player.transform.localRotation = Quaternion.Euler(0, 0, FacingAngle(facing));
        }

        private void PlaceTile(SpriteRenderer tile, Cell cell)
        {
            tile.enabled = true;
            tile.transform.localPosition = CellLocal(cell);
            tile.transform.localScale = new Vector3(TileOverlap, TileOverlap, 1);
        }

        private Vector3 CellLocal(Cell cell) =>
            new(cell.X - ((_map.Width - 1) / 2f), ((_map.Height - 1) / 2f) - cell.Y, 0);

        private static void HideFrom(SpriteRenderer[] pool, int start)
        {
            for (var i = start; i < pool.Length; i++) { pool[i].enabled = false; }
        }

        private static int CountShown(SpriteRenderer[] pool)
        {
            var count = 0;
            foreach (var item in pool)
            {
                if (item.enabled) { count++; }
            }

            return count;
        }

        // 絵は上向きに描いてある。
        private static float FacingAngle(Direction facing) => facing switch
        {
            Direction.Left => 90,
            Direction.Down => 180,
            Direction.Right => -90,
            _ => 0,
        };
    }
}
