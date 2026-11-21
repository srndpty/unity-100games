using System;
using Game.Core;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Game.Unity
{
    /// <summary>
    /// 1 面の描画。地形は Tilemap（生成スクリプトが Core の地図から作る）、自機と敵は SpriteRenderer。
    /// 敵の SpriteRenderer は Core の敵と同じ順（地図の出現順）に並べる。1 タイル = 1 ワールド単位で、
    /// 体の箱の下端の中央にスプライトの基準点を置く。Tick の状態をそのまま描き、補間しない。
    /// </summary>
    public sealed class BrambleDashStage : MonoBehaviour
    {
        /// <summary>踏まれた敵をつぶれた姿で残す Tick 数。</summary>
        public const int SquashTicks = 30;

        /// <summary>走る姿の 1 コマの Tick 数。</summary>
        public const int RunFrameTicks = 6;

        [SerializeField] private int _levelIndex;
        [SerializeField] private Tilemap _ground;
        [SerializeField] private SpriteRenderer _player;
        [SerializeField] private SpriteRenderer[] _enemies = Array.Empty<SpriteRenderer>();
        [SerializeField] private Sprite _playerStand;
        [SerializeField] private Sprite[] _playerRun = Array.Empty<Sprite>();
        [SerializeField] private Sprite _playerJump;
        [SerializeField] private Sprite _playerMiss;
        [SerializeField] private Sprite[] _walker = Array.Empty<Sprite>();
        [SerializeField] private Sprite _walkerSquashed;
        [SerializeField] private Sprite _hopper;
        [SerializeField] private Sprite _hopperAir;
        [SerializeField] private Sprite _hopperSquashed;
        [SerializeField] private Sprite[] _thorn = Array.Empty<Sprite>();

        public int LevelIndex => _levelIndex;

        public Tilemap Ground => _ground;

        public SpriteRenderer Player => _player;

        public int EnemyCount => _enemies.Length;

        public SpriteRenderer EnemyAt(int index) => _enemies[index];

        /// <summary>Core の単位の位置をワールド座標へ。</summary>
        public Vector3 World(int x, int y) => transform.TransformPoint(new Vector3((float)x / Units.Tile, (float)y / Units.Tile, 0));

        /// <summary>地形と敵の数が Core の面と一致するか。違えば理由、一致すれば null（生成スクリプトとテストが確かめる）。</summary>
        public string Mismatch(Level level)
        {
            if (level == null) { throw new ArgumentNullException(nameof(level)); }
            var map = level.Map;
            for (var row = -1; row <= map.Height; row++)
            {
                for (var column = -1; column <= map.Width; column++)
                {
                    var solid = column >= 0 && column < map.Width && map.IsSolid(column, row);
                    if (solid != _ground.HasTile(new Vector3Int(column, row, 0))) { return $"タイル ({column}, {row}) が地図と違います。"; }
                }
            }

            if (_enemies.Length != level.Enemies.Count) { return $"敵の数が違います（{_enemies.Length}／{level.Enemies.Count}）。"; }
            return null;
        }

        /// <summary>面を始める前の配置（Prefab に保存した姿）に戻す。</summary>
        public void RenderInitial()
        {
            _player.enabled = true;
            _player.sprite = _playerStand;
            _player.flipX = false;
            foreach (var enemy in _enemies) { enemy.enabled = true; }
        }

        public void Render(LevelRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            if (run.Enemies.Count != _enemies.Length)
            {
                throw new InvalidOperationException($"敵の数が Stage（{_enemies.Length}）と面（{run.Enemies.Count}）で違います。Stage Prefab を作り直してください。");
            }

            var player = run.Player;
            Place(_player, player.Body);
            _player.flipX = player.Facing < 0;
            _player.sprite = run.Status == RunStatus.Missed ? _playerMiss
                : !player.Grounded ? _playerJump
                : player.VelocityX != 0 ? _playerRun[(int)(run.Ticks / RunFrameTicks % _playerRun.Length)]
                : _playerStand;

            for (var i = 0; i < _enemies.Length; i++)
            {
                var enemy = run.Enemies[i];
                var renderer = _enemies[i];
                var squashed = enemy.State == EnemyState.Stomped && run.Ticks - enemy.EndedTick < SquashTicks;
                renderer.enabled = enemy.IsAlive || squashed;
                if (!renderer.enabled) { continue; }
                Place(renderer, enemy.Body);
                renderer.flipX = enemy.Facing > 0;
                renderer.sprite = EnemySprite(enemy, squashed, run.Ticks);
            }
        }

        private Sprite EnemySprite(Enemy enemy, bool squashed, long ticks)
        {
            var frame = (int)(ticks / (RunFrameTicks * 2) % 2);
            return enemy.Kind switch
            {
                EnemyKind.Walker => squashed ? _walkerSquashed : enemy.State == EnemyState.Waiting ? _walker[0] : _walker[frame % _walker.Length],
                EnemyKind.Hopper => squashed ? _hopperSquashed : enemy.VelocityY != 0 ? _hopperAir : _hopper,
                _ => _thorn[enemy.State == EnemyState.Waiting ? 0 : frame % _thorn.Length],
            };
        }

        private void Place(SpriteRenderer renderer, Box body)
        {
            renderer.transform.localPosition = new Vector3((float)body.CenterX / Units.Tile, (float)body.Bottom / Units.Tile, 0);
        }
    }
}
