using System;
using Game.Core;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Game.Unity
{
    /// <summary>
    /// 1 部屋の描画。地形・トゲ・出口は Tilemap（生成スクリプトが Core の地図から作る）、自機は SpriteRenderer。
    /// 1 タイル = 1 ワールド単位で、体の箱の下端の中央にスプライトの基準点を置く。Tick の状態をそのまま描き、補間しない。
    /// </summary>
    public sealed class GlintAscentStage : MonoBehaviour
    {
        /// <summary>走る姿の 1 コマの Tick 数。</summary>
        public const int RunFrameTicks = 6;

        [SerializeField] private int _roomIndex;
        [SerializeField] private Tilemap _ground;
        [SerializeField] private Tilemap _spikes;
        [SerializeField] private Tilemap _exit;
        [SerializeField] private SpriteRenderer _player;
        [SerializeField] private Sprite _stand;
        [SerializeField] private Sprite[] _run = Array.Empty<Sprite>();
        [SerializeField] private Sprite _rise;
        [SerializeField] private Sprite _fall;
        [SerializeField] private Sprite _dash;
        [SerializeField] private Sprite _slide;
        [SerializeField] private Sprite _dead;

        /// <summary>ダッシュを使い切っている間の自機の色（接地で回復すると白に戻る）。</summary>
        [SerializeField] private Color _spentTint = new(.55f, .75f, 1f, 1f);

        public int RoomIndex => _roomIndex;

        public SpriteRenderer Player => _player;

        public Color SpentTint => _spentTint;

        public Sprite StandSprite => _stand;

        public Sprite DeadSprite => _dead;

        public Sprite DashSprite => _dash;

        /// <summary>Core の単位の位置をワールド座標へ。</summary>
        public Vector3 World(int x, int y) => transform.TransformPoint(new Vector3((float)x / Units.Tile, (float)y / Units.Tile, 0));

        /// <summary>地形・トゲ・出口が Core の部屋と一致するか。違えば理由、一致すれば null（生成スクリプトとテストが確かめる）。</summary>
        public string Mismatch(Room room)
        {
            if (room == null) { throw new ArgumentNullException(nameof(room)); }
            var map = room.Map;
            for (var row = -1; row <= map.Height; row++)
            {
                for (var column = -1; column <= map.Width; column++)
                {
                    var inside = column >= 0 && column < map.Width && row >= 0 && row < map.Height;
                    var kind = inside ? map[column, row] : TileKind.Empty;
                    var cell = new Vector3Int(column, row, 0);
                    if ((kind == TileKind.Solid) != _ground.HasTile(cell)) { return $"固体 ({column}, {row}) が地図と違います。"; }
                    if ((kind == TileKind.Spike) != _spikes.HasTile(cell)) { return $"トゲ ({column}, {row}) が地図と違います。"; }
                    if ((kind == TileKind.Exit) != _exit.HasTile(cell)) { return $"出口 ({column}, {row}) が地図と違います。"; }
                }
            }

            return null;
        }

        /// <summary>
        /// 部屋を始める前の姿を描く。Core の部屋の開始位置から位置・姿・向き・色を作り直すので、
        /// 遊んだ後にタイトルへ戻っても前の挑戦の状態は残らない。
        /// </summary>
        public void RenderInitial(Room room)
        {
            if (room == null) { throw new ArgumentNullException(nameof(room)); }
            _player.enabled = true;
            _player.sprite = _stand;
            _player.flipX = false;
            _player.color = Color.white;
            Place(room.StartBox);
        }

        public void Render(RoomRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            var player = run.Player;
            Place(player.Body);
            _player.enabled = true;
            _player.flipX = player.Facing < 0;
            _player.color = player.DashesLeft > 0 || run.Status == RunStatus.Dying ? Color.white : _spentTint;
            _player.sprite = run.Status == RunStatus.Dying ? _dead
                : player.IsDashing ? _dash
                : player.WallSliding ? _slide
                : !player.Grounded ? (player.VelocityY > 0 ? _rise : _fall)
                : player.VelocityX != 0 ? _run[(int)(run.Ticks / RunFrameTicks % _run.Length)]
                : _stand;
        }

        private void Place(Box body)
        {
            _player.transform.localPosition = new Vector3((float)body.CenterX / Units.Tile, (float)body.Bottom / Units.Tile, 0);
        }
    }
}
