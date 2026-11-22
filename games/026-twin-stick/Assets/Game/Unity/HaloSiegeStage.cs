using System;
using System.Collections.Generic;
using Game.Core;
using TileMotion;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Game.Unity
{
    /// <summary>
    /// アリーナの描画。床と外周の壁は Tilemap、自機・照準・敵・予告・突進の筋・弾は SpriteRenderer で、
    /// どれも生成スクリプトが Prefab に置く（プレイ前から Scene ビューで見える）。敵・予告・筋・弾は Prefab の見本を 1 つ目とし、
    /// 足りなければ見本から増やす（この Stage が持ち、壊すときに一緒に消える）。
    /// 1 セル = 1 ワールド単位で、Stage のローカル座標は Core の単位を <see cref="Units.Tile"/> で割った値。
    /// Tick の状態をそのまま描き、補間しない。点滅も Tick で決める。
    /// </summary>
    public sealed class HaloSiegeStage : MonoBehaviour
    {
        /// <summary>無敵中の点滅の 1 段の Tick 数。</summary>
        public const int BlinkTicks = 4;

        [SerializeField] private Tilemap _floor;
        [SerializeField] private Tilemap _walls;
        [SerializeField] private SpriteRenderer _ship;
        [SerializeField] private SpriteRenderer _crosshair;
        [SerializeField] private SpriteRenderer _enemyTemplate;
        [SerializeField] private SpriteRenderer _warningTemplate;
        [SerializeField] private SpriteRenderer _laneTemplate;
        [SerializeField] private SpriteRenderer _bulletTemplate;
        [SerializeField] private Sprite _chaser;
        [SerializeField] private Sprite _bouncer;
        [SerializeField] private Sprite _lancer;

        /// <summary>撃たれた直後の敵の色。</summary>
        [SerializeField] private Color _flashTint = new(1f, 1f, 1f, 1f);

        /// <summary>予告の色（<see cref="EnemyKind"/> の順）。</summary>
        [SerializeField] private Color[] _warningTints = { new(1f, .35f, .8f, 1f), new(.4f, 1f, .5f, 1f), new(1f, .6f, .2f, 1f) };

        private readonly List<SpriteRenderer> _enemies = new();
        private readonly List<SpriteRenderer> _warnings = new();
        private readonly List<SpriteRenderer> _lanes = new();
        private readonly List<SpriteRenderer> _bullets = new();

        public SpriteRenderer Ship => _ship;

        public SpriteRenderer Crosshair => _crosshair;

        public Tilemap Floor => _floor;

        /// <summary>今表示している敵（動いているもの）・予告・弾の数。</summary>
        public int VisibleEnemies { get; private set; }

        public int VisibleWarnings { get; private set; }

        public int VisibleBullets { get; private set; }

        public int VisibleLanes { get; private set; }

        /// <summary>今まで作った描画物の数（見本を含む。負荷の記録用）。</summary>
        public int PooledRenderers => _enemies.Count + _warnings.Count + _lanes.Count + _bullets.Count;

        /// <summary>Core の単位の位置を Stage のローカル座標へ。</summary>
        public static Vector3 Local(Vec2i point) => new((float)point.X / Units.Tile, (float)point.Y / Units.Tile, 0);

        /// <summary>Stage のローカル座標を Core の単位へ（四捨五入）。マウスの位置を照準点にするのに使う。</summary>
        public static Vec2i ToCore(Vector3 local) => new(Mathf.RoundToInt(local.x * Units.Tile), Mathf.RoundToInt(local.y * Units.Tile));

        /// <summary>向き（Core の速度）の角度（度）。</summary>
        public static float Angle(Vec2i direction) => direction.IsZero ? 0 : Mathf.Atan2(direction.Y, direction.X) * Mathf.Rad2Deg;

        public Sprite EnemySprite(EnemyKind kind) => kind switch
        {
            EnemyKind.Chaser => _chaser,
            EnemyKind.Bouncer => _bouncer,
            EnemyKind.Lancer => _lancer,
            _ => null,
        };

        /// <summary>床が設定のアリーナを覆い、外周の 1 セル外側だけが壁か。違えば理由、一致すれば null。</summary>
        public string Mismatch(SiegeConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            for (var row = -2; row <= config.ArenaHeightCells + 1; row++)
            {
                for (var column = -2; column <= config.ArenaWidthCells + 1; column++)
                {
                    var cell = new Vector3Int(column, row, 0);
                    var inside = column >= 0 && row >= 0 && column < config.ArenaWidthCells && row < config.ArenaHeightCells;
                    var ring = !inside && column >= -1 && row >= -1 && column <= config.ArenaWidthCells && row <= config.ArenaHeightCells;
                    if (inside != _floor.HasTile(cell)) { return $"床 ({column}, {row}) がアリーナと違います。"; }
                    if (ring != _walls.HasTile(cell)) { return $"壁 ({column}, {row}) がアリーナの外周と違います。"; }
                }
            }

            if (_ship == null || _crosshair == null || _enemyTemplate == null || _warningTemplate == null || _laneTemplate == null || _bulletTemplate == null)
            {
                return "描画物の参照が欠けています。";
            }

            return null;
        }

        /// <summary>試合の前の表示（自機をアリーナの中央に置き、敵・弾・照準を出さない）。</summary>
        public void RenderInitial(SiegeConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _ship.enabled = true;
            _ship.transform.localPosition = Local(new Vec2i(config.ArenaWidth / 2, config.ArenaHeight / 2));
            _ship.transform.localRotation = Quaternion.identity;
            _ship.color = Color.white;
            _crosshair.enabled = false;
            Hide(_enemies, _enemyTemplate, 0);
            Hide(_warnings, _warningTemplate, 0);
            Hide(_lanes, _laneTemplate, 0);
            Hide(_bullets, _bulletTemplate, 0);
            VisibleEnemies = VisibleWarnings = VisibleBullets = VisibleLanes = 0;
        }

        /// <summary>試合の状態を描く。<paramref name="aim"/> は照準カーソルの位置（Core の単位）。</summary>
        public void Render(SiegeRun run, Vec2i aim, bool showCrosshair, SiegeConfig config)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            var ship = run.Ship;
            _ship.transform.localPosition = Local(ship.Position);
            _ship.transform.localRotation = Quaternion.Euler(0, 0, Angle(ship.ShotVelocity));
            _ship.enabled = run.Status != RunStatus.Lost;
            var dim = ship.IsInvincible && (ship.InvincibleRemaining / BlinkTicks) % 2 == 1;
            _ship.color = dim ? new Color(1f, 1f, 1f, .3f) : Color.white;
            _crosshair.enabled = showCrosshair && !run.IsOver;
            _crosshair.transform.localPosition = Local(aim);

            int enemies = 0, warnings = 0, lanes = 0;
            foreach (var enemy in run.Enemies)
            {
                if (!enemy.IsActive)
                {
                    var marker = Take(_warnings, _warningTemplate, warnings++);
                    marker.transform.localPosition = Local(enemy.Position);
                    var pulse = 1f + (.2f * ((run.Elapsed / 6) % 2));
                    marker.transform.localScale = new Vector3(pulse, pulse, 1);
                    marker.color = _warningTints[(int)enemy.Kind];
                    continue;
                }

                var body = Take(_enemies, _enemyTemplate, enemies++);
                body.sprite = EnemySprite(enemy.Kind);
                body.transform.localPosition = Local(enemy.Position);
                body.color = enemy.IsFlashing ? _flashTint : Color.white;
                var angle = enemy.Kind == EnemyKind.Bouncer ? -run.Elapsed * 4 % 360 : Angle(enemy.Velocity);
                body.transform.localRotation = Quaternion.Euler(0, 0, angle);

                // 狙っている Lancer は、突進の通り道を筋で示す。
                if (enemy.Kind == EnemyKind.Lancer && enemy.Mode == LancerMode.Aim)
                {
                    var lane = Take(_lanes, _laneTemplate, lanes++);
                    var length = (float)config.LancerDashSpeed * config.LancerDashTicks / Units.Tile;
                    var direction = new Vector3(enemy.Velocity.X, enemy.Velocity.Y, 0).normalized;
                    lane.transform.localPosition = Local(enemy.Position) + (direction * (length / 2));
                    lane.transform.localRotation = Quaternion.Euler(0, 0, angle);
                    lane.transform.localScale = new Vector3(length, 1, 1);
                }
            }

            var bullets = 0;
            foreach (var bullet in run.Bullets)
            {
                var item = Take(_bullets, _bulletTemplate, bullets++);
                item.transform.localPosition = Local(bullet.Position);
                item.transform.localRotation = Quaternion.Euler(0, 0, Angle(bullet.Velocity));
            }

            Hide(_enemies, _enemyTemplate, enemies);
            Hide(_warnings, _warningTemplate, warnings);
            Hide(_lanes, _laneTemplate, lanes);
            Hide(_bullets, _bulletTemplate, bullets);
            VisibleEnemies = enemies;
            VisibleWarnings = warnings;
            VisibleLanes = lanes;
            VisibleBullets = bullets;
        }

        /// <summary><paramref name="index"/> 番目の描画物（なければ見本から増やす）を出す。</summary>
        private static SpriteRenderer Take(List<SpriteRenderer> pool, SpriteRenderer template, int index)
        {
            if (pool.Count == 0) { pool.Add(template); }
            while (pool.Count <= index) { pool.Add(Instantiate(template, template.transform.parent)); }
            var item = pool[index];
            item.enabled = true;
            return item;
        }

        private static void Hide(List<SpriteRenderer> pool, SpriteRenderer template, int from)
        {
            if (pool.Count == 0) { template.enabled = false; }
            for (var i = from; i < pool.Count; i++) { pool[i].enabled = false; }
        }
    }
}
