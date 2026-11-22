using System;
using System.Collections.Generic;
using Game.Core;
using TileMotion;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Game.Unity
{
    /// <summary>
    /// 1 部屋の描画。床・壁・鍵の扉は Tilemap、宝箱・全滅の小鍵・秘宝・敵・自機・剣・弾は SpriteRenderer で、
    /// どれも生成スクリプトが Core の地図から Prefab に置く（プレイ前から Scene ビューで見える）。
    /// 1 タイル = 1 ワールド単位で、Stage のローカル座標は Core の単位を <see cref="Units.Tile"/> で割った値。
    /// Tick の状態をそのまま描き、補間しない。点滅も Tick で決める。
    /// </summary>
    public sealed class EmberVaultStage : MonoBehaviour
    {
        /// <summary>歩く姿の 1 コマの Tick 数。</summary>
        public const int FrameTicks = 8;

        /// <summary>無敵中の点滅の 1 段の Tick 数。</summary>
        public const int BlinkTicks = 4;

        [SerializeField] private int _roomIndex;
        [SerializeField] private Tilemap _floor;
        [SerializeField] private Tilemap _walls;
        [SerializeField] private Tilemap _doors;
        [SerializeField] private TileBase _smallDoorTile;
        [SerializeField] private TileBase _bossDoorTile;
        [SerializeField] private SpriteRenderer[] _chests = Array.Empty<SpriteRenderer>();

        /// <summary>閉じた宝箱の絵（<see cref="ChestContent"/> の順）。</summary>
        [SerializeField] private Sprite[] _chestClosed = Array.Empty<Sprite>();
        [SerializeField] private Sprite _chestOpen;
        [SerializeField] private SpriteRenderer _reward;
        [SerializeField] private SpriteRenderer _treasure;

        /// <summary>出現位置の順（<see cref="Room.Spawns"/>）の敵。</summary>
        [SerializeField] private SpriteRenderer[] _enemies = Array.Empty<SpriteRenderer>();
        [SerializeField] private Sprite[] _slime = Array.Empty<Sprite>();
        [SerializeField] private Sprite _spitter;
        [SerializeField] private Sprite _charger;
        [SerializeField] private Sprite _chargerCharge;
        [SerializeField] private Sprite _boss;
        [SerializeField] private Sprite _bossCharge;
        [SerializeField] private Sprite _bossStun;
        [SerializeField] private SpriteRenderer _hero;
        [SerializeField] private Sprite[] _heroDown = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _heroUp = Array.Empty<Sprite>();
        [SerializeField] private Sprite[] _heroSide = Array.Empty<Sprite>();
        [SerializeField] private Sprite _heroRoll;
        [SerializeField] private Sprite _heroHurt;
        [SerializeField] private SpriteRenderer _sword;
        [SerializeField] private SpriteRenderer _shotTemplate;

        /// <summary>剣が当たった直後の敵の色。</summary>
        [SerializeField] private Color _flashTint = new(1f, .45f, .45f, 1f);

        /// <summary>突進の構えの色。</summary>
        [SerializeField] private Color _windupTint = new(1f, .85f, .5f, 1f);

        // 弾は数が決まらないので、足りなければ見本から増やす（この Stage が持ち、壊すときに一緒に消える）。
        private readonly List<SpriteRenderer> _shots = new();

        public int RoomIndex => _roomIndex;

        public SpriteRenderer Hero => _hero;

        public SpriteRenderer Sword => _sword;

        public Tilemap Doors => _doors;

        public IReadOnlyList<SpriteRenderer> Chests => _chests;

        public IReadOnlyList<SpriteRenderer> Enemies => _enemies;

        public SpriteRenderer Reward => _reward;

        public SpriteRenderer Treasure => _treasure;

        public Sprite ChestOpenSprite => _chestOpen;

        /// <summary>今表示している弾の数。</summary>
        public int VisibleShots { get; private set; }

        /// <summary>Core の単位の位置を Stage のローカル座標へ。</summary>
        public static Vector3 Local(float x, float y) => new(x / Units.Tile, y / Units.Tile, 0);

        /// <summary>箱の中央（Stage のローカル座標）。描画と Prefab の照合・生成で共有する。</summary>
        public static Vector3 Center(Box box) => Local(box.X + (box.Width / 2f), box.Y + (box.Height / 2f));

        /// <summary>マスの中央（Stage のローカル座標）。</summary>
        public static Vector3 CellCenter(Cell cell) => new(cell.Column + .5f, cell.Row + .5f, 0);

        /// <summary>Core の単位の位置をワールド座標へ。</summary>
        public Vector3 World(Box box) => transform.TransformPoint(Center(box));

        /// <summary>種類の最初の絵（Prefab の照合と初期表示で使う）。</summary>
        public Sprite EnemySprite(EnemyKind kind) => kind switch
        {
            EnemyKind.Slime => _slime.Length > 0 ? _slime[0] : null,
            EnemyKind.Spitter => _spitter,
            EnemyKind.Charger => _charger,
            EnemyKind.Boss => _boss,
            _ => null,
        };

        public Sprite ChestSprite(ChestContent content)
        {
            var index = (int)content;
            return index >= 0 && index < _chestClosed.Length ? _chestClosed[index] : null;
        }

        /// <summary>床・壁・扉のタイルと、置かれた物の種類・位置が Core の部屋と一致するか。違えば理由、一致すれば null。</summary>
        public string Mismatch(Room room)
        {
            if (room == null) { throw new ArgumentNullException(nameof(room)); }
            var map = room.Map;
            for (var row = -1; row <= map.Height; row++)
            {
                for (var column = -1; column <= map.Width; column++)
                {
                    var inside = map.Contains(column, row);
                    var kind = inside ? map[column, row] : TileKind.Floor;
                    var cell = new Vector3Int(column, row, 0);
                    if (inside != _floor.HasTile(cell)) { return $"床 ({column}, {row}) が地図と違います。"; }
                    if ((inside && kind == TileKind.Wall) != _walls.HasTile(cell)) { return $"壁 ({column}, {row}) が地図と違います。"; }
                    var door = kind == TileKind.SmallDoor ? _smallDoorTile : kind == TileKind.BossDoor ? _bossDoorTile : null;
                    if (_doors.GetTile(cell) != door) { return $"扉 ({column}, {row}) が地図と違います。"; }
                }
            }

            // 置かれた物は数だけでなく、種類・位置も Core の地図から期待値を出して比べる（023 の知見）。
            if (_chests.Length != room.Chests.Count) { return "宝箱の数が地図と違います。"; }
            for (var i = 0; i < _chests.Length; i++)
            {
                var chest = room.Chests[i];
                if (!At(_chests[i], CellCenter(chest.Cell))) { return $"宝箱 {i} の位置が地図の {chest.Cell} と違います。"; }
                var sprite = ChestSprite(chest.Content);
                if (sprite == null || _chests[i].sprite != sprite) { return $"宝箱 {i} の絵が中身（{chest.Content}）と違います。"; }
            }

            if (_enemies.Length != room.Spawns.Count) { return "敵の数が地図と違います。"; }
            for (var i = 0; i < _enemies.Length; i++)
            {
                var spawn = room.Spawns[i];
                if (!At(_enemies[i], Center(spawn.Cell.Centered(Enemy.SizeOf(spawn.Kind))))) { return $"敵 {i} の位置が地図の {spawn.Cell} と違います。"; }
                var sprite = EnemySprite(spawn.Kind);
                if (sprite == null || _enemies[i].sprite != sprite) { return $"敵 {i} の絵が種類（{spawn.Kind}）と違います。"; }
            }

            if ((_reward != null) != room.Reward.HasValue) { return "全滅の小鍵の有無が地図と違います。"; }
            if (room.Reward is Cell reward && !At(_reward, CellCenter(reward))) { return $"全滅の小鍵の位置が地図の {reward} と違います。"; }
            if ((_treasure != null) != room.Boss.HasValue) { return "秘宝の有無が地図と違います。"; }
            if (room.Boss is Cell boss && !At(_treasure, CellCenter(boss))) { return $"秘宝の位置が地図の {boss} と違います。"; }
            return null;
        }

        private static bool At(SpriteRenderer renderer, Vector3 expected) =>
            renderer != null && (renderer.transform.localPosition - expected).sqrMagnitude < 1e-6f;

        /// <summary>
        /// 試合の前の姿を描く（タイトル）。Core の部屋の初期配置から作り直すので、遊んだ後にタイトルへ戻っても
        /// 前の試合の状態（位置・姿・開いた扉・宝箱）は残らない。
        /// </summary>
        public void RenderInitial(Room room)
        {
            if (room == null) { throw new ArgumentNullException(nameof(room)); }
            SyncDoors(room, _ => false);
            for (var i = 0; i < _chests.Length; i++) { _chests[i].sprite = ChestSprite(room.Chests[i].Content); }
            if (_reward != null) { _reward.enabled = false; }
            if (_treasure != null) { _treasure.enabled = false; }
            for (var i = 0; i < _enemies.Length; i++)
            {
                var spawn = room.Spawns[i];
                var renderer = _enemies[i];
                renderer.enabled = true;
                renderer.sprite = EnemySprite(spawn.Kind);
                renderer.flipX = false;
                renderer.color = Color.white;
                renderer.transform.localPosition = Center(spawn.Cell.Centered(Enemy.SizeOf(spawn.Kind)));
            }

            _hero.enabled = room.Start.HasValue;
            _hero.sprite = _heroUp[0];
            _hero.flipX = false;
            _hero.color = Color.white;
            if (room.Start is Cell start) { _hero.transform.localPosition = Center(start.Centered(Core.Hero.Size)); }
            _sword.enabled = false;
            HideShots(0);
        }

        /// <summary>自機と敵のいない部屋を描く（切り替えで出ていく部屋）。扉・宝箱・小鍵・秘宝はダンジョンの状態のまま。</summary>
        public void RenderVacant(DungeonState state)
        {
            if (state == null) { throw new ArgumentNullException(nameof(state)); }
            var room = state.World.Rooms[_roomIndex];
            RenderFixtures(room, state, room.Reward.HasValue && state.IsRewardShown(_roomIndex) && !state.IsRewardTaken(_roomIndex), state.BossDefeated);
            foreach (var enemy in _enemies) { enemy.enabled = false; }
            _hero.enabled = false;
            _sword.enabled = false;
            HideShots(0);
        }

        public void Render(Dungeon dungeon)
        {
            if (dungeon == null) { throw new ArgumentNullException(nameof(dungeon)); }
            var run = dungeon.Run;
            if (run.RoomIndex != _roomIndex) { throw new ArgumentException($"{run.RoomIndex} 部屋目の進行を {_roomIndex} 部屋目の Stage で描こうとしました。", nameof(dungeon)); }
            var tick = dungeon.ElapsedTicks;
            var state = dungeon.State;
            RenderFixtures(run.Room, state, run.RewardCell.HasValue, run.TreasureCell.HasValue);

            // RoomRun は倒したボスを作らないので、出現位置の順に対応させながら飛ばす。
            var next = 0;
            for (var i = 0; i < _enemies.Length; i++)
            {
                if (run.Room.Spawns[i].Kind == EnemyKind.Boss && state.BossDefeated)
                {
                    _enemies[i].enabled = false;
                    continue;
                }

                DrawEnemy(_enemies[i], run.Enemies[next++], tick);
            }

            DrawHero(dungeon.Hero, tick);
            for (var i = 0; i < run.Projectiles.Count; i++)
            {
                var shot = Shot(i);
                shot.enabled = true;
                shot.transform.localPosition = Center(run.Projectiles[i].Body);
            }

            HideShots(run.Projectiles.Count);
        }

        private void RenderFixtures(Room room, DungeonState state, bool reward, bool treasure)
        {
            SyncDoors(room, side => state.IsOpen(_roomIndex, side));
            for (var i = 0; i < _chests.Length; i++)
            {
                _chests[i].sprite = state.IsChestOpen(_roomIndex, i) ? _chestOpen : ChestSprite(room.Chests[i].Content);
            }

            if (_reward != null) { _reward.enabled = reward; }
            if (_treasure != null) { _treasure.enabled = treasure; }
        }

        /// <summary>鍵の扉のタイルを、開いているかに合わせる（開いていれば消す）。変わったタイルだけを書き換える。</summary>
        private void SyncDoors(Room room, Func<Side, bool> isOpen)
        {
            var map = room.Map;
            for (var row = 0; row < map.Height; row++)
            {
                for (var column = 0; column < map.Width; column++)
                {
                    var kind = map[column, row];
                    if (kind != TileKind.SmallDoor && kind != TileKind.BossDoor) { continue; }
                    var cell = new Vector3Int(column, row, 0);
                    var tile = isOpen(map.DoorSideOf(column, row)!.Value) ? null : kind == TileKind.SmallDoor ? _smallDoorTile : _bossDoorTile;
                    if (_doors.GetTile(cell) != tile) { _doors.SetTile(cell, tile); }
                }
            }
        }

        private void DrawEnemy(SpriteRenderer renderer, Enemy enemy, long tick)
        {
            renderer.enabled = enemy.IsAlive;
            if (!enemy.IsAlive) { return; }
            renderer.transform.localPosition = Center(enemy.Body);
            renderer.flipX = enemy.Facing == Facing.Left;
            renderer.sprite = enemy.Kind switch
            {
                EnemyKind.Slime => _slime[(int)(tick / FrameTicks % _slime.Length)],
                EnemyKind.Spitter => _spitter,
                EnemyKind.Charger => enemy.Phase == EnemyPhase.Charge ? _chargerCharge : _charger,
                _ => enemy.Phase == EnemyPhase.Charge ? _bossCharge : enemy.Phase == EnemyPhase.Stun ? _bossStun : _boss,
            };
            renderer.color = enemy.FlashRemaining > 0 ? _flashTint : enemy.Phase == EnemyPhase.Windup ? _windupTint : Color.white;
        }

        private void DrawHero(Core.Hero hero, long tick)
        {
            _hero.transform.localPosition = Center(hero.Body);
            var moving = hero.LastMotion.Start != hero.LastMotion.End;
            var frame = moving ? (int)(tick / FrameTicks % 2) : 0;
            _hero.flipX = hero.Facing == Facing.Left;
            _hero.sprite = hero.IsDown || hero.IsKnockedBack ? _heroHurt
                : hero.IsRolling ? _heroRoll
                : hero.Facing switch
                {
                    Facing.Up => _heroUp[frame],
                    Facing.Down => _heroDown[frame],
                    _ => _heroSide[frame],
                };

            // 被弾の後の無敵は点滅（Tick で決める）。ロールの無敵は少し透ける。
            _hero.enabled = hero.InvulnRemaining <= 0 || hero.IsDown || hero.InvulnRemaining / BlinkTicks % 2 == 0;
            _hero.color = hero.IsRolling && hero.IsInvulnerable && hero.InvulnRemaining <= 0 ? new Color(1, 1, 1, .7f) : Color.white;

            // 剣は振っている間ずっと出し、当たる Tick だけ不透明にする。位置は当たりの箱の中央（判定と同じ関数）。
            _sword.enabled = hero.IsSwinging;
            if (hero.IsSwinging)
            {
                _sword.transform.localPosition = Center(hero.SwordFacing(hero.Facing));
                _sword.transform.localRotation = Quaternion.Euler(0, 0, hero.Facing switch
                {
                    Facing.Up => 90,
                    Facing.Left => 180,
                    Facing.Down => 270,
                    _ => 0,
                });
                _sword.color = hero.IsSwordActive ? Color.white : new Color(1, 1, 1, .35f);
            }
        }

        private SpriteRenderer Shot(int index)
        {
            while (_shots.Count <= index)
            {
                var copy = Instantiate(_shotTemplate, _shotTemplate.transform.parent);
                copy.name = $"Shot{_shots.Count}";
                _shots.Add(copy);
            }

            return _shots[index];
        }

        private void HideShots(int from)
        {
            if (_shotTemplate != null) { _shotTemplate.enabled = false; }
            for (var i = from; i < _shots.Count; i++) { _shots[i].enabled = false; }
            VisibleShots = from;
        }

        private void OnDestroy()
        {
            foreach (var shot in _shots)
            {
                if (shot != null) { Destroy(shot.gameObject); }
            }
        }
    }
}
