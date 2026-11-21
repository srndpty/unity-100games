using System;
using Game.Core;
using UnityEngine;

namespace Game.Unity
{
    /// <summary>
    /// 盤の描画。占有マスクは 1 セル = 1 テクセルのテクスチャに書き、Point フィルタで拡大して描く。
    /// 盤が変わったとき（版番号が変わったとき）だけ書き直す。自機と敵は Tick の状態をそのまま描き、補間しない。
    /// </summary>
    public sealed class TideClaimFieldView : MonoBehaviour
    {
        /// <summary>1 セルの大きさ（Stage のローカル単位）。盤の初期画像はこの逆数を pixels per unit にしている。</summary>
        public const float CellSize = .2f;

        [SerializeField] private SpriteRenderer _field;
        [SerializeField] private SpriteRenderer _player;
        [SerializeField] private SpriteRenderer _enemy;
        [SerializeField] private Color _playerIdle = new(1f, 1f, 1f);
        [SerializeField] private Color _playerDrawing = new(1f, .45f, .35f);
        [SerializeField] private float _playerSizeCells = 1.8f;
        [SerializeField] private float _enemySpinDegreesPerTick = 6f;

        private Texture2D _texture;
        private Sprite _sprite;
        private Color32[] _pixels;
        private QixGame _shownGame;
        private int _shownVersion;
        private bool _showsInitial;

        public int Width { get; private set; }

        public int Height { get; private set; }

        public SpriteRenderer Player => _player;

        public SpriteRenderer Enemy => _enemy;

        public SpriteRenderer Field => _field;

        /// <summary>盤を描き直した回数（変わったときだけ書き直すことの確認用）。</summary>
        public int Uploads { get; private set; }

        /// <summary>描いているセルの色。</summary>
        public Color32 PixelAt(int x, int y) => _pixels[(y * Width) + x];

        /// <summary>セルの中心のワールド座標。</summary>
        public Vector3 CellWorld(int x, int y) => transform.TransformPoint(Local((x + .5f) * QixConfig.SubCell, (y + .5f) * QixConfig.SubCell));

        /// <summary>盤の四隅を含む範囲（ワールド座標）。</summary>
        public Bounds FieldBounds
        {
            get
            {
                var min = transform.TransformPoint(Local(0, 0));
                var max = transform.TransformPoint(Local(Width * QixConfig.SubCell, Height * QixConfig.SubCell));
                var bounds = new Bounds(min, Vector3.zero);
                bounds.Encapsulate(max);
                return bounds;
            }
        }

        /// <summary>試合の前の盤（外周の縁と海）を描く。試合は作らず、色を直接決める。</summary>
        public void RenderInitial(int width, int height)
        {
            Ensure(width, height);
            _player.enabled = false;
            _enemy.enabled = false;
            if (_showsInitial) { return; }
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++) { _pixels[(y * width) + x] = TideClaimPalette.InitialColor(x, y, width, height); }
            }

            Upload();
            _showsInitial = true;
            _shownGame = null;
        }

        public void Render(QixGame game)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            Ensure(game.Width, game.Height);
            if (_showsInitial || _shownGame != game || _shownVersion != game.GridVersion)
            {
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        var cell = game.CellAt(x, y);
                        _pixels[(y * Width) + x] = TideClaimPalette.CellColor(cell, cell == Cell.Claimed && game.IsBoundary(x, y), x, y);
                    }
                }

                Upload();
                _showsInitial = false;
                _shownGame = game;
                _shownVersion = game.GridVersion;
            }

            _player.enabled = true;
            _player.transform.localPosition = Local((game.PlayerX + .5f) * QixConfig.SubCell, (game.PlayerY + .5f) * QixConfig.SubCell);
            _player.transform.localScale = Vector3.one * (_playerSizeCells * CellSize);
            _player.color = game.IsDrawing ? _playerDrawing : _playerIdle;

            var enemy = game.Enemy;
            _enemy.enabled = true;
            _enemy.transform.localPosition = Local(enemy.X, enemy.Y);
            _enemy.transform.localScale = Vector3.one * (2f * enemy.Radius / QixConfig.SubCell * CellSize);
            _enemy.transform.localRotation = Quaternion.Euler(0, 0, -(game.Ticks % 360 * _enemySpinDegreesPerTick % 360));
        }

        // 1/SubCell セル単位の盤の座標を、盤の中心が原点のローカル座標へ。
        private Vector3 Local(float subX, float subY) => new(
            ((subX / QixConfig.SubCell) - (Width / 2f)) * CellSize,
            ((subY / QixConfig.SubCell) - (Height / 2f)) * CellSize,
            0);

        private void Ensure(int width, int height)
        {
            if (_texture != null && width == Width && height == Height) { return; }
            Release();
            Width = width;
            Height = height;
            _texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "TideClaimField",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            _pixels = new Color32[width * height];
            _sprite = Sprite.Create(_texture, new Rect(0, 0, width, height), new Vector2(.5f, .5f), 1f / CellSize);
            _sprite.name = "TideClaimField";
            _field.sprite = _sprite;
            _showsInitial = false;
            _shownGame = null;
        }

        private void Upload()
        {
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
            Uploads++;
        }

        private void Release()
        {
            if (_sprite != null) { Destroy(_sprite); }
            if (_texture != null) { Destroy(_texture); }
            _sprite = null;
            _texture = null;
        }

        private void OnDestroy() => Release();
    }
}
