using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum TowerKind { Bow, Cannon, Frost, Arcane }
    public enum EnemyKind { Soldier, Runner, Armor }
    public enum Phase { Preparation, Running, Won, Lost }
    public enum CommandResult { Applied, Closed, Invalid, Occupied, Empty, Funds, Maximum }

    public readonly struct Point
    {
        public Point(int x, int y) => (X, Y) = (x, y);
        public int X { get; }
        public int Y { get; }
        public int DistanceSquared(Point other) => (X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y);
    }

    public static class Canyon
    {
        public const int Scale = 1000;
        public const int Length = 17000;
        public static IReadOnlyList<Point> Slots { get; } = Array.AsReadOnly(new Point[]
        {
            new(1000, 2000), new(2000, 4000), new(4000, 2000), new(5000, 0), new(6000, 2000),
            new(7000, 3000), new(9000, 2000), new(9000, 4000), new(10000, 6000), new(11000, 4000),
        });

        public static Point At(int progress)
        {
            var p = Math.Clamp(progress, 0, Length);
            if (p <= 3000) { return new Point(p, 3000); }
            if (p <= 5000) { return new Point(3000, 6000 - p); }
            if (p <= 10000) { return new Point(p - 2000, 1000); }
            if (p <= 14000) { return new Point(8000, p - 9000); }
            return new Point(p - 6000, 5000);
        }
    }

    [Serializable]
    public sealed class Rules
    {
        public int StartingLives = 10;
        public int StartingMoney = 160;
        public int WaveIncome = 25;
        public int SpawnInterval = 30;
        public int[] Costs = { 45, 80, 60, 90 };
        public int[] Ranges = { 2500, 2300, 2000, 2100 };
        public int[] Cooldowns = { 15, 36, 24, 24 };
        public int[] Power = { 5, 10, 3, 12 };
        public int[] Increments = { 3, 5, 2, 6 };
        public int[] HitPoints = { 18, 12, 32 };
        public int[] Speeds = { 60, 90, 40 };
        public int[] Rewards = { 8, 9, 14 };
        public int[] Enemies =
        {
            6,0,0, 6,2,0, 5,4,0, 6,0,2, 4,5,2, 6,3,3, 4,6,3, 5,4,5, 4,6,6, 6,6,8,
        };

        internal Rules Snapshot()
        {
            if (StartingLives is < 1 or > 1000 || StartingMoney is < 0 or > 100000 ||
                WaveIncome is < 0 or > 10000 || SpawnInterval is < 1 or > 300)
            {
                throw new ArgumentException("初期値または出現間隔が範囲外です。");
            }

            var copy = (Rules)MemberwiseClone();
            copy.Costs = Copy(Costs, 4, 1, 1000);
            copy.Ranges = Copy(Ranges, 4, 1, 12000);
            copy.Cooldowns = Copy(Cooldowns, 4, 1, 300);
            copy.Power = Copy(Power, 4, 1, 1000);
            copy.Increments = Copy(Increments, 4, 0, 1000);
            copy.HitPoints = Copy(HitPoints, 3, 1, 1000);
            copy.Speeds = Copy(Speeds, 3, 2, Canyon.Length);
            copy.Rewards = Copy(Rewards, 3, 0, 1000);
            copy.Enemies = Copy(Enemies, 30, 0, 30);
            for (var i = 0; i < 30; i += 3)
            {
                if (Enemies[i] + Enemies[i + 1] + Enemies[i + 2] == 0) { throw new ArgumentException("空のWaveです。"); }
            }

            return copy;
        }

        private static int[] Copy(int[] values, int size, int min, int max)
        {
            if (values.Length != size || Array.Exists(values, x => x < min || x > max))
            {
                throw new ArgumentException("設定配列の長さまたは値が範囲外です。");
            }

            return (int[])values.Clone();
        }
    }

    public sealed class Enemy
    {
        internal Enemy(int id, EnemyKind kind, int hp) => (Id, Kind, Hp, MaxHp) = (id, kind, hp, hp);
        public int Id { get; }
        public EnemyKind Kind { get; }
        public int Hp { get; internal set; }
        public int MaxHp { get; }
        public int Progress { get; internal set; }
        public int SlowUntil { get; internal set; }
        public int HitTick { get; internal set; } = -100;
        public TowerKind HitBy { get; internal set; }
        public Point Position => Canyon.At(Progress);
    }

    public sealed class Tower
    {
        internal Tower(TowerKind kind, int cost) => (Kind, Paid) = (kind, cost);
        public TowerKind Kind { get; }
        public int Level { get; internal set; } = 1;
        public int Paid { get; internal set; }
        public int ShotTick { get; internal set; } = -100;
    }

    public readonly struct Shot
    {
        public Shot(int tick, int slot, TowerKind kind, int targetId, Point target) =>
            (Tick, Slot, Kind, TargetId, Target) = (tick, slot, kind, targetId, target);

        public int Tick { get; }
        public int Slot { get; }
        public TowerKind Kind { get; }
        public int TargetId { get; }
        public Point Target { get; }
    }

    public readonly struct Hit
    {
        public Hit(int tick, TowerKind kind, int enemyId) => (Tick, Kind, EnemyId) = (tick, kind, enemyId);
        public int Tick { get; }
        public TowerKind Kind { get; }
        public int EnemyId { get; }
    }

    public sealed class Defense
    {
        public const int WaveCount = 10;
        public const int TickRate = 30;
        public const int FlightTicks = 8;
        public const int EffectTicks = FlightTicks + 3;
        private readonly List<Shot> _shots = new();
        private readonly List<Hit> _hits = new();
        private readonly int[] _readyTick = new int[Canyon.Slots.Count];

        private readonly Rules _rules;
        private readonly List<Enemy> _enemies = new();
        private readonly Tower?[] _towers = new Tower?[Canyon.Slots.Count];
        private int _nextId;
        private int _spawnTick;
        private int _spawned;

        public Defense(Rules rules)
        {
            _rules = rules.Snapshot();
            Money = _rules.StartingMoney;
            Lives = _rules.StartingLives;
            Enemies = _enemies.AsReadOnly();
            Towers = Array.AsReadOnly(_towers);
            Shots = _shots.AsReadOnly();
            Hits = _hits.AsReadOnly();
        }

        public IReadOnlyList<Shot> Shots { get; }
        public IReadOnlyList<Hit> Hits { get; }
        public IReadOnlyList<Enemy> Enemies { get; }
        public IReadOnlyList<Tower?> Towers { get; }
        public int Money { get; private set; }
        public int Lives { get; private set; }
        public int Wave { get; private set; } = 1;
        public int Tick { get; private set; }
        public Phase Phase { get; private set; }
        private bool Open => Phase is Phase.Preparation or Phase.Running;

        public int Cost(TowerKind kind) => _rules.Costs[(int)kind];
        public int Range(TowerKind kind) => _rules.Ranges[(int)kind];
        public int Power(TowerKind kind, int level) => _rules.Power[(int)kind] + (level - 1) * _rules.Increments[(int)kind];
        public int Cooldown(TowerKind kind) => _rules.Cooldowns[(int)kind];
        public int WaveEnemies(EnemyKind kind) => _rules.Enemies[(Wave - 1) * 3 + (int)kind];
        public int UpgradeCost(Tower tower) => tower.Level == 1 ? (Cost(tower.Kind) + 1) / 2 : Cost(tower.Kind);
        public static int Refund(Tower tower) => tower.Paid * 7 / 10;

        public CommandResult Build(int slot, TowerKind kind)
        {
            if (!Open) { return CommandResult.Closed; }
            if (slot < 0 || slot >= _towers.Length || (uint)kind >= 4) { return CommandResult.Invalid; }
            if (_towers[slot] != null) { return CommandResult.Occupied; }
            var cost = Cost(kind);
            if (Money < cost) { return CommandResult.Funds; }
            _towers[slot] = new Tower(kind, cost);
            Money -= cost;
            return CommandResult.Applied;
        }

        public CommandResult Upgrade(int slot)
        {
            if (!Open) { return CommandResult.Closed; }
            if (slot < 0 || slot >= _towers.Length) { return CommandResult.Invalid; }
            var tower = _towers[slot];
            if (tower == null) { return CommandResult.Empty; }
            if (tower.Level >= 3) { return CommandResult.Maximum; }
            var cost = UpgradeCost(tower);
            if (Money < cost) { return CommandResult.Funds; }
            Money -= cost;
            tower.Paid += cost;
            tower.Level++;
            return CommandResult.Applied;
        }

        public CommandResult Sell(int slot)
        {
            if (!Open) { return CommandResult.Closed; }
            if (slot < 0 || slot >= _towers.Length) { return CommandResult.Invalid; }
            var tower = _towers[slot];
            if (tower == null) { return CommandResult.Empty; }
            Money += Refund(tower);
            _towers[slot] = null;
            return CommandResult.Applied;
        }

        public bool StartWave()
        {
            if (Phase != Phase.Preparation) { return false; }
            _spawned = 0;
            _spawnTick = Tick + 1;
            Phase = Phase.Running;
            return true;
        }

        public void Step()
        {
            if (Phase != Phase.Running) { return; }
            Tick++;
            _shots.RemoveAll(shot => Tick - shot.Tick >= EffectTicks);
            // 遅れて表示する被弾も、飛翔と着弾の表示期間まで保持する。
            _hits.RemoveAll(hit => Tick - hit.Tick >= EffectTicks + 1);
            var offset = (Wave - 1) * 3;
            var soldiers = _rules.Enemies[offset];
            var runners = _rules.Enemies[offset + 1];
            var total = soldiers + runners + _rules.Enemies[offset + 2];
            if (_spawned < total && Tick == _spawnTick)
            {
                var kind = _spawned < soldiers ? EnemyKind.Soldier :
                    _spawned < soldiers + runners ? EnemyKind.Runner : EnemyKind.Armor;
                _enemies.Add(new Enemy(_nextId++, kind, _rules.HitPoints[(int)kind] + 2 * (Wave - 1)));
                _spawnTick += _rules.SpawnInterval;
                _spawned++;
            }

            for (var i = 0; i < _enemies.Count;)
            {
                var enemy = _enemies[i];
                var speed = _rules.Speeds[(int)enemy.Kind];
                enemy.Progress = Math.Min(Canyon.Length, enemy.Progress + (Tick <= enemy.SlowUntil ? speed / 2 : speed));
                if (enemy.Progress < Canyon.Length) { i++; continue; }
                _enemies.RemoveAt(i);
                Lives--;
                if (Lives == 0) { ClearEffects(); Phase = Phase.Lost; return; }
            }

            for (var slot = 0; slot < _towers.Length; slot++)
            {
                var tower = _towers[slot];
                if (tower != null && _readyTick[slot] <= Tick) { Fire(slot, tower); }
            }

            _enemies.RemoveAll(enemy => enemy.Hp == 0);
            if (_spawned != total || _enemies.Count != 0) { return; }
            ClearEffects();

            Money += _rules.WaveIncome;
            if (Wave == WaveCount) { Phase = Phase.Won; }
            else { Wave++; Phase = Phase.Preparation; }
        }

        private void ClearEffects()
        {
            _shots.Clear();
            _hits.Clear();
            foreach (var tower in _towers)
            {
                if (tower != null) { tower.ShotTick = -100; }
            }
        }

        private void Fire(int slot, Tower tower)
        {
            var origin = Canyon.Slots[slot];
            var range = Range(tower.Kind);
            Enemy? target = null;
            foreach (var enemy in _enemies)
            {
                if (enemy.Hp <= 0 || origin.DistanceSquared(enemy.Position) > range * range) { continue; }
                if (target == null || enemy.Progress > target.Progress) { target = enemy; }
            }

            if (target == null) { return; }
            tower.ShotTick = Tick;
            _shots.Add(new Shot(Tick, slot, tower.Kind, target.Id, target.Position));
            _readyTick[slot] = Tick + Cooldown(tower.Kind);
            Hit(tower, target);
            if (tower.Kind != TowerKind.Cannon) { return; }
            foreach (var enemy in _enemies)
            {
                if (enemy != target && enemy.Hp > 0 && target.Position.DistanceSquared(enemy.Position) <= 800 * 800)
                {
                    Hit(tower, enemy);
                }
            }
        }

        private void Hit(Tower tower, Enemy enemy)
        {
            var armor = enemy.Kind == EnemyKind.Armor && tower.Kind != TowerKind.Arcane ? 3 : 0;
            var damage = Math.Max(1, Power(tower.Kind, tower.Level) - armor);
            enemy.HitTick = Tick;
            enemy.HitBy = tower.Kind;
            _hits.Add(new Hit(Tick, tower.Kind, enemy.Id));
            enemy.Hp = Math.Max(0, enemy.Hp - damage);
            if (enemy.Hp == 0) { Money += _rules.Rewards[(int)enemy.Kind]; }
            else if (tower.Kind == TowerKind.Frost) { enemy.SlowUntil = Tick + 30; }
        }
    }
}
