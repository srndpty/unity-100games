using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TileMotion;

namespace Game.Core
{
    public enum RunStatus
    {
        Playing,

        /// <summary>試合の長さを生き残った。</summary>
        Won,

        /// <summary>残機が 0 になった。</summary>
        Lost,
    }

    /// <summary>1 Tick に起きた出来事（音・表示用）。</summary>
    [Flags]
    public enum RunEvents
    {
        None = 0,
        Fired = 1 << 0,
        EnemyWarned = 1 << 1,
        EnemyActivated = 1 << 2,
        EnemyHit = 1 << 3,
        EnemyDestroyed = 1 << 4,
        EnemyCulled = 1 << 5,
        Hurt = 1 << 6,
        Won = 1 << 7,
        Lost = 1 << 8,
    }

    /// <summary>
    /// 試合 1 回。1 Tick の順序：
    /// 時間を進める（無敵・連射・予告・点滅）→ 自機の移動 → 照準 → 射撃 → 弾の移動 → 敵の移動 → 弾×敵 → 自機×敵 → 画面外の除去 → 出現 → 時間切れの判定。
    /// 同じ Tick では弾の当たりを自機の被弾より先に解決する（撃ち落とした敵には触れない）。
    /// </summary>
    public sealed class SiegeRun
    {
        private readonly SiegeConfig _config;
        private List<Enemy> _enemies = new();
        private List<Bullet> _bullets = new();
        private SeededRandom _placeRandom;
        private SeededRandom _kindRandom;
        private SeededRandom _behaviorRandom;
        private int _spawnProgress;
        private int _nextId = 1;
        private RunEvents _events;

        /// <param name="config">設定（検証して複製する）。</param>
        /// <param name="seed">試合のシード。出現位置・種類・敵の行動で系列を分ける。</param>
        public SiegeRun(SiegeConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            Seed = seed;
            _placeRandom = new SeededRandom(unchecked(seed ^ 0x504C4143));
            _kindRandom = new SeededRandom(unchecked(seed ^ 0x4B494E44));
            _behaviorRandom = new SeededRandom(unchecked(seed ^ 0x42484156));
            Ship = new Ship(new Vec2i(_config.ArenaWidth / 2, _config.ArenaHeight / 2), _config.Lives, _config.BulletSpeed);
            Enemies = _enemies.AsReadOnly();
            Bullets = _bullets.AsReadOnly();
        }

        public SiegeConfig Config => _config.Copy();

        public int Seed { get; }

        public RunStatus Status { get; private set; }

        /// <summary>進めた Tick 数。</summary>
        public int Elapsed { get; private set; }

        public int RemainingTicks => Math.Max(0, _config.DurationTicks - Elapsed);

        public long Score { get; private set; }

        /// <summary>撃破数。</summary>
        public int Kills { get; private set; }

        /// <summary>試合中に出した敵の数（予告を出した数）。</summary>
        public int Spawned { get; private set; }

        public Ship Ship { get; private set; }

        /// <summary>生きている敵（予告中を含む）。数は常に <see cref="SiegeConfig.MaxAlive"/> 以下。</summary>
        public ReadOnlyCollection<Enemy> Enemies { get; private set; }

        public ReadOnlyCollection<Bullet> Bullets { get; private set; }

        /// <summary>画面に映る範囲（自機の位置から決まる）。</summary>
        public Rect View => Game.Core.View.Of(Ship.Position, _config.ArenaWidth, _config.ArenaHeight, _config.ViewWidth, _config.ViewHeight);

        public bool IsOver => Status != RunStatus.Playing;

        /// <summary>1 Tick 進める。終わった試合では何もしない。</summary>
        public void Step(ShipCommand command)
        {
            if (IsOver) { return; }
            Elapsed++;
            AdvanceTimers();

            var shipStart = Ship.Position;
            MoveShip(command);
            Aim(command.Aim);
            if (command.Fire) { Fire(); }

            var bulletStarts = new Vec2i[_bullets.Count];
            for (var i = 0; i < _bullets.Count; i++)
            {
                bulletStarts[i] = _bullets[i].Position;
                _bullets[i].Position += _bullets[i].Velocity;
            }

            var enemyStarts = new Vec2i[_enemies.Count];
            for (var i = 0; i < _enemies.Count; i++)
            {
                enemyStarts[i] = _enemies[i].Position;
                _enemies[i].Move(_config, Ship.Position);
            }

            ResolveBullets(bulletStarts, enemyStarts);
            ResolveShip(shipStart, enemyStarts);
            RemoveDead();
            CullOffscreen();
            if (Status == RunStatus.Playing)
            {
                SpawnStep();
                if (Elapsed >= _config.DurationTicks)
                {
                    Status = RunStatus.Won;
                    Score += (long)Ship.Lives * _config.LifeBonus;
                    _events |= RunEvents.Won;
                }
            }
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public RunEvents TakeEvents()
        {
            var events = _events;
            _events = RunEvents.None;
            return events;
        }

        /// <summary>以後の動きを決める状態をすべて混ぜたハッシュ。</summary>
        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix((int)Status);
            hash.Mix(Elapsed);
            hash.Mix(Score);
            hash.Mix(Kills);
            hash.Mix(Spawned);
            hash.Mix(_spawnProgress);
            hash.Mix(_nextId);
            hash.Mix(_placeRandom.State);
            hash.Mix(_kindRandom.State);
            hash.Mix(_behaviorRandom.State);
            Ship.Mix(ref hash);
            hash.Mix(_enemies.Count);
            foreach (var enemy in _enemies) { enemy.Mix(ref hash); }
            hash.Mix(_bullets.Count);
            foreach (var bullet in _bullets) { bullet.Mix(ref hash); }
            return hash.Value;
        }

        /// <summary>以後の進行を共有しない複製（先読み・テスト用）。</summary>
        public SiegeRun Clone()
        {
            var copy = (SiegeRun)MemberwiseClone();
            copy._placeRandom = _placeRandom.Clone();
            copy._kindRandom = _kindRandom.Clone();
            copy._behaviorRandom = _behaviorRandom.Clone();
            copy.Ship = Ship.Clone();
            copy._enemies = _enemies.ConvertAll(enemy => enemy.Clone());
            copy._bullets = _bullets.ConvertAll(bullet => bullet.Clone());
            copy.Enemies = copy._enemies.AsReadOnly();
            copy.Bullets = copy._bullets.AsReadOnly();
            return copy;
        }

        /// <summary>
        /// 敵を直接置く（テスト・撮影用）。<paramref name="active"/> が false なら予告から始める。
        /// 上限（<see cref="SiegeConfig.MaxAlive"/>）を超える置き方は拒否する。
        /// </summary>
        public Enemy Place(EnemyKind kind, Vec2i position, Vec2i velocity = default, bool active = true)
        {
            if (IsOver) { throw new InvalidOperationException("終わった試合には置けません。"); }
            if (_enemies.Count >= _config.MaxAlive) { throw new InvalidOperationException("敵の数の上限に達しています。"); }
            var enemy = Create(kind, position, velocity);
            if (active) { Activate(enemy); }
            else { enemy.WarnTimer.Arm(_config.SpawnWarnTicks); }
            return enemy;
        }

        /// <summary>自機を置く（テスト・撮影用）。アリーナの内側に寄せる。</summary>
        public void PlaceShip(Vec2i position)
        {
            Ship.Position = ClampShip(position);
        }

        private void AdvanceTimers()
        {
            Ship.InvincibleTimer.Tick();
            Ship.FireTimer.Tick();
            foreach (var bullet in _bullets) { bullet.Life.Tick(); }
            _bullets.RemoveAll(bullet => !bullet.Life.IsActive);
            foreach (var enemy in _enemies)
            {
                enemy.FlashTimer.Tick();
                if (enemy.IsActive) { continue; }
                enemy.WarnTimer.Tick();
                if (!enemy.WarnTimer.IsActive)
                {
                    Activate(enemy);
                    _events |= RunEvents.EnemyActivated;
                }
            }
        }

        private void MoveShip(ShipCommand command)
        {
            var speed = command.MoveX != 0 && command.MoveY != 0 ? SiegeConfig.DiagonalOf(_config.ShipSpeed) : _config.ShipSpeed;
            Ship.Position = ClampShip(Ship.Position + new Vec2i(command.MoveX * speed, command.MoveY * speed));
        }

        private Vec2i ClampShip(Vec2i position) => new(
            IntMath.Clamp(position.X, _config.ShipRadius, _config.ArenaWidth - _config.ShipRadius),
            IntMath.Clamp(position.Y, _config.ShipRadius, _config.ArenaHeight - _config.ShipRadius));

        /// <summary>照準：移動の後の自機から照準点への向き。照準点が自機に重なれば直前の向きを保つ。</summary>
        private void Aim(Vec2i point)
        {
            var velocity = IntMath.Scale(point - Ship.Position, _config.BulletSpeed);
            if (!velocity.IsZero) { Ship.ShotVelocity = velocity; }
        }

        private void Fire()
        {
            if (Ship.FireTimer.IsActive) { return; }

            // 寿命 L・間隔 I の弾は同時に ceil(L / I) 発までしか存在しない。検証を通った設定では上限を超えない（超えたら壊れている）。
            if (_bullets.Count >= _config.BulletCapacity) { throw new InvalidOperationException("弾の数が上限を超えます。"); }
            _bullets.Add(new Bullet(_nextId++, Ship.Position, Ship.ShotVelocity, _config.BulletLifeTicks));
            Ship.FireTimer.Arm(_config.FireIntervalTicks);
            _events |= RunEvents.Fired;
        }

        /// <summary>弾ごとに、Tick の中で最初に触れた敵 1 体にだけ当てる。当たった弾はアリーナの外に出た弾と一緒に消す。</summary>
        private void ResolveBullets(Vec2i[] bulletStarts, Vec2i[] enemyStarts)
        {
            var arena = new Rect(0, 0, _config.ArenaWidth, _config.ArenaHeight);
            var spent = new bool[_bullets.Count];
            for (var b = 0; b < _bullets.Count; b++)
            {
                var bullet = _bullets[b];
                var target = -1;
                var first = Touch.None;
                for (var e = 0; e < _enemies.Count; e++)
                {
                    var enemy = _enemies[e];
                    if (!enemy.IsActive || enemy.Health <= 0) { continue; }
                    var touch = Sweep.First(bulletStarts[b], bullet.Position, enemyStarts[e], enemy.Position, _config.BulletRadius + enemy.Radius);
                    if (touch.IsEarlierThan(first))
                    {
                        first = touch;
                        target = e;
                    }
                }

                if (target >= 0)
                {
                    spent[b] = true;
                    Damage(_enemies[target]);
                }
                else if (!arena.Contains(bullet.Position))
                {
                    spent[b] = true;
                }
            }

            for (var b = _bullets.Count - 1; b >= 0; b--)
            {
                if (spent[b]) { _bullets.RemoveAt(b); }
            }
        }

        private void Damage(Enemy enemy)
        {
            enemy.Health--;
            enemy.FlashTimer.Arm(Enemy.FlashTicks);
            _events |= RunEvents.EnemyHit;
            if (enemy.Health > 0) { return; }
            Score += _config.StatsOf(enemy.Kind).Score;
            Kills++;
            _events |= RunEvents.EnemyDestroyed;
        }

        /// <summary>無敵でなければ、Tick の中で最初に触れた敵 1 体で被弾する（その敵は得点なしで消える）。</summary>
        private void ResolveShip(Vec2i shipStart, Vec2i[] enemyStarts)
        {
            if (Ship.IsInvincible) { return; }
            var target = -1;
            var first = Touch.None;
            for (var e = 0; e < _enemies.Count; e++)
            {
                var enemy = _enemies[e];
                if (!enemy.IsActive || enemy.Health <= 0) { continue; }
                var touch = Sweep.First(shipStart, Ship.Position, enemyStarts[e], enemy.Position, _config.ShipRadius + enemy.Radius);
                if (touch.IsEarlierThan(first))
                {
                    first = touch;
                    target = e;
                }
            }

            if (target < 0) { return; }
            _enemies[target].Health = 0;
            Ship.Lives--;
            Ship.InvincibleTimer.Arm(_config.InvincibleTicks);
            _events |= RunEvents.Hurt;
            if (Ship.Lives > 0) { return; }
            Status = RunStatus.Lost;
            _events |= RunEvents.Lost;
        }

        private void RemoveDead() => _enemies.RemoveAll(enemy => enemy.Health <= 0);

        /// <summary>画面外に <see cref="SiegeConfig.OffscreenLimitTicks"/> Tick 続けて居た敵を、得点なしで消す（予告中も数える）。</summary>
        private void CullOffscreen()
        {
            var view = View;
            var culled = _enemies.RemoveAll(enemy =>
            {
                enemy.OffscreenTicks = view.Overlaps(enemy.Position, enemy.Radius) ? 0 : enemy.OffscreenTicks + 1;
                return enemy.OffscreenTicks >= _config.OffscreenLimitTicks;
            });
            if (culled > 0) { _events |= RunEvents.EnemyCulled; }
        }

        /// <summary>
        /// 出現の制御：区間の間隔で予算を貯め、間隔 1 回分で頭打ちにする（上限から空いた瞬間にまとめて湧かない）。
        /// 生存数が上限に達している間・位置が見つからない間は出さず、1 Tick に出すのは最大 1 体。
        /// </summary>
        private void SpawnStep()
        {
            var stage = _config.StageAt(Elapsed - 1);
            _spawnProgress = Math.Min(_spawnProgress + 1, stage.IntervalTicks);
            if (_spawnProgress < stage.IntervalTicks || _enemies.Count >= _config.MaxAlive) { return; }
            if (!TryPickPosition(out var position)) { return; }
            var kind = PickKind(stage);
            var velocity = kind == EnemyKind.Bouncer ? RandomDirection(_config.Bouncer.Speed) : Vec2i.Zero;
            var enemy = Create(kind, position, velocity);
            enemy.WarnTimer.Arm(_config.SpawnWarnTicks);
            _spawnProgress = 0;
            Spawned++;
            _events |= RunEvents.EnemyWarned;
        }

        /// <summary>画面の内側（いちばん大きい敵が収まる範囲）で、自機から安全距離以上離れた点を探す。</summary>
        private bool TryPickPosition(out Vec2i position)
        {
            var view = View;
            var margin = Math.Max(_config.Chaser.Radius, Math.Max(_config.Bouncer.Radius, _config.Lancer.Radius));
            var safe = (long)_config.SafeRadius * _config.SafeRadius;
            for (var i = 0; i < _config.SpawnAttempts; i++)
            {
                var candidate = new Vec2i(
                    _placeRandom.Range(view.MinX + margin, view.MaxX - margin),
                    _placeRandom.Range(view.MinY + margin, view.MaxY - margin));
                if (Vec2i.DistanceSquared(candidate, Ship.Position) >= safe)
                {
                    position = candidate;
                    return true;
                }
            }

            position = default;
            return false;
        }

        private EnemyKind PickKind(SpawnStage stage)
        {
            var roll = _kindRandom.Next(stage.ChaserWeight + stage.BouncerWeight + stage.LancerWeight);
            if (roll < stage.ChaserWeight) { return EnemyKind.Chaser; }
            return roll < stage.ChaserWeight + stage.BouncerWeight ? EnemyKind.Bouncer : EnemyKind.Lancer;
        }

        /// <summary>行動の乱数で、長さ <paramref name="speed"/> の向きを選ぶ。</summary>
        private Vec2i RandomDirection(int speed)
        {
            while (true)
            {
                var direction = new Vec2i(_behaviorRandom.Range(-256, 256), _behaviorRandom.Range(-256, 256));
                var velocity = IntMath.Scale(direction, speed);
                if (!velocity.IsZero) { return velocity; }
            }
        }

        private Enemy Create(EnemyKind kind, Vec2i position, Vec2i velocity)
        {
            var stats = _config.StatsOf(kind);
            var clamped = new Vec2i(
                IntMath.Clamp(position.X, stats.Radius, _config.ArenaWidth - stats.Radius),
                IntMath.Clamp(position.Y, stats.Radius, _config.ArenaHeight - stats.Radius));
            var enemy = new Enemy(_nextId++, kind, stats, clamped, velocity);
            _enemies.Add(enemy);
            return enemy;
        }

        private static void Activate(Enemy enemy)
        {
            enemy.Phase = EnemyPhase.Active;
            enemy.WarnTimer.Clear();
            enemy.Mode = LancerMode.Rest;
            enemy.ModeTicks = 0;
        }
    }
}
