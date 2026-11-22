using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TileMotion;

namespace Game.Core
{
    /// <summary>1 Tick の中で起きたこと。</summary>
    [Flags]
    public enum ExpeditionEvents
    {
        None = 0,
        Jumped = 1 << 0,
        DoubleJumped = 1 << 1,
        Shot = 1 << 2,
        EnemyHit = 1 << 3,
        EnemyDefeated = 1 << 4,
        BossHit = 1 << 5,
        WallBroken = 1 << 6,
        PickedFruit = 1 << 7,
        GainedAbility = 1 << 8,
        Saved = 1 << 9,
        Hurt = 1 << 10,
        RoomChanged = 1 << 11,
        Down = 1 << 12,
        Continued = 1 << 13,
        Won = 1 << 14,
    }

    /// <summary>1 Tick の結果、部屋の外で処理すること。</summary>
    public enum RoomOutcome
    {
        None,

        /// <summary>扉に触れた（向きは <see cref="RoomRun.ExitSide"/>）。</summary>
        Door,

        /// <summary>体力が 0 になった。</summary>
        Down,

        /// <summary>ボスを倒した。</summary>
        Won,
    }

    /// <summary>
    /// 今いる部屋の進行。敵・弾・ボスはこの部屋の中だけの状態で、部屋に入るたびに初めから作る。
    /// 自機と世界の状態（能力・壊した壁・取ったアイテム）は試合（<see cref="Expedition"/>）のものを共有して書き換える。
    /// <para>
    /// 1 Tick の順序：自機の移動（撃てば弾を作る）→ 歩く敵・ボスの移動（ボスの弾を作る）→ 自機の弾（地形と、相対軌跡で敵・ボス。
    /// ボスを倒したらこの Tick で勝ち）→ ボスの弾の移動 → アイテム・セーブ像（自機の経路で）→ 被弾（相対軌跡で最初に触れた 1 つ）→
    /// 部屋の下への落下 → 扉（自機の経路で）。被弾で体力が 0 になれば扉は通らない。
    /// </para>
    /// </summary>
    public sealed class RoomRun
    {
        private readonly ExplorerConfig _config;
        private readonly World _world;
        private readonly WorldState _state;
        private readonly List<Walker> _walkers;
        private readonly List<Shot> _shots;
        private readonly List<Bullet> _bullets;
        private readonly Box[] _walkerStarts;
        private bool _onStatue;
        private ExpeditionEvents _events;

        public RoomRun(ExplorerConfig config, World world, WorldState state, int roomIndex, Player player)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            Player = player ?? throw new ArgumentNullException(nameof(player));
            if (roomIndex < 0 || roomIndex >= world.Rooms.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(roomIndex), roomIndex, "世界にない部屋です。");
            }

            RoomIndex = roomIndex;
            Room = world.Rooms[roomIndex];
            Terrain = new Terrain(Room.Map, state.BrokenIn(roomIndex));
            _walkers = new List<Walker>();
            foreach (var cell in Room.Walkers)
            {
                _walkers.Add(new Walker(Room.PlaceOnTile(cell, Walker.Width, Walker.Height), config.WalkerHealth));
            }

            _walkerStarts = new Box[_walkers.Count];
            if (Room.Boss is Cell boss)
            {
                Boss = new Boss(Room.PlaceOnTile(boss, Boss.Width, Boss.Height), config.BossHealth, config.BossIdleTicks);
            }

            _shots = new List<Shot>();
            _bullets = new List<Bullet>();
            Walkers = _walkers.AsReadOnly();
            Shots = _shots.AsReadOnly();
            Bullets = _bullets.AsReadOnly();

            // 置かれた位置がセーブ像の上なら、離れて触れ直すまで記録しない。
            _onStatue = TouchingStatue(Player.Body);
        }

        private RoomRun(RoomRun source, WorldState state, Player player)
        {
            _config = source._config;
            _world = source._world;
            _state = state;
            Player = player;
            RoomIndex = source.RoomIndex;
            Room = source.Room;
            Terrain = new Terrain(Room.Map, state.BrokenIn(RoomIndex));
            _walkers = source._walkers.ConvertAll(walker => walker.Clone());
            _walkerStarts = new Box[_walkers.Count];
            Boss = source.Boss?.Clone();
            _shots = source._shots.ConvertAll(shot => shot.Clone());
            _bullets = source._bullets.ConvertAll(bullet => bullet.Clone());
            Walkers = _walkers.AsReadOnly();
            Shots = _shots.AsReadOnly();
            Bullets = _bullets.AsReadOnly();
            _onStatue = source._onStatue;
            ExitSide = source.ExitSide;
        }

        public int RoomIndex { get; }

        public Room Room { get; }

        public Terrain Terrain { get; }

        public Player Player { get; }

        public ReadOnlyCollection<Walker> Walkers { get; }

        public Boss? Boss { get; }

        public ReadOnlyCollection<Shot> Shots { get; }

        public ReadOnlyCollection<Bullet> Bullets { get; }

        /// <summary>直前の Tick で触れた扉の辺（<see cref="RoomOutcome.Door"/> のとき）。</summary>
        public Side ExitSide { get; private set; }

        /// <summary>部屋のアイテムのうち、まだ取っていないものか。</summary>
        public bool IsPickupPresent(int index) => !_state.IsTaken(_world.PickupId(RoomIndex, index));

        /// <summary>試合の複製に合わせて、複製した世界の状態と自機を使う複製を作る。</summary>
        internal RoomRun Clone(WorldState state, Player player) => new(this, state, player);

        internal ExpeditionEvents TakeEvents()
        {
            var events = _events;
            _events = ExpeditionEvents.None;
            return events;
        }

        public RoomOutcome Tick(PlayerInput input)
        {
            var actions = Player.Step(Terrain, input, _state.Abilities, _shots.Count < _config.MaxShots);
            if ((actions & PlayerActions.Jumped) != 0) { _events |= ExpeditionEvents.Jumped; }
            if ((actions & PlayerActions.DoubleJumped) != 0) { _events |= ExpeditionEvents.DoubleJumped; }
            if ((actions & PlayerActions.Shot) != 0)
            {
                var spawn = Shot.SpawnBox(Player.Body, Player.Facing);
                _shots.Add(new Shot(spawn, Player.Facing, _config.ShotRangeTicks, _state.Has(Ability.BreakShot)));
                _events |= ExpeditionEvents.Shot;
            }

            for (var i = 0; i < _walkers.Count; i++)
            {
                _walkerStarts[i] = _walkers[i].Body;
                _walkers[i].Step(Terrain, _config);
            }

            var bossStart = Boss?.Body ?? default;
            Boss?.Step(Terrain, _config, Player.Body, _bullets);

            MoveShots(bossStart);
            if (Boss != null && !Boss.IsAlive)
            {
                _events |= ExpeditionEvents.Won;
                return RoomOutcome.Won;
            }

            MoveBullets();
            var motion = Player.LastMotion;
            CollectPickups(motion);
            CheckStatues(motion);
            TakeDamage(motion, bossStart);
            if (!HasDoor(Side.Bottom) && Player.Body.Top <= 0)
            {
                Player.Fall();
            }

            if (Player.IsDown)
            {
                _events |= ExpeditionEvents.Down;
                return RoomOutcome.Down;
            }

            var door = TouchedDoor(motion.HorizontalSweep) ?? TouchedDoor(motion.VerticalSweep);
            if (door is Side side)
            {
                ExitSide = side;
                return RoomOutcome.Door;
            }

            return RoomOutcome.None;
        }

        public void MixInto(ref StateHash hash)
        {
            hash.Mix(RoomIndex);
            hash.Mix(_onStatue);
            foreach (var walker in _walkers) { walker.MixInto(ref hash); }
            Boss?.MixInto(ref hash);
            hash.Mix(_shots.Count);
            foreach (var shot in _shots) { shot.MixInto(ref hash); }
            hash.Mix(_bullets.Count);
            foreach (var bullet in _bullets) { bullet.MixInto(ref hash); }
        }

        private bool HasDoor(Side side) => Room.HasDoor(side);

        private void MoveShots(Box bossStart)
        {
            for (var i = 0; i < _shots.Count;)
            {
                var shot = _shots[i];
                var start = shot.Body;
                var stopped = shot.Move(Terrain, _config.ShotSpeed, out var wallColumn);
                var end = shot.Body;

                // 地形で止まった位置までの軌跡で、最初に触れた敵（またはボス）に当てる。
                var best = Touch.None;
                var target = -1;
                for (var w = 0; w < _walkers.Count; w++)
                {
                    if (!_walkers[w].IsAlive) { continue; }
                    var touch = Sweep.First(start, end, _walkerStarts[w], _walkers[w].Body);
                    if (touch.IsEarlierThan(best))
                    {
                        best = touch;
                        target = w;
                    }
                }

                if (Boss != null && Boss.IsAlive)
                {
                    var touch = Sweep.First(start, end, bossStart, Boss.Body);
                    if (touch.IsEarlierThan(best))
                    {
                        best = touch;
                        target = _walkers.Count;
                    }
                }

                if (target >= 0)
                {
                    if (target == _walkers.Count)
                    {
                        Boss!.Hit();
                        _events |= ExpeditionEvents.BossHit;
                    }
                    else
                    {
                        _events |= _walkers[target].Hit() ? ExpeditionEvents.EnemyDefeated | ExpeditionEvents.EnemyHit : ExpeditionEvents.EnemyHit;
                    }

                    _shots.RemoveAt(i);
                    continue;
                }

                if (stopped)
                {
                    if (shot.Breaking && Terrain.BreakInColumn(wallColumn, end) > 0)
                    {
                        _events |= ExpeditionEvents.WallBroken;
                    }

                    _shots.RemoveAt(i);
                    continue;
                }

                if (shot.TicksLeft <= 0)
                {
                    _shots.RemoveAt(i);
                    continue;
                }

                i++;
            }
        }

        private void MoveBullets()
        {
            var bounds = new Box(0, 0, Room.Map.Width * Units.Tile, Room.Map.Height * Units.Tile);
            foreach (var bullet in _bullets)
            {
                bullet.Move();

                // 固体に入った Tick も、入るまでの区間では自機に当たりうる（自機は固体の中にいないので、軌跡全体で判定してよい）。
                // 被弾の判定の後に消す。
                if (!bullet.Body.Overlaps(bounds) || SolidQuery.Overlaps(Terrain, bullet.Body))
                {
                    bullet.MarkGone();
                }
            }
        }

        private void CollectPickups(Motion motion)
        {
            for (var i = 0; i < Room.Pickups.Count; i++)
            {
                var pickup = Room.Pickups[i];
                var id = _world.PickupId(RoomIndex, i);
                if (_state.IsTaken(id) || !motion.Touches(pickup.Box)) { continue; }
                _state.Take(id);
                switch (pickup.Kind)
                {
                    case PickupKind.Fruit:
                        Player.Heal(_config.FruitHeal);
                        _events |= ExpeditionEvents.PickedFruit;
                        break;
                    case PickupKind.DoubleJump:
                        _state.Grant(Ability.DoubleJump);
                        _events |= ExpeditionEvents.GainedAbility;
                        break;
                    case PickupKind.BreakShot:
                        _state.Grant(Ability.BreakShot);
                        _events |= ExpeditionEvents.GainedAbility;
                        break;
                }
            }
        }

        private void CheckStatues(Motion motion)
        {
            var touching = false;
            foreach (var statue in Room.Statues)
            {
                if (motion.Touches(Room.StatueBox(statue)))
                {
                    touching = true;
                    if (!_onStatue)
                    {
                        Player.Heal(_config.MaxHealth);
                        SavedAt = statue;
                        _events |= ExpeditionEvents.Saved;
                    }

                    break;
                }
            }

            _onStatue = touching;
        }

        /// <summary>直前に記録したセーブ像（<see cref="ExpeditionEvents.Saved"/> のとき）。</summary>
        public Cell SavedAt { get; private set; }

        private void TakeDamage(Motion motion, Box bossStart)
        {
            var best = Touch.None;
            var damage = 0;
            var sourceX = 0;
            var bullet = -1;
            for (var w = 0; w < _walkers.Count; w++)
            {
                if (!_walkers[w].IsAlive) { continue; }
                Consider(Sweep.First(motion.Start, motion.End, _walkerStarts[w], _walkers[w].Body), _config.ContactDamage, _walkers[w].Body.CenterX, -1);
            }

            if (Boss != null && Boss.IsAlive)
            {
                Consider(Sweep.First(motion.Start, motion.End, bossStart, Boss.Body), _config.ContactDamage, Boss.Body.CenterX, -1);
            }

            for (var b = 0; b < _bullets.Count; b++)
            {
                var item = _bullets[b];
                Consider(Sweep.First(motion.Start, motion.End, item.Previous, item.Body), _config.BulletDamage, item.Previous.CenterX, b);
            }

            if (best.Hit && Player.Hurt(damage, Math.Sign(Player.Body.CenterX - sourceX)))
            {
                _events |= ExpeditionEvents.Hurt;
                if (bullet >= 0) { _bullets[bullet].MarkGone(); }
            }

            _bullets.RemoveAll(item => item.IsGone);

            void Consider(Touch touch, int amount, int x, int index)
            {
                if (!touch.IsEarlierThan(best)) { return; }
                best = touch;
                damage = amount;
                sourceX = x;
                bullet = index;
            }
        }

        private bool TouchingStatue(Box body)
        {
            foreach (var statue in Room.Statues)
            {
                if (body.Overlaps(Room.StatueBox(statue))) { return true; }
            }

            return false;
        }

        /// <summary>箱が重なる扉の辺。重ならなければ null。</summary>
        private Side? TouchedDoor(Box box)
        {
            var map = Room.Map;
            var fromColumn = Units.FloorDiv(box.Left, Units.Tile);
            var toColumn = Units.FloorDiv(box.Right - 1, Units.Tile);
            var fromRow = Units.FloorDiv(box.Bottom, Units.Tile);
            var toRow = Units.FloorDiv(box.Top - 1, Units.Tile);
            for (var row = fromRow; row <= toRow; row++)
            {
                for (var column = fromColumn; column <= toColumn; column++)
                {
                    if (map[column, row] != TileKind.Door) { continue; }
                    if (column == 0) { return Side.Left; }
                    if (column == map.Width - 1) { return Side.Right; }
                    return row == 0 ? Side.Bottom : Side.Top;
                }
            }

            return null;
        }
    }
}
