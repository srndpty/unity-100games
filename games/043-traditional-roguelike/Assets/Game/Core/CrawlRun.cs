using System;
using System.Collections.Generic;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>
    /// 1 試合（1 シード）。自機の番で止まり、<see cref="Submit"/> で受けた操作を行ってから、
    /// <see cref="TurnScheduler"/> の順に敵を動かし、次の自機の番でまた止まる。
    /// 受け付けない操作は状態を何も変えない（時刻・乱数・満腹度も）。
    /// 画面に出すものは <see cref="View"/>（既知マップと視界の中だけ）から取る。
    /// </summary>
    public sealed partial class CrawlRun
    {
        private readonly List<Monster> _monsters = new();
        private readonly List<RunEvent> _events = new();
        private readonly Dictionary<int, int[]> _distanceCache = new();
        private bool[] _visible;
        private int[] _items;
        private int _itemCount;
        private int _nextMonsterId = 1;
        private RandomSeries _actionRng;

        public CrawlRun(CrawlConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            Rules = config.Copy();
            Seed = seed;
            Fingerprint = Game.Core.Fingerprint.Of(Rules);
            Player = new Player
            {
                Hp = Rules.PlayerHp,
                MaxHp = Rules.PlayerHp,
                Attack = Rules.PlayerAttack,
                Defense = Rules.PlayerDefense,
                Satiety = Rules.MaxSatiety,
            };
            Scheduler = new TurnScheduler();
            View = new PlayerView(this);

            // EnterFloor で作り直す。
            _visible = Array.Empty<bool>();
            _items = Array.Empty<int>();
            _actionRng = new RandomSeries(seed);
            Grid = null!;
            Known = null!;
            EnterFloor(1, 0);
            View.Refresh();
        }

        /// <summary>この試合の設定の複製。書き換えても試合には効かない（試合が使う設定と指紋は作ったときのまま）。</summary>
        public CrawlConfig Config => Rules.Copy();

        /// <summary>試合が使う設定（検証済み・以後変えない）。Core の中だけで読む。</summary>
        internal CrawlConfig Rules { get; }

        public int Seed { get; }

        /// <summary>設定の指紋（状態のハッシュに混ぜる）。</summary>
        public uint Fingerprint { get; }

        public RunState State { get; private set; } = RunState.Playing;

        /// <summary>今いる階（1〜5）。</summary>
        public int Floor { get; private set; }

        /// <summary>画面に出してよい情報だけの窓口。</summary>
        public PlayerView View { get; }

        public DeathCause DeathCause { get; private set; }

        /// <summary>自機を倒した敵の種類（死因が敵のとき）。</summary>
        public MonsterKind Killer { get; private set; }

        public int Kills { get; private set; }

        internal Player Player { get; }

        internal TurnScheduler Scheduler { get; }

        internal FloorGrid Grid { get; private set; }

        internal KnownMap Known { get; private set; }

        internal IReadOnlyList<Monster> Monsters => _monsters;

        internal IReadOnlyList<RunEvent> Events => _events;

        internal bool[] Visible => _visible;

        internal RandomSeries ActionRng => _actionRng;

        internal int FloorItemCount => _itemCount;

        /// <summary>自機の操作を 1 つ行い、敵を次の自機の番まで動かす。</summary>
        public Outcome Submit(Command command)
        {
            if (State != RunState.Playing) { return Outcome.Reject(RejectReason.NotPlaying); }
            var check = Validate(command);
            if (!check.Accepted) { return check; }

            _events.Clear();
            var cost = Perform(command);
            if (cost > 0 && State == RunState.Playing)
            {
                Scheduler.Spend(TurnScheduler.PlayerId, cost);
                AfterPlayerAction();
                RunUntilPlayerTurn();
            }

            Look();
            View.Refresh();
            return Outcome.Ok;
        }

        /// <summary>状態のハッシュ。以後の進み方を決める全ての状態と、設定の指紋を混ぜる。</summary>
        public uint Hash()
        {
            var h = StateHash.Start();
            h.Mix(Fingerprint);
            h.Mix(Seed);
            h.Mix((int)State);
            h.Mix(Floor);
            h.Mix((int)DeathCause);
            h.Mix((int)Killer);
            h.Mix(Kills);
            var p = Player;
            foreach (var v in new[] { p.Position.X, p.Position.Y, (int)p.Facing, p.Hp, p.MaxHp, p.Level, p.Exp, p.Attack, p.Defense, p.Satiety, p.Actions, p.SleepTurns })
            {
                h.Mix(v);
            }

            h.Mix(p.Inventory.Count);
            foreach (var item in p.Inventory) { h.Mix((int)item); }
            h.Mix(_nextMonsterId);
            h.Mix(_monsters.Count);
            foreach (var m in _monsters)
            {
                foreach (var v in new[]
                {
                    m.Id, (int)m.Kind, m.Position.X, m.Position.Y, m.Hp, m.MaxHp, m.Asleep ? 1 : 0, m.SleepTurns,
                    m.Held.HasValue ? (int)m.Held.Value : -1,
                    m.LastSeen.HasValue ? 1 : 0, m.LastSeen?.X ?? 0, m.LastSeen?.Y ?? 0,
                    m.WanderTarget.HasValue ? 1 : 0, m.WanderTarget?.X ?? 0, m.WanderTarget?.Y ?? 0,
                })
                {
                    h.Mix(v);
                }
            }

            h.Mix(_itemCount);
            for (int i = 0; i < _items.Length; i++)
            {
                if (_items[i] != KnownMap.NoItem)
                {
                    h.Mix(i);
                    h.Mix(_items[i]);
                }
            }

            for (int y = 0; y < Grid.Height; y++)
            {
                foreach (var ch in Grid.Row(y)) { h.Mix(ch); }
            }

            Known.Mix(ref h);
            Scheduler.Mix(ref h);
            h.Mix(_actionRng.State);
            return h.Value;
        }

        internal Monster? MonsterAt(Vec2i p)
        {
            foreach (var m in _monsters)
            {
                if (m.Position == p) { return m; }
            }

            return null;
        }

        internal ItemKind? ItemAt(Vec2i p)
        {
            if (!Grid.InBounds(p)) { return null; }
            var v = _items[Grid.Index(p)];
            return v == KnownMap.NoItem ? null : (ItemKind)v;
        }

        internal bool IsVisible(Vec2i p) => Grid.InBounds(p) && _visible[Grid.Index(p)];

        /// <summary>テスト用：品を置く・消す（上限を見ない）。</summary>
        internal void SetItem(Vec2i p, ItemKind? item)
        {
            var i = Grid.Index(p);
            if (_items[i] != KnownMap.NoItem) { _itemCount--; }
            _items[i] = item.HasValue ? (int)item.Value : KnownMap.NoItem;
            if (item.HasValue) { _itemCount++; }
        }

        /// <summary>テスト用：敵を足す（スケジューラにも今の時刻で入れる）。</summary>
        internal Monster AddMonster(MonsterKind kind, Vec2i position)
        {
            var m = new Monster(_nextMonsterId++, kind, position, Rules.StatsOf(kind).Hp);
            _monsters.Add(m);
            Scheduler.Add(m.Id, Scheduler.TimeOf(TurnScheduler.PlayerId));
            return m;
        }

        /// <summary>テスト用：敵を全て取り除く。</summary>
        internal void ClearMonsters()
        {
            foreach (var m in _monsters) { Scheduler.Remove(m.Id); }
            _monsters.Clear();
        }

        /// <summary>テスト用：自機を動かして視界を計算し直す。</summary>
        internal void Teleport(Vec2i p)
        {
            Player.Position = p;
            Look();
            View.Refresh();
        }

        /// <summary>テスト用：視界と画面の窓口を今の状態に合わせる。</summary>
        internal void Refresh()
        {
            Look();
            View.Refresh();
        }

        private void EnterFloor(int floor, long time)
        {
            Floor = floor;
            var layout = FloorGenerator.Generate(Seed, floor, Rules);
            Grid = layout.Grid;
            Known = new KnownMap(Grid.Width, Grid.Height);
            _visible = new bool[Grid.Width * Grid.Height];
            _items = new int[Grid.Width * Grid.Height];
            for (int i = 0; i < _items.Length; i++) { _items[i] = KnownMap.NoItem; }
            _itemCount = 0;
            _distanceCache.Clear();
            _monsters.Clear();
            Scheduler.Clear();
            Player.Position = layout.Start;
            Scheduler.Add(TurnScheduler.PlayerId, time);
            Populate(layout, time);
            _actionRng = RandomSeries.Derive(Seed, floor, (int)RngPurpose.Action);
            Look();
        }

        /// <summary>階の敵と品を置く。配置専用の系列を使うので、自機の行動に依らず同じ階は同じ配置になる。</summary>
        private void Populate(FloorLayout layout, long time)
        {
            var rng = RandomSeries.Derive(Seed, Floor, (int)RngPurpose.Placement);
            var startRoom = Grid.RoomAt(layout.Start);
            var monsterWeights = new int[CrawlConfig.MonsterKindCount];
            for (int k = 0; k < monsterWeights.Length; k++) { monsterWeights[k] = Rules.Monsters[k].FloorWeights[Floor - 1]; }
            var count = Rules.MonstersBase + (Rules.MonstersPerFloor * Floor);
            for (int i = 0; i < count; i++)
            {
                if (!TryRoomTile(rng, startRoom, p => MonsterAt(p) == null && p != layout.Start, out var p)) { break; }
                var kind = (MonsterKind)rng.Weighted(monsterWeights);
                var m = new Monster(_nextMonsterId++, kind, p, Rules.StatsOf(kind).Hp) { Asleep = kind == MonsterKind.Serpent };
                _monsters.Add(m);
                Scheduler.Add(m.Id, time);
            }

            var itemWeights = new int[CrawlConfig.ItemKindCount];
            for (int k = 0; k < itemWeights.Length; k++) { itemWeights[k] = Rules.Items[k].FloorWeights[Floor - 1]; }
            var items = Rules.ItemsBase + (Rules.ItemsPerFloor * Floor);
            for (int i = 0; i < items; i++)
            {
                if (!TryRoomTile(rng, -1, p => ItemAt(p) == null && Grid[p] == Tile.Room, out var p)) { break; }
                SetItem(p, (ItemKind)rng.Weighted(itemWeights));
            }
        }

        /// <summary>部屋の床から条件に合うマスを引く（<paramref name="excludedRoom"/> の部屋は除く）。</summary>
        private bool TryRoomTile(RandomSeries rng, int excludedRoom, Func<Vec2i, bool> ok, out Vec2i result)
        {
            var candidates = new List<Vec2i>();
            for (int r = 0; r < Grid.Rooms.Count; r++)
            {
                if (r == excludedRoom) { continue; }
                var room = Grid.Rooms[r];
                for (int y = room.Y0; y <= room.Y1; y++)
                {
                    for (int x = room.X0; x <= room.X1; x++)
                    {
                        var p = new Vec2i(x, y);
                        if (ok(p)) { candidates.Add(p); }
                    }
                }
            }

            if (candidates.Count == 0)
            {
                result = default;
                return false;
            }

            result = candidates[rng.Next(candidates.Count)];
            return true;
        }

        /// <summary>視界を計算し直し、見えているマスを既知マップに覚える。</summary>
        private void Look()
        {
            Fov.Compute(Grid, Player.Position, Rules.ViewRadius, _visible);
            Known.Observe(_visible, ItemAt);
        }

        /// <summary>受け付けるかだけを判定する。状態を変えない。</summary>
        private Outcome Validate(Command command)
        {
            var p = Player;
            switch (command.Kind)
            {
                case CommandKind.Move:
                    if (!Directions.IsValid(command.Direction)) { return Outcome.Reject(RejectReason.InvalidCommand); }
                    return Grid.CanStep(p.Position, command.Direction) ? Outcome.Ok : Outcome.Reject(RejectReason.Blocked);
                case CommandKind.Face:
                    return Directions.IsValid(command.Direction) ? Outcome.Ok : Outcome.Reject(RejectReason.InvalidCommand);
                case CommandKind.Wait:
                    return Outcome.Ok;
                case CommandKind.Pickup:
                    if (ItemAt(p.Position) == null) { return Outcome.Reject(RejectReason.NothingToPickUp); }
                    return p.Inventory.Count < Rules.InventoryCapacity ? Outcome.Ok : Outcome.Reject(RejectReason.InventoryFull);
                case CommandKind.Use:
                    if (command.Slot < 0 || command.Slot >= p.Inventory.Count) { return Outcome.Reject(RejectReason.EmptySlot); }
                    return p.Inventory[command.Slot] == ItemKind.Stone ? Outcome.Reject(RejectReason.CannotUse) : Outcome.Ok;
                case CommandKind.Throw:
                    return command.Slot >= 0 && command.Slot < p.Inventory.Count ? Outcome.Ok : Outcome.Reject(RejectReason.EmptySlot);
                case CommandKind.Drop:
                    if (command.Slot < 0 || command.Slot >= p.Inventory.Count) { return Outcome.Reject(RejectReason.EmptySlot); }
                    return ItemAt(p.Position) == null && _itemCount < Rules.FloorItemCap ? Outcome.Ok : Outcome.Reject(RejectReason.CannotDrop);
                case CommandKind.Descend:
                    return Grid[p.Position] == Tile.Stairs ? Outcome.Ok : Outcome.Reject(RejectReason.NotOnStairs);
                default:
                    return Outcome.Reject(RejectReason.InvalidCommand);
            }
        }

        /// <summary>受け付けた操作を行い、行動の重さを返す（向きを変えるだけなら 0）。</summary>
        private int Perform(Command command)
        {
            var p = Player;
            switch (command.Kind)
            {
                case CommandKind.Move:
                    p.Facing = command.Direction;
                    var target = MonsterAt(p.Position + command.Direction.Offset());
                    if (target != null)
                    {
                        PlayerAttack(target);
                    }
                    else
                    {
                        p.Position += command.Direction.Offset();
                        Look();
                        StepOnItem();
                    }

                    return CrawlConfig.PlayerActionCost;
                case CommandKind.Face:
                    p.Facing = command.Direction;
                    return 0;
                case CommandKind.Wait:
                    return CrawlConfig.PlayerActionCost;
                case CommandKind.Pickup:
                    PickUp();
                    return CrawlConfig.PlayerActionCost;
                case CommandKind.Use:
                    UseItem(command.Slot);
                    return CrawlConfig.PlayerActionCost;
                case CommandKind.Throw:
                    ThrowItem(command.Slot);
                    return CrawlConfig.PlayerActionCost;
                case CommandKind.Drop:
                    var dropped = p.Inventory[command.Slot];
                    p.Inventory.RemoveAt(command.Slot);
                    SetItem(p.Position, dropped);
                    Emit(EventKind.Dropped, 0, -1, default, dropped, p.Position, 0);
                    return CrawlConfig.PlayerActionCost;
                case CommandKind.Descend:
                    Descend();
                    return 0;
                default:
                    throw new InvalidOperationException(command.ToString());
            }
        }

        private void Descend()
        {
            if (Floor >= CrawlConfig.FloorCount)
            {
                State = RunState.Won;
                Emit(EventKind.Won, 0, -1, default, default, Player.Position, Floor);
                return;
            }

            var time = Scheduler.TimeOf(TurnScheduler.PlayerId) + CrawlConfig.PlayerActionCost;
            EnterFloor(Floor + 1, time);
            Emit(EventKind.Descended, 0, -1, default, default, Player.Position, Floor);
        }

        /// <summary>時間を使った自機の行動の後：満腹度・自然回復・眠りの残り。</summary>
        private void AfterPlayerAction()
        {
            var p = Player;
            p.Actions++;
            if (p.Actions % Rules.HungerEvery == 0 && p.Satiety > 0)
            {
                p.Satiety--;
                if (p.Satiety == Rules.HungryWarning) { Emit(EventKind.Hungry, 0, -1, default, default, p.Position, p.Satiety); }
                if (p.Satiety == 0) { Emit(EventKind.Starving, 0, -1, default, default, p.Position, 0); }
            }

            if (p.Satiety == 0)
            {
                p.Hp -= Rules.StarveDamage;
                Emit(EventKind.Starve, 0, -1, default, default, p.Position, Rules.StarveDamage);
                if (p.Hp <= 0)
                {
                    Die(DeathCause.Starvation, default);
                    return;
                }
            }
            else if (p.Actions % Rules.RegenEvery == 0 && p.Hp < p.MaxHp)
            {
                p.Hp++;
            }
        }

        /// <summary>次が自機の番（眠っていない）になるまで、スケジューラの順に動かす。</summary>
        private void RunUntilPlayerTurn()
        {
            while (State == RunState.Playing)
            {
                var id = Scheduler.Next();
                if (id == TurnScheduler.PlayerId)
                {
                    if (Player.SleepTurns <= 0) { return; }
                    Player.SleepTurns--;
                    if (Player.SleepTurns == 0) { Emit(EventKind.WokeUp, 0, -1, default, default, Player.Position, 0); }
                    Scheduler.Spend(TurnScheduler.PlayerId, CrawlConfig.PlayerActionCost);
                    AfterPlayerAction();
                    continue;
                }

                var monster = FindMonster(id);
                if (monster == null)
                {
                    throw new InvalidOperationException($"スケジューラに敵 {id} が残っています。");
                }

                MonsterAct(monster);
                if (Scheduler.Contains(id)) { Scheduler.Spend(id, Rules.StatsOf(monster.Kind).ActionCost); }
            }
        }

        private Monster? FindMonster(int id)
        {
            foreach (var m in _monsters)
            {
                if (m.Id == id) { return m; }
            }

            return null;
        }

        private void Die(DeathCause cause, MonsterKind killer)
        {
            Player.Hp = 0;
            State = RunState.Dead;
            DeathCause = cause;
            Killer = killer;
            Emit(EventKind.Died, 0, -1, killer, default, Player.Position, (int)cause);
        }

        /// <summary>出来事を記録する。場所が自機の位置か視界の中なら見えた出来事。</summary>
        private void Emit(EventKind kind, int actor, int target, MonsterKind monster, ItemKind item, Vec2i position, int value)
        {
            var seen = position == Player.Position || IsVisible(position);
            _events.Add(new RunEvent(kind, actor, target, monster, item, position, value, seen));
        }

        /// <summary>ダメージ：攻撃 × (87〜112)% − 防御、最低 1。</summary>
        private int RollDamage(int attack, int defense)
        {
            var percent = _actionRng.Range(Rules.DamageMinPercent, Rules.DamageMaxPercent);
            var raw = (int)((long)attack * percent / 100) - defense;
            return Math.Max(1, raw);
        }

        /// <summary>目的地への距離の地図（地形だけ）。地形は階の中で変わらないので階ごとに覚えておく。</summary>
        private int[] DistancesTo(Vec2i target)
        {
            var key = Grid.Index(target);
            if (!_distanceCache.TryGetValue(key, out var dist))
            {
                dist = Grid.Distances(target);
                _distanceCache[key] = dist;
            }

            return dist;
        }

        /// <summary>歩ける・誰もいないマスを無作為に引く（行動の系列）。</summary>
        private bool TryRandomFreeTile(Func<Vec2i, bool> extra, out Vec2i result)
        {
            var candidates = new List<Vec2i>();
            for (int y = 0; y < Grid.Height; y++)
            {
                for (int x = 0; x < Grid.Width; x++)
                {
                    var p = new Vec2i(x, y);
                    if (Grid.IsWalkable(p) && p != Player.Position && MonsterAt(p) == null && extra(p)) { candidates.Add(p); }
                }
            }

            if (candidates.Count == 0)
            {
                result = default;
                return false;
            }

            result = candidates[_actionRng.Next(candidates.Count)];
            return true;
        }
    }
}
