using System;
using System.Collections.Generic;
using Game.Core;
using TileMotion;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Game.Unity
{
    /// <summary>
    /// 1 部屋の描画。地形・ひびの壁・扉は Tilemap、セーブ像・アイテム・敵・ボス・自機・弾は SpriteRenderer で、
    /// どれも生成スクリプトが Core の地図から Prefab に置く（プレイ前から Scene ビューで見える）。
    /// 1 タイル = 1 ワールド単位。Tick の状態をそのまま描き、補間しない。点滅も Tick で決める。
    /// </summary>
    public sealed class MossDepthsStage : MonoBehaviour
    {
        /// <summary>歩く姿の 1 コマの Tick 数。</summary>
        public const int FrameTicks = 8;

        /// <summary>無敵中の点滅の 1 段の Tick 数。</summary>
        public const int BlinkTicks = 4;

        [SerializeField] private int _roomIndex;
        [SerializeField] private Tilemap _ground;
        [SerializeField] private Tilemap _cracked;
        [SerializeField] private Tilemap _doors;
        [SerializeField] private TileBase _crackedTile;
        [SerializeField] private SpriteRenderer _player;
        [SerializeField] private Sprite _stand;
        [SerializeField] private Sprite[] _run = Array.Empty<Sprite>();
        [SerializeField] private Sprite _rise;
        [SerializeField] private Sprite _fall;
        [SerializeField] private Sprite _hurt;
        [SerializeField] private SpriteRenderer[] _statues = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer[] _pickups = Array.Empty<SpriteRenderer>();
        [SerializeField] private SpriteRenderer[] _walkers = Array.Empty<SpriteRenderer>();
        [SerializeField] private Sprite[] _walk = Array.Empty<Sprite>();
        [SerializeField] private SpriteRenderer _boss;
        [SerializeField] private Sprite _bossIdle;
        [SerializeField] private Sprite _bossCharge;
        [SerializeField] private Sprite _bossStunned;
        [SerializeField] private SpriteRenderer[] _shots = Array.Empty<SpriteRenderer>();
        [SerializeField] private Sprite _shot;
        [SerializeField] private Sprite _breakShot;
        [SerializeField] private SpriteRenderer _bulletTemplate;

        /// <summary>弾が当たった直後の敵の色。</summary>
        [SerializeField] private Color _flashTint = new(1f, .45f, .45f, 1f);

        // 弾は数が設定で変わるので、足りなければ見本から増やす（この Stage が持ち、壊すときに一緒に消える）。
        private readonly List<SpriteRenderer> _bullets = new();

        public int RoomIndex => _roomIndex;

        public SpriteRenderer Player => _player;

        public Tilemap Cracked => _cracked;

        public SpriteRenderer Boss => _boss;

        public IReadOnlyList<SpriteRenderer> Pickups => _pickups;

        public IReadOnlyList<SpriteRenderer> Walkers => _walkers;

        public IReadOnlyList<SpriteRenderer> Shots => _shots;

        public Sprite StandSprite => _stand;

        public Sprite HurtSprite => _hurt;

        /// <summary>今表示している自機の弾・ボスの弾の数。</summary>
        public int VisibleShots { get; private set; }

        public int VisibleBullets { get; private set; }

        /// <summary>Core の単位の位置をワールド座標へ。</summary>
        public Vector3 World(int x, int y) => transform.TransformPoint(new Vector3((float)x / Units.Tile, (float)y / Units.Tile, 0));

        /// <summary>地形・ひびの壁・扉と置かれた物の数が Core の部屋と一致するか。違えば理由、一致すれば null。</summary>
        public string Mismatch(Room room)
        {
            if (room == null) { throw new ArgumentNullException(nameof(room)); }
            var map = room.Map;
            for (var row = -1; row <= map.Height; row++)
            {
                for (var column = -1; column <= map.Width; column++)
                {
                    var inside = map.Contains(column, row);
                    var kind = inside ? map[column, row] : TileKind.Empty;
                    var cell = new Vector3Int(column, row, 0);
                    if ((kind == TileKind.Solid) != _ground.HasTile(cell)) { return $"固体 ({column}, {row}) が地図と違います。"; }
                    if ((kind == TileKind.Cracked) != _cracked.HasTile(cell)) { return $"ひびの壁 ({column}, {row}) が地図と違います。"; }
                    if ((kind == TileKind.Door) != _doors.HasTile(cell)) { return $"扉 ({column}, {row}) が地図と違います。"; }
                }
            }

            if (_statues.Length != room.Statues.Count) { return "セーブ像の数が地図と違います。"; }
            if (_pickups.Length != room.Pickups.Count) { return "アイテムの数が地図と違います。"; }
            if (_walkers.Length != room.Walkers.Count) { return "歩く敵の数が地図と違います。"; }
            if ((_boss != null) != room.Boss.HasValue) { return "ボスの有無が地図と違います。"; }
            return null;
        }

        /// <summary>
        /// 試合の前の姿を描く（タイトル）。Core の部屋の初期配置から作り直すので、遊んだ後にタイトルへ戻っても
        /// 前の試合の状態（位置・姿・壊した壁・取ったアイテム）は残らない。
        /// </summary>
        public void RenderInitial(Room room)
        {
            if (room == null) { throw new ArgumentNullException(nameof(room)); }
            SyncCracked(room, (_, _) => false);
            for (var i = 0; i < _pickups.Length; i++) { _pickups[i].enabled = true; }
            for (var i = 0; i < _walkers.Length; i++)
            {
                var spawn = Room.PlaceOnTile(room.Walkers[i], Walker.Width, Walker.Height);
                DrawWalker(_walkers[i], spawn, -1, 0, true, 0);
            }

            if (_boss != null)
            {
                var spawn = Room.PlaceOnTile(room.Boss!.Value, Core.Boss.Width, Core.Boss.Height);
                DrawBoss(spawn, -1, BossPhase.Idle, 0, true);
            }

            _player.enabled = room.Start.HasValue;
            _player.sprite = _stand;
            _player.flipX = false;
            _player.color = Color.white;
            if (room.Start is Cell start) { Place(_player, Room.PlaceOnTile(start, Core.Player.Width, Core.Player.Height)); }
            HideShots(0);
            HideBullets(0);
        }

        public void Render(Expedition expedition)
        {
            if (expedition == null) { throw new ArgumentNullException(nameof(expedition)); }
            var run = expedition.Run;
            if (run.RoomIndex != _roomIndex) { throw new ArgumentException($"{run.RoomIndex} 部屋目の進行を {_roomIndex} 部屋目の Stage で描こうとしました。", nameof(expedition)); }
            var tick = expedition.ElapsedTicks;
            var state = expedition.State;
            SyncCracked(run.Room, (column, row) => state.IsBroken(_roomIndex, column, row, run.Room.Map));
            for (var i = 0; i < _pickups.Length; i++) { _pickups[i].enabled = run.IsPickupPresent(i); }
            for (var i = 0; i < _walkers.Length; i++)
            {
                var walker = run.Walkers[i];
                DrawWalker(_walkers[i], walker.Body, walker.Facing, walker.FlashRemaining, walker.IsAlive, tick);
            }

            if (_boss != null && run.Boss != null)
            {
                var boss = run.Boss;
                DrawBoss(boss.Body, boss.Facing, boss.Phase, boss.FlashRemaining, boss.IsAlive);
            }

            var player = expedition.Player;
            Place(_player, player.Body);
            _player.flipX = player.Facing < 0;
            _player.color = Color.white;
            _player.enabled = player.InvincibleRemaining <= 0 || player.IsDown || player.InvincibleRemaining / BlinkTicks % 2 == 0;
            _player.sprite = player.IsDown || player.KnockbackRemaining > 0 ? _hurt
                : !player.Grounded ? (player.VelocityY > 0 ? _rise : _fall)
                : player.VelocityX != 0 ? _run[(int)(tick / FrameTicks % _run.Length)]
                : _stand;

            for (var i = 0; i < run.Shots.Count && i < _shots.Length; i++)
            {
                var shot = run.Shots[i];
                _shots[i].enabled = true;
                _shots[i].sprite = shot.Breaking ? _breakShot : _shot;
                _shots[i].flipX = shot.Direction < 0;
                PlaceCenter(_shots[i], shot.Body);
            }

            HideShots(Math.Min(run.Shots.Count, _shots.Length));
            for (var i = 0; i < run.Bullets.Count; i++)
            {
                var bullet = Bullet(i);
                bullet.enabled = true;
                PlaceCenter(bullet, run.Bullets[i].Body);
            }

            HideBullets(run.Bullets.Count);
        }

        /// <summary>
        /// ひびの壁のタイルを、壊れているかに合わせる（壊れていれば消し、セーブからの再開などで戻っていれば置き直す）。
        /// 変わったタイルだけを書き換える。
        /// </summary>
        private void SyncCracked(Room room, Func<int, int, bool> isBroken)
        {
            var map = room.Map;
            for (var row = 0; row < map.Height; row++)
            {
                for (var column = 0; column < map.Width; column++)
                {
                    if (map[column, row] != TileKind.Cracked) { continue; }
                    var broken = isBroken(column, row);
                    var cell = new Vector3Int(column, row, 0);
                    if (_cracked.HasTile(cell) != broken) { continue; }
                    _cracked.SetTile(cell, broken ? null : _crackedTile);
                }
            }
        }

        private void DrawWalker(SpriteRenderer renderer, Box body, int facing, int flash, bool alive, long tick)
        {
            renderer.enabled = alive;
            if (!alive) { return; }
            Place(renderer, body);
            renderer.flipX = facing > 0;
            renderer.sprite = _walk[(int)(tick / FrameTicks % _walk.Length)];
            renderer.color = flash > 0 ? _flashTint : Color.white;
        }

        private void DrawBoss(Box body, int facing, BossPhase phase, int flash, bool alive)
        {
            _boss.enabled = alive;
            if (!alive) { return; }
            Place(_boss, body);
            _boss.flipX = facing > 0;
            _boss.sprite = phase switch
            {
                BossPhase.Charge => _bossCharge,
                BossPhase.Stunned => _bossStunned,
                _ => _bossIdle,
            };
            _boss.color = flash > 0 ? _flashTint : Color.white;
        }

        private SpriteRenderer Bullet(int index)
        {
            while (_bullets.Count <= index)
            {
                var copy = Instantiate(_bulletTemplate, _bulletTemplate.transform.parent);
                copy.name = $"Bullet{_bullets.Count}";
                _bullets.Add(copy);
            }

            return _bullets[index];
        }

        private void HideShots(int from)
        {
            for (var i = from; i < _shots.Length; i++) { _shots[i].enabled = false; }
            VisibleShots = from;
        }

        private void HideBullets(int from)
        {
            if (_bulletTemplate != null) { _bulletTemplate.enabled = false; }
            for (var i = from; i < _bullets.Count; i++) { _bullets[i].enabled = false; }
            VisibleBullets = from;
        }

        /// <summary>体の箱の下端の中央に、スプライトの基準点を置く。</summary>
        private static void Place(SpriteRenderer renderer, Box body)
        {
            renderer.transform.localPosition = new Vector3((float)body.CenterX / Units.Tile, (float)body.Bottom / Units.Tile, 0);
        }

        /// <summary>箱の中央に、スプライトの基準点（中央）を置く。</summary>
        private static void PlaceCenter(SpriteRenderer renderer, Box body)
        {
            renderer.transform.localPosition = new Vector3((float)body.CenterX / Units.Tile, (body.Y + (body.Height / 2f)) / Units.Tile, 0);
        }

        private void OnDestroy()
        {
            foreach (var bullet in _bullets)
            {
                if (bullet != null) { Destroy(bullet.gameObject); }
            }
        }
    }
}
