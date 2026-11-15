using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum GamePhase { Playing, Respawning, WaveTransition, Won, Lost }
    public readonly struct PlayerCommand
    {
        public PlayerCommand(int turn, bool thrust, bool fire) { Turn = turn; Thrust = thrust; Fire = fire; }
        public int Turn { get; }
        public bool Thrust { get; }
        public bool Fire { get; }
    }
    public readonly struct Asteroid
    {
        public Asteroid(int id, int size, Point position, Point velocity, float angle, float angularVelocity)
        { Id = id; Size = size; Position = position; Velocity = velocity; Angle = angle; AngularVelocity = angularVelocity; }
        public int Id { get; }
        public int Size { get; }
        public Point Position { get; }
        public Point Velocity { get; }
        public float Angle { get; }
        public float AngularVelocity { get; }
    }
    public readonly struct Bullet
    {
        public Bullet(int id, Point position, Point velocity, int remainingTicks)
        { Id = id; Position = position; Velocity = velocity; RemainingTicks = remainingTicks; }
        public int Id { get; }
        public Point Position { get; }
        public Point Velocity { get; }
        public int RemainingTicks { get; }
    }
    public sealed class AsteroidsState
    {
        private readonly AsteroidsConfig _config;
        private readonly List<Asteroid> _asteroids = new(AsteroidsConfig.AsteroidCapacity);
        private readonly List<Bullet> _bullets = new(16);
        private readonly List<Hit> _hits = new(32 * 17);
        private readonly List<Asteroid> _children = new(32);
        private readonly bool[] _deadAsteroids = new bool[32];
        private readonly bool[] _deadBullets = new bool[16];
        private uint _random;
        private uint _splitRandom;
        private int _nextId;
        private int _cooldown;
        private int _wait;
        public AsteroidsState(AsteroidsConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate(); _config = config.Copy();
            _random = seed == 0 ? 0x9e3779b9u : unchecked((uint)seed);
            _splitRandom = _random;
            Seed = seed;
            Lives = _config.Lives; Wave = 1; SpawnWave();
        }
        public Point ShipPosition { get; private set; }
        public int Seed { get; }
        public Point ShipVelocity { get; private set; }
        public float ShipAngle { get; private set; }
        public float ShipAngularVelocity { get; private set; }
        public int TickCount { get; private set; }
        public int Wave { get; private set; }
        public int Lives { get; private set; }
        public int Score { get; private set; }
        public int InvincibleTicks { get; private set; }
        public GamePhase Phase { get; private set; }
        public int AsteroidCount => _asteroids.Count;
        public int BulletCount => _bullets.Count;
        public Asteroid AsteroidAt(int index) => _asteroids[index];
        public Bullet BulletAt(int index) => _bullets[index];
        private static float RandomUnit(ref uint random)
        {
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            return (random >> 8) / 16777216f;
        }
        private void ResetShip()
        {
            ShipPosition = default; ShipVelocity = default; ShipAngle = 0; ShipAngularVelocity = 0; _cooldown = 0;
        }
        private void SpawnWave()
        {
            _asteroids.Clear(); _bullets.Clear(); ResetShip();
            var count = _config.FirstWaveCount + (Wave - 1) * _config.WaveIncrease;
            var offset = (int)(RandomUnit(ref _random) * 8);
            for (var i = 0; i < count; i++)
            {
                var a = (offset + i) * MathF.PI / 4;
                var position = new Point(MathF.Cos(a) * 12, MathF.Sin(a) * 6);
                var velocity = Point.Forward(RandomUnit(ref _random) * MathF.PI * 2).Scale(_config.AsteroidMinSpeed + RandomUnit(ref _random) * (_config.AsteroidMaxSpeed - _config.AsteroidMinSpeed));
                _asteroids.Add(new Asteroid(_nextId++, 2, position, velocity, RandomUnit(ref _random) * MathF.PI * 2, (RandomUnit(ref _random) - .5f) * 2));
            }
            InvincibleTicks = AsteroidsConfig.Ticks(_config.InvincibleSeconds); Phase = GamePhase.Playing;
        }
        public void Step(PlayerCommand command)
        {
            if (Phase == GamePhase.Won || Phase == GamePhase.Lost) { return; }
            TickCount++;
            if (Phase != GamePhase.Playing)
            {
                if (--_wait == 0)
                {
                    if (Phase == GamePhase.WaveTransition) { Wave++; SpawnWave(); }
                    else { Phase = GamePhase.Playing; InvincibleTicks = AsteroidsConfig.Ticks(_config.InvincibleSeconds); }
                }
                return;
            }
            ShipAngularVelocity = Math.Clamp(command.Turn, -1, 1) * _config.RotationDegrees * MathF.PI / 180;
            ShipAngle = Torus.Wrap(ShipAngle + ShipAngularVelocity / AsteroidsConfig.TickRate, MathF.PI * 2);
            if (command.Thrust)
            {
                ShipVelocity = ShipVelocity.Plus(Point.Forward(ShipAngle).Scale(_config.Acceleration / AsteroidsConfig.TickRate));
                if (ShipVelocity.Length > _config.MaxSpeed) { ShipVelocity = ShipVelocity.Scale(_config.MaxSpeed / ShipVelocity.Length); }
            }
            if (_cooldown > 0) { _cooldown--; }
            if (command.Fire && _cooldown == 0 && _bullets.Count < _config.BulletLimit)
            {
                var forward = Point.Forward(ShipAngle);
                _bullets.Add(new Bullet(_nextId++, Torus.Wrap(ShipPosition.Plus(forward.Scale(AsteroidsConfig.ShipRadius + AsteroidsConfig.BulletRadius))),
                    ShipVelocity.Plus(forward.Scale(_config.BulletSpeed)), AsteroidsConfig.Ticks(_config.BulletSeconds)));
                _cooldown = AsteroidsConfig.Ticks(_config.FireSeconds);
            }
            ResolveContacts();
            if (InvincibleTicks > 0) { InvincibleTicks--; }
            if (Lives == 0) { Phase = GamePhase.Lost; }
            else if (_asteroids.Count == 0)
            {
                _bullets.Clear(); ResetShip();
                if (Wave == _config.WaveCount) { Phase = GamePhase.Won; }
                else { Phase = GamePhase.WaveTransition; _wait = Math.Max(_wait, AsteroidsConfig.Ticks(_config.TransitionSeconds)); }
            }
        }
        private void ResolveContacts()
        {
            _hits.Clear(); _children.Clear(); Array.Clear(_deadAsteroids, 0, _deadAsteroids.Length); Array.Clear(_deadBullets, 0, _deadBullets.Length);
            var shipDelta = ShipVelocity.Scale(1f / AsteroidsConfig.TickRate);
            for (var a = 0; a < _asteroids.Count; a++)
            {
                var asteroid = _asteroids[a]; var delta = asteroid.Velocity.Scale(1f / AsteroidsConfig.TickRate);
                for (var b = 0; b < _bullets.Count; b++)
                {
                    var bullet = _bullets[b];
                    var time = Torus.Contact(bullet.Position, bullet.Velocity.Scale(1f / AsteroidsConfig.TickRate), asteroid.Position, delta, _config.Radius(asteroid.Size) + AsteroidsConfig.BulletRadius);
                    if (time.HasValue) { _hits.Add(new Hit(time.Value, b, a, bullet.Id, asteroid.Id)); }
                }
                if (InvincibleTicks == 0)
                {
                    var time = Torus.Contact(ShipPosition, shipDelta, asteroid.Position, delta, _config.Radius(asteroid.Size) + AsteroidsConfig.ShipRadius);
                    if (time.HasValue) { _hits.Add(new Hit(time.Value, -1, a, int.MaxValue, asteroid.Id)); }
                }
            }
            _hits.Sort(Hit.Compare);
            var hitShip = false;
            foreach (var hit in _hits)
            {
                if (_deadAsteroids[hit.Asteroid]) { continue; }
                if (hit.Bullet < 0)
                {
                    if (hitShip) { continue; }
                    hitShip = true; Lives--; continue;
                }
                if (_deadBullets[hit.Bullet] || hitShip) { continue; }
                _deadBullets[hit.Bullet] = true; _deadAsteroids[hit.Asteroid] = true;
                var parent = _asteroids[hit.Asteroid]; Score += parent.Size == 2 ? 20 : parent.Size == 1 ? 50 : 100;
                if (parent.Size == 0) { continue; }
                var position = Torus.Wrap(parent.Position.Plus(parent.Velocity.Scale(hit.Time / AsteroidsConfig.TickRate)));
                var side = Point.Forward(RandomUnit(ref _splitRandom) * MathF.PI * 2).Scale(_config.SplitSpeed);
                for (var sign = -1; sign <= 1; sign += 2)
                {
                    _children.Add(new Asteroid(_nextId++, parent.Size - 1, position, parent.Velocity.Plus(side.Scale(sign)), parent.Angle, -parent.AngularVelocity));
                }
            }
            for (var a = _asteroids.Count - 1; a >= 0; a--)
            {
                if (_deadAsteroids[a]) { _asteroids.RemoveAt(a); continue; }
                var item = _asteroids[a];
                _asteroids[a] = new Asteroid(item.Id, item.Size, Torus.Wrap(item.Position.Plus(item.Velocity.Scale(1f / AsteroidsConfig.TickRate))), item.Velocity,
                    Torus.Wrap(item.Angle + item.AngularVelocity / AsteroidsConfig.TickRate, MathF.PI * 2), item.AngularVelocity);
            }
            _asteroids.AddRange(_children);
            if (_asteroids.Count > AsteroidsConfig.AsteroidCapacity) { throw new InvalidOperationException("分裂数が理論上限を超えました。"); }
            for (var b = _bullets.Count - 1; b >= 0; b--)
            {
                var item = _bullets[b];
                if (_deadBullets[b] || item.RemainingTicks <= 1 || hitShip) { _bullets.RemoveAt(b); continue; }
                _bullets[b] = new Bullet(item.Id, Torus.Wrap(item.Position.Plus(item.Velocity.Scale(1f / AsteroidsConfig.TickRate))), item.Velocity, item.RemainingTicks - 1);
            }
            if (hitShip)
            {
                ResetShip(); InvincibleTicks = 0; Phase = GamePhase.Respawning; _wait = AsteroidsConfig.Ticks(_config.RespawnSeconds);
            }
            else { ShipPosition = Torus.Wrap(ShipPosition.Plus(shipDelta)); }
        }
        private readonly struct Hit
        {
            public Hit(float time, int bullet, int asteroid, int sourceId, int targetId)
            { Time = time; Bullet = bullet; Asteroid = asteroid; SourceId = sourceId; TargetId = targetId; }
            public float Time { get; }
            public int Bullet { get; }
            public int Asteroid { get; }
            private int SourceId { get; }
            private int TargetId { get; }
            public static int Compare(Hit a, Hit b)
            {
                var c = a.Time.CompareTo(b.Time); if (c != 0) { return c; }
                c = (a.Bullet < 0).CompareTo(b.Bullet < 0); if (c != 0) { return c; }
                c = a.SourceId.CompareTo(b.SourceId); return c != 0 ? c : a.TargetId.CompareTo(b.TargetId);
            }
        }
    }
}
