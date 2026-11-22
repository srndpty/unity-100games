using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TileMotion;

namespace Game.Core
{
    /// <summary>1 Tick の中で起きたこと。</summary>
    [Flags]
    public enum DungeonEvents
    {
        None = 0,
        Swung = 1 << 0,
        Rolled = 1 << 1,
        EnemyHit = 1 << 2,
        EnemyDefeated = 1 << 3,
        BossHit = 1 << 4,
        BossDefeated = 1 << 5,
        Hurt = 1 << 6,
        DoorOpened = 1 << 7,

        /// <summary>鍵の扉に押し当てたが、合う鍵を持っていなかった。</summary>
        DoorLocked = 1 << 8,
        ChestOpened = 1 << 9,
        RewardShown = 1 << 10,
        KeyTaken = 1 << 11,
        RoomChanged = 1 << 12,
        Arrived = 1 << 13,
        Down = 1 << 14,
        Continued = 1 << 15,
        Won = 1 << 16,
    }

    /// <summary>1 Tick の結果、部屋の外で処理すること。</summary>
    public enum RoomOutcome
    {
        None,

        /// <summary>出入口に触れた（向きは <see cref="RoomRun.ExitSide"/>）。</summary>
        Exit,

        /// <summary>体力が 0 になった。</summary>
        Down,

        /// <summary>秘宝に触れた。</summary>
        Won,
    }

    /// <summary>
    /// 今いる部屋の進行。敵・弾はこの部屋の中だけの状態で、部屋に入るたびに初めから作る。
    /// 自機とダンジョンの状態は試合（<see cref="Dungeon"/>）のものを共有して書き換える。
    /// <para>
    /// 1 Tick の順序：自機の移動 → 鍵の扉への押し当て → 剣のキー（前に閉じた宝箱があれば開け、なければ振る）→ 剣の当たり（敵は Tick の始めの位置）→
    /// 全滅の小鍵 → 敵の移動（弾を作る）→ 弾の移動 → 小鍵・秘宝を取る（自機の経路で。秘宝ならここで勝ち）→
    /// 被弾（相対軌跡で最初に触れた 1 つだけ。無敵なら受けない）→ 無敵の残りを減らす → 体力 0 なら倒れる → 出入口（自機の経路で）。
    /// 致死の被弾と出入口が同じ Tick なら、倒れるほうが先（部屋は移らない）。
    /// </para>
    /// </summary>
    public sealed class RoomRun
    {
        private readonly VaultConfig _config;
        private readonly DungeonState _state;
        private readonly List<Enemy> _enemies;
        private readonly List<Projectile> _projectiles;
        private readonly SeededRandom _random;
        private readonly bool[] _open;
        private DungeonEvents _events;

        public RoomRun(VaultConfig config, DungeonState state, int roomIndex, Hero hero, int seed)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            Hero = hero ?? throw new ArgumentNullException(nameof(hero));
            var world = state.World;
            if (roomIndex < 0 || roomIndex >= world.Rooms.Count) { throw new ArgumentOutOfRangeException(nameof(roomIndex), roomIndex, "ダンジョンにない部屋です。"); }
            RoomIndex = roomIndex;
            Room = world.Rooms[roomIndex];
            _open = state.OpenSides(roomIndex);
            Terrain = new Terrain(Room.Map, _open);
            _random = new SeededRandom(seed);
            _enemies = new List<Enemy>();
            foreach (var spawn in Room.Spawns)
            {
                // 倒したボスは戻らない（部屋に入り直しても、ゲームオーバーから再開しても）。
                if (spawn.Kind == EnemyKind.Boss && state.BossDefeated) { continue; }
                _enemies.Add(Enemy.At(spawn, config));
            }

            _projectiles = new List<Projectile>();
            Enemies = _enemies.AsReadOnly();
            Projectiles = _projectiles.AsReadOnly();
        }

        private RoomRun(RoomRun source, DungeonState state, Hero hero)
        {
            _config = source._config;
            _state = state;
            Hero = hero;
            RoomIndex = source.RoomIndex;
            Room = source.Room;
            _open = (bool[])source._open.Clone();
            Terrain = new Terrain(Room.Map, _open);
            _random = source._random.Clone();
            _enemies = source._enemies.ConvertAll(enemy => enemy.Clone());
            _projectiles = source._projectiles.ConvertAll(projectile => projectile.Clone());
            Enemies = _enemies.AsReadOnly();
            Projectiles = _projectiles.AsReadOnly();
            ExitSide = source.ExitSide;
        }

        public int RoomIndex { get; }

        public Room Room { get; }

        public Terrain Terrain { get; }

        public Hero Hero { get; }

        /// <summary>この部屋の敵（倒れた敵も残る。<see cref="Enemy.IsAlive"/> で見分ける）。</summary>
        public ReadOnlyCollection<Enemy> Enemies { get; }

        public ReadOnlyCollection<Projectile> Projectiles { get; }

        /// <summary>直前の Tick で触れた出入口の辺（<see cref="RoomOutcome.Exit"/> のとき）。</summary>
        public Side ExitSide { get; private set; }

        /// <summary>この部屋のボス（いなければ・倒した後は null）。</summary>
        public Enemy? Boss
        {
            get
            {
                foreach (var enemy in _enemies)
                {
                    if (enemy.Kind == EnemyKind.Boss && enemy.IsAlive) { return enemy; }
                }

                return null;
            }
        }

        /// <summary>生きている敵の数。</summary>
        public int AliveCount
        {
            get
            {
                var count = 0;
                foreach (var enemy in _enemies) { if (enemy.IsAlive) { count++; } }
                return count;
            }
        }

        /// <summary>全滅の小鍵が出ていて、まだ取っていない場所。</summary>
        public Cell? RewardCell => Room.Reward is Cell cell && _state.IsRewardShown(RoomIndex) && !_state.IsRewardTaken(RoomIndex) ? cell : (Cell?)null;

        /// <summary>秘宝が出ている場所（ボスを倒したボスの部屋）。</summary>
        public Cell? TreasureCell => Room.Boss is Cell cell && _state.BossDefeated ? cell : (Cell?)null;

        /// <summary>試合の複製に合わせて、複製した状態と自機を使う複製を作る。</summary>
        internal RoomRun Clone(DungeonState state, Hero hero) => new(this, state, hero);

        internal DungeonEvents TakeEvents()
        {
            var events = _events;
            _events = DungeonEvents.None;
            return events;
        }

        public RoomOutcome Tick(HeroInput input)
        {
            var actions = Hero.Step(Terrain, input);
            if ((actions & HeroActions.Rolled) != 0) { _events |= DungeonEvents.Rolled; }
            PushDoors(input);
            if ((actions & HeroActions.AttackReady) != 0 && !TryOpenChest())
            {
                Hero.StartSwing();
                _events |= DungeonEvents.Swung;
            }

            StrikeEnemies();
            if (Room.Reward.HasValue && AliveCount == 0 && _state.ShowReward(RoomIndex)) { _events |= DungeonEvents.RewardShown; }

            foreach (var enemy in _enemies) { enemy.Step(Terrain, _config, Hero.Body, _random, _projectiles); }
            MoveProjectiles();

            var motion = Hero.LastMotion;
            if (RewardCell is Cell reward && motion.Touches(Room.ItemBox(reward)) && _state.TryTakeReward(RoomIndex))
            {
                _events |= DungeonEvents.KeyTaken;
            }

            if (TreasureCell is Cell treasure && motion.Touches(Room.ItemBox(treasure)))
            {
                _events |= DungeonEvents.Won;
                return RoomOutcome.Won;
            }

            TakeDamage(motion);
            Hero.EndTick();
            foreach (var enemy in _enemies) { enemy.EndTick(); }

            if (Hero.IsDown)
            {
                _events |= DungeonEvents.Down;
                return RoomOutcome.Down;
            }

            var exit = Terrain.PassageUnder(motion.HorizontalSweep) ?? Terrain.PassageUnder(motion.VerticalSweep);
            if (exit is Side side)
            {
                ExitSide = side;
                return RoomOutcome.Exit;
            }

            return RoomOutcome.None;
        }

        public void MixInto(ref StateHash hash)
        {
            hash.Mix(RoomIndex);
            hash.Mix(_random.State);
            foreach (var open in _open) { hash.Mix(open); }
            hash.Mix(_enemies.Count);
            foreach (var enemy in _enemies) { enemy.MixInto(ref hash); }
            hash.Mix(_projectiles.Count);
            foreach (var projectile in _projectiles) { projectile.MixInto(ref hash); }
        }

        /// <summary>歩く向きの 1 単位先に閉じた鍵の扉があれば開けようとする（押し当て）。</summary>
        private void PushDoors(HeroInput input)
        {
            if (Hero.IsRolling || Hero.IsKnockedBack) { return; }
            Push(input.X, 0);
            Push(0, input.Y);

            void Push(int dx, int dy)
            {
                if (dx == 0 && dy == 0) { return; }
                if (!(Terrain.ClosedDoorUnder(Hero.Body.Offset(dx, dy)) is Side side)) { return; }
                switch (_state.TryOpen(RoomIndex, side))
                {
                    case DoorResult.Opened:
                        _open[(int)side] = true;
                        _events |= DungeonEvents.DoorOpened;
                        break;
                    case DoorResult.NoKey:
                        _events |= DungeonEvents.DoorLocked;
                        break;
                }
            }
        }

        /// <summary>体の前（剣の箱）に閉じた宝箱があれば開ける。開けたら true。</summary>
        private bool TryOpenChest()
        {
            var front = SwordHitbox.For(Hero.Body, Hero.Facing, _config);
            for (var i = 0; i < Room.Chests.Count; i++)
            {
                if (_state.IsChestOpen(RoomIndex, i) || !front.Overlaps(Room.Chests[i].Cell.Whole)) { continue; }
                _state.TryOpenChest(RoomIndex, i);
                _events |= DungeonEvents.ChestOpened;
                return true;
            }

            return false;
        }

        private void StrikeEnemies()
        {
            if (!(Hero.Sword is Box sword)) { return; }
            foreach (var enemy in _enemies)
            {
                if (!enemy.IsAlive || !sword.Overlaps(enemy.Body)) { continue; }
                if (!enemy.Hit(Hero.SwingSerial, Hero.Facing, _config.SwordDamage, _config)) { continue; }
                var boss = enemy.Kind == EnemyKind.Boss;
                _events |= boss ? DungeonEvents.BossHit : DungeonEvents.EnemyHit;
                if (enemy.IsAlive) { continue; }
                _events |= DungeonEvents.EnemyDefeated;
                if (boss)
                {
                    _state.DefeatBoss();
                    _projectiles.Clear();
                    _events |= DungeonEvents.BossDefeated;
                }
            }
        }

        private void MoveProjectiles()
        {
            var bounds = new Box(0, 0, Room.Map.Width * Units.Tile, Room.Map.Height * Units.Tile);
            foreach (var projectile in _projectiles)
            {
                projectile.Move();

                // 固体に入った Tick も、入るまでの区間では自機に当たりうる。被弾の判定の後に消す。
                if (!projectile.Body.Overlaps(bounds) || SolidQuery.Overlaps(Terrain, projectile.Body)) { projectile.MarkGone(); }
            }
        }

        private void TakeDamage(Motion motion)
        {
            var best = Touch.None;
            var damage = 0;
            Box source = default;
            var hitProjectile = -1;
            foreach (var enemy in _enemies)
            {
                if (!enemy.IsAlive) { continue; }
                Consider(Sweep.Along(motion, enemy.Previous, enemy.Body), _config.ContactDamage, enemy.Body, -1);
            }

            for (var i = 0; i < _projectiles.Count; i++)
            {
                var projectile = _projectiles[i];
                Consider(Sweep.Along(motion, projectile.Previous, projectile.Body), _config.ShotDamage, projectile.Previous, i);
            }

            if (best.Hit && Hero.Hurt(damage, source.CenterX, Boxes.CenterY(source)))
            {
                _events |= DungeonEvents.Hurt;
                if (hitProjectile >= 0) { _projectiles[hitProjectile].MarkGone(); }
            }

            _projectiles.RemoveAll(projectile => projectile.IsGone);

            void Consider(Touch touch, int amount, Box box, int index)
            {
                if (!touch.IsEarlierThan(best)) { return; }
                best = touch;
                damage = amount;
                source = box;
                hitProjectile = index;
            }
        }
    }
}
