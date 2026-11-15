using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum GamePhase { Playing, Respawning, WaveTransition, Won, Lost }
    public enum BulletOwner { Player, Enemy }
    public readonly struct PlayerCommand
    {
        public PlayerCommand(int move, bool fire) { Move = move; Fire = fire; }
        public int Move { get; }
        public bool Fire { get; }
    }
    public readonly struct Bullet
    {
        public Bullet(int id, BulletOwner owner, Point position) { Id = id; Owner = owner; Position = position; }
        public int Id { get; }
        public BulletOwner Owner { get; }
        public Point Position { get; }
    }
    public sealed class InvadersState
    {
        private readonly InvadersConfig _config;
        private readonly bool[] _enemies;
        private readonly bool[] _shields = new bool[3 * 12 * 6];
        private readonly List<Bullet> _bullets = new();
        private readonly List<Hit> _hits = new();
        private readonly Motion[] _motions;
        private readonly bool[] _consumed;
        private uint _random;
        private int _nextBulletId;
        private int _playerCooldown;
        private int _enemyCooldown;
        private int _wait;
        private bool _descendPending;
        private bool _protectNextWave;
        private float _formationX;
        private float _formationY;

        public InvadersState(InvadersConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate(); _config = config.Copy();
            _random = seed == 0 ? 0x9e3779b9u : unchecked((uint)seed);
            _enemies = new bool[config.Columns * config.Rows];
            _motions = new Motion[config.PlayerBulletLimit + config.EnemyBulletLimit];
            _consumed = new bool[_motions.Length];
            for (var i = 0; i < _shields.Length; i++) { _shields[i] = true; }
            Lives = config.Lives; Wave = 1;
            InitializeWave();
        }
        public GamePhase Phase { get; private set; }
        public int Lives { get; private set; }
        public int Score { get; private set; }
        public int Wave { get; private set; }
        public int TickCount { get; private set; }
        public int Direction { get; private set; }
        public int InvincibleTicks { get; private set; }
        public float PlayerX { get; private set; }
        public int EnemyCount { get; private set; }
        public int EnemyCapacity => _enemies.Length;
        public int ShieldCapacity => _shields.Length;
        public int BulletCount => _bullets.Count;
        public Bullet BulletAt(int index) => _bullets[index];
        public bool EnemyAlive(int index) => _enemies[index];
        public bool ShieldAlive(int index) => _shields[index];
        public Point EnemyAt(int index) => new(_formationX + (index % _config.Columns - (_config.Columns - 1) / 2f) * 1.5f,
            _formationY - index / _config.Columns * 1.15f);
        public static Point ShieldAt(int index) => new((index / 72 - 1) * 6f + (index % 12 - 5.5f) * InvadersConfig.ShieldCellSize,
            -3.5f + index % 72 / 12 * InvadersConfig.ShieldCellSize);
        public static bool CanHit(BulletOwner owner, BulletOwner target) => owner != target;

        public void Step(PlayerCommand command)
        {
            if (Phase == GamePhase.Won || Phase == GamePhase.Lost) { return; }
            if (command.Move < -1 || command.Move > 1) { throw new ArgumentOutOfRangeException(nameof(command)); }
            TickCount++;
            if (Phase == GamePhase.WaveTransition || Phase == GamePhase.Respawning)
            {
                if (--_wait == 0)
                {
                    if (Phase == GamePhase.WaveTransition) { Wave++; InitializeWave(); }
                    else { Phase = GamePhase.Playing; InvincibleTicks = InvadersConfig.Ticks(_config.InvincibleSeconds); }
                }
                return;
            }
            var invincible = InvincibleTicks > 0;
            if (invincible) { InvincibleTicks--; }
            if (_playerCooldown > 0) { _playerCooldown--; }
            if (--_enemyCooldown <= 0)
            {
                EnemyFire();
                _enemyCooldown = InvadersConfig.Ticks(_config.EnemyFireSeconds * (float)Math.Pow(_config.WaveFireMultiplier, Wave - 1));
            }
            if (command.Fire && _playerCooldown == 0 && CountBullets(BulletOwner.Player) < _config.PlayerBulletLimit)
            {
                AddBullet(BulletOwner.Player, new Point(PlayerX, InvadersConfig.PlayerY + .5f));
                _playerCooldown = InvadersConfig.Ticks(_config.PlayerFireSeconds);
            }
            var player = PlayerMotion(command.Move);
            var formation = FormationMotion();
            var motions = _motions;
            _hits.Clear();
            for (var b = 0; b < _bullets.Count; b++)
            {
                var bullet = _bullets[b];
                motions[b] = new Motion(bullet.Position, 0, (bullet.Owner == BulletOwner.Player ? _config.PlayerBulletSpeed : -_config.EnemyBulletSpeed) / InvadersConfig.TickRate);
                for (var s = 0; s < _shields.Length; s++)
                {
                    if (_shields[s]) { AddHit(b, s, 0, motions[b], new Motion(ShieldAt(s), 0, 0), .125f, .125f); }
                }
                if (CanHit(bullet.Owner, BulletOwner.Enemy))
                {
                    for (var e = 0; e < _enemies.Length; e++)
                    {
                        if (_enemies[e]) { AddHit(b, e, 1, motions[b], EnemyMotion(e, formation), .5f, .35f); }
                    }
                }
                else if (!invincible) { AddHit(b, 0, 2, motions[b], player, .5f, .3f); }
            }
            _hits.Sort(Hit.Compare);
            var consumed = _consumed;
            Array.Clear(consumed, 0, consumed.Length);
            var damaged = false;
            foreach (var hit in _hits)
            {
                if (consumed[hit.Bullet]) { continue; }
                if (hit.Kind == 0)
                {
                    if (!_shields[hit.Target]) { continue; }
                    _shields[hit.Target] = false;
                }
                else if (hit.Kind == 1)
                {
                    if (!_enemies[hit.Target]) { continue; }
                    _enemies[hit.Target] = false; EnemyCount--; Score += 100;
                }
                else
                {
                    if (damaged) { continue; }
                    damaged = true; Lives--;
                }
                consumed[hit.Bullet] = true;
            }
            for (var b = _bullets.Count - 1; b >= 0; b--)
            {
                var position = motions[b].At(1);
                if (consumed[b] || position.Y > 9 || position.Y < -8) { _bullets.RemoveAt(b); }
                else { _bullets[b] = new Bullet(_bullets[b].Id, _bullets[b].Owner, position); }
            }
            PlayerX = player.At(1).X;
            var end = formation.At(1); _formationX = end.X; _formationY = end.Y;
            var invaded = false;
            for (var e = 0; e < _enemies.Length; e++)
            {
                if (!_enemies[e]) { continue; }
                var enemy = EnemyAt(e);
                if (enemy.Y - .35f <= InvadersConfig.InvasionY) { invaded = true; }
                for (var s = 0; s < _shields.Length; s++)
                {
                    if (!_shields[s] || enemy.Y - .35f > -2.125f || enemy.Y + .35f + _config.Descent < -3.625f) { continue; }
                    if (Motion.Contact(EnemyMotion(e, formation), new Motion(ShieldAt(s), 0, 0), .625f, .475f) != null) { _shields[s] = false; }
                }
            }
            if (Lives == 0 || invaded) { Phase = GamePhase.Lost; _bullets.Clear(); }
            else if (EnemyCount == 0)
            {
                Phase = Wave == 3 ? GamePhase.Won : GamePhase.WaveTransition;
                _protectNextWave = damaged;
                _wait = InvadersConfig.Ticks(damaged ? Math.Max(_config.TransitionSeconds, _config.RespawnSeconds) : _config.TransitionSeconds);
                _bullets.Clear(); PlayerX = 0;
            }
            else if (damaged)
            {
                Phase = GamePhase.Respawning; _wait = InvadersConfig.Ticks(_config.RespawnSeconds);
                _bullets.Clear(); PlayerX = 0; _playerCooldown = 0;
            }
        }

        private void InitializeWave()
        {
            Array.Fill(_enemies, true); EnemyCount = _enemies.Length;
            _formationX = 0; _formationY = 6.5f; Direction = 1; _descendPending = false;
            PlayerX = 0; _playerCooldown = 0;
            _enemyCooldown = InvadersConfig.Ticks(_config.EnemyFireSeconds * (float)Math.Pow(_config.WaveFireMultiplier, Wave - 1));
            InvincibleTicks = _protectNextWave ? InvadersConfig.Ticks(_config.InvincibleSeconds) : 0;
            _protectNextWave = false; Phase = GamePhase.Playing;
        }

        private Motion PlayerMotion(int move)
        {
            var dx = move * _config.PlayerSpeed / InvadersConfig.TickRate;
            var stop = dx == 0 ? 1 : Math.Min(1, Math.Max(0, (move * 9.5f - PlayerX) / dx));
            return new Motion(new Point(PlayerX, InvadersConfig.PlayerY), dx, 0, stop);
        }

        private Motion FormationMotion()
        {
            var start = new Point(_formationX, _formationY);
            if (_descendPending) { _descendPending = false; Direction = -Direction; return new Motion(start, 0, -_config.Descent); }
            var edge = Direction > 0 ? float.MinValue : float.MaxValue;
            for (var e = 0; e < _enemies.Length; e++)
            {
                if (_enemies[e]) { edge = Direction > 0 ? Math.Max(edge, EnemyAt(e).X + .5f) : Math.Min(edge, EnemyAt(e).X - .5f); }
            }
            var dx = Direction * (_config.FormationSpeed + (Wave - 1) * _config.WaveSpeedIncrease) / InvadersConfig.TickRate;
            var stop = Math.Min(1, Math.Max(0, (Direction * InvadersConfig.FieldHalfWidth - edge) / dx));
            if (stop < 1 || Math.Abs(edge + dx) >= InvadersConfig.FieldHalfWidth) { _descendPending = true; }
            return new Motion(start, dx, 0, stop);
        }

        private Motion EnemyMotion(int index, Motion formation)
        {
            var offsetX = (index % _config.Columns - (_config.Columns - 1) / 2f) * 1.5f;
            return new Motion(new Point(formation.Start.X + offsetX, formation.Start.Y - index / _config.Columns * 1.15f), formation.Dx, formation.Dy, formation.Stop);
        }

        private void AddHit(int bullet, int target, int kind, Motion a, Motion b, float hw, float hh)
        {
            var endA = a.At(1); var endB = b.At(1);
            if (Math.Min(a.Start.Y, endA.Y) - .15f > Math.Max(b.Start.Y, endB.Y) + hh
                || Math.Max(a.Start.Y, endA.Y) + .15f < Math.Min(b.Start.Y, endB.Y) - hh
                || Math.Min(a.Start.X, endA.X) - .06f > Math.Max(b.Start.X, endB.X) + hw
                || Math.Max(a.Start.X, endA.X) + .06f < Math.Min(b.Start.X, endB.X) - hw) { return; }
            var time = Motion.Contact(a, b, hw + .06f, hh + .15f);
            if (time != null) { _hits.Add(new Hit(time.Value, bullet, target, kind)); }
        }
        private int CountBullets(BulletOwner owner)
        {
            var count = 0;
            foreach (var bullet in _bullets) { if (bullet.Owner == owner) { count++; } }
            return count;
        }
        private void AddBullet(BulletOwner owner, Point position) => _bullets.Add(new Bullet(_nextBulletId++, owner, position));
        private void EnemyFire()
        {
            if (CountBullets(BulletOwner.Enemy) >= _config.EnemyBulletLimit) { return; }
            var columns = new int[_config.Columns]; var count = 0;
            for (var c = 0; c < _config.Columns; c++)
            {
                for (var r = _config.Rows - 1; r >= 0; r--)
                {
                    var index = r * _config.Columns + c;
                    if (_enemies[index]) { columns[count++] = index; break; }
                }
            }
            if (count == 0) { return; }
            _random ^= _random << 13; _random ^= _random >> 17; _random ^= _random << 5;
            var position = EnemyAt(columns[(int)(_random % (uint)count)]);
            AddBullet(BulletOwner.Enemy, new Point(position.X, position.Y - .5f));
        }

        private readonly struct Hit
        {
            public Hit(float time, int bullet, int target, int kind) { Time = time; Bullet = bullet; Target = target; Kind = kind; }
            private float Time { get; }
            public int Bullet { get; }
            public int Target { get; }
            public int Kind { get; }
            public static int Compare(Hit a, Hit b)
            {
                var time = a.Time.CompareTo(b.Time);
                if (time != 0) { return time; }
                var bullet = a.Bullet.CompareTo(b.Bullet);
                if (bullet != 0) { return bullet; }
                var kind = a.Kind.CompareTo(b.Kind);
                return kind != 0 ? kind : a.Target.CompareTo(b.Target);
            }
        }
    }
}
