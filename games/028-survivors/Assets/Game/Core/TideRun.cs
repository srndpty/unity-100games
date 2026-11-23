using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    public enum RunStatus
    {
        Playing,

        /// <summary>ボスを倒した。</summary>
        Won,

        /// <summary>体力が 0 になった、またはボスの猶予が尽きた。</summary>
        Lost,
    }

    public enum RunEnd
    {
        None,
        BossDefeated,
        Died,

        /// <summary>ボスが出てから <see cref="TideConfig.BossTimeLimitTicks"/> の間に倒せなかった。</summary>
        TimeUp,
    }

    /// <summary>1 Tick に起きた出来事（音・表示用）。</summary>
    [Flags]
    public enum RunEvents
    {
        None = 0,
        BoltFired = 1 << 0,
        Pulsed = 1 << 1,
        Lashed = 1 << 2,
        Arced = 1 << 3,
        EnemyHit = 1 << 4,
        EnemyKilled = 1 << 5,
        GemCollected = 1 << 6,
        LevelUp = 1 << 7,
        Hurt = 1 << 8,
        BossSpawned = 1 << 9,
        Won = 1 << 10,
        Lost = 1 << 11,
    }

    /// <summary>1 Tick の自機の操作（移動だけ。攻撃は自動）。</summary>
    public readonly struct PlayerCommand : IEquatable<PlayerCommand>
    {
        public PlayerCommand(int moveX, int moveY)
        {
            if (moveX < -1 || moveX > 1) { throw new ArgumentOutOfRangeException(nameof(moveX), moveX, "-1〜1 で指定してください。"); }
            if (moveY < -1 || moveY > 1) { throw new ArgumentOutOfRangeException(nameof(moveY), moveY, "-1〜1 で指定してください。"); }
            MoveX = moveX;
            MoveY = moveY;
        }

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int MoveX { get; }

        /// <summary>下 -1・なし 0・上 1。</summary>
        public int MoveY { get; }

        public static PlayerCommand Idle => default;

        public static bool operator ==(PlayerCommand left, PlayerCommand right) => left.Equals(right);

        public static bool operator !=(PlayerCommand left, PlayerCommand right) => !left.Equals(right);

        public bool Equals(PlayerCommand other) => MoveX == other.MoveX && MoveY == other.MoveY;

        public override bool Equals(object? obj) => obj is PlayerCommand other && Equals(other);

        public override int GetHashCode() => (MoveX * 3) + MoveY;

        public override string ToString() => $"Move({MoveX}, {MoveY})";
    }

    public enum ChoiceKind
    {
        /// <summary>持っていない武器を Lv1 で得る。</summary>
        Acquire,

        /// <summary>持っている武器のレベルを 1 上げる。</summary>
        Upgrade,

        /// <summary>体力を <see cref="TideConfig.HealAmount"/> 戻す（上げられる武器が 3 つ未満のときの埋め草）。</summary>
        Heal,
    }

    /// <summary>レベルアップの候補 1 つ。</summary>
    public readonly struct LevelChoice : IEquatable<LevelChoice>
    {
        public LevelChoice(ChoiceKind kind, int weapon, int level)
        {
            Kind = kind;
            Weapon = weapon;
            Level = level;
        }

        public ChoiceKind Kind { get; }

        /// <summary>武器の番号（<see cref="ChoiceKind.Heal"/> では -1）。</summary>
        public int Weapon { get; }

        /// <summary>選んだ後のレベル（<see cref="ChoiceKind.Heal"/> では 0）。</summary>
        public int Level { get; }

        public static bool operator ==(LevelChoice left, LevelChoice right) => left.Equals(right);

        public static bool operator !=(LevelChoice left, LevelChoice right) => !left.Equals(right);

        public bool Equals(LevelChoice other) => Kind == other.Kind && Weapon == other.Weapon && Level == other.Level;

        public override bool Equals(object? obj) => obj is LevelChoice other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397) ^ (Weapon * 31) ^ Level;

        public override string ToString() => $"{Kind}(武器 {Weapon} → Lv{Level})";
    }

    /// <summary>
    /// 試合 1 回。1 Tick の順序：
    /// 時間（無敵・点滅・Orbit の再ヒット待ち）→ 自機の移動 → 敵の向き（位相の合う敵だけ）と移動 → 押し合い → 近傍グリッドの作り直し →
    /// 武器の発動（定義の順）→ 弾の移動と当たり → 自機×敵 → 宝石の吸い寄せと取得 → 倒れた敵を詰める → レベルアップ → 置き直し → 出現 → ボス・時間切れ。
    /// <para>
    /// 武器が倒した敵は同じ Tick に自機へ触れない。レベルアップの 3 択が出ている間（<see cref="IsChoosing"/>）は <see cref="Step"/> で進まず、
    /// <see cref="Choose"/> で選ぶまで止まる。<see cref="Step"/> は割り当てをしない（容量はすべて設定から作る）。
    /// </para>
    /// </summary>
    public sealed partial class TideRun
    {
        public const int ChoiceSlots = 3;

        private readonly TideConfig _config;
        private readonly int _bossDef;
        private readonly int _largestRadius;
        private EnemySet _enemies;
        private ProjectileSet _projectiles;
        private GemSet _gems;
        private SeededRandom _placeRandom;
        private SeededRandom _kindRandom;
        private SeededRandom _choiceRandom;
        private int[] _weaponLevel;
        private int[] _weaponCooldown;
        private int[] _orbitAngle;
        private LevelChoice[] _choices;
        private int _choiceCount;
        private int _spawnProgress;
        private int _nextId = 1;
        private Countdown _invincible;
        private RunEvents _events;

        // 1 Tick の中だけで使う作業領域（状態ではない。複製では作り直す）。
        private SpatialGrid _grid = null!;
        private int[] _candidates = null!;
        private int[] _pushX = null!;
        private int[] _pushY = null!;
        private int[] _stamp = null!;
        private int _stampValue;
        private LevelChoice[] _pool = null!;

        /// <param name="config">設定（検証して複製する）。</param>
        /// <param name="seed">試合のシード。出現位置・種類・3 択で系列を分ける。</param>
        public TideRun(TideConfig config, int seed)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            _config = config.Copy();
            Seed = seed;
            _bossDef = _config.BossIndex;
            _largestRadius = _config.LargestEnemyRadius;
            _placeRandom = new SeededRandom(unchecked(seed ^ 0x504C4143));
            _kindRandom = new SeededRandom(unchecked(seed ^ 0x4B494E44));
            _choiceRandom = new SeededRandom(unchecked(seed ^ 0x43484F49));
            _enemies = new EnemySet(_config.MaxEnemies + 1);
            _projectiles = new ProjectileSet(_config.MaxProjectiles);
            _gems = new GemSet(_config.MaxGems);
            _weaponLevel = new int[_config.Weapons.Length];
            _weaponCooldown = new int[_config.Weapons.Length];
            _orbitAngle = new int[_config.Weapons.Length];
            _choices = new LevelChoice[ChoiceSlots];
            _weaponLevel[_config.StartingWeapon] = 1;
            Health = _config.PlayerHealth;
            Level = 1;
            FacingX = 1;
            Effects = new WeaponEffects();
            CreateScratch();
        }

        public TideConfig Config => _config.Copy();

        public int Seed { get; }

        public RunStatus Status { get; private set; }

        public RunEnd End { get; private set; }

        public bool IsOver => Status != RunStatus.Playing;

        /// <summary>進めた Tick 数（3 択の間は進まない）。</summary>
        public int Elapsed { get; private set; }

        /// <summary>ボスが出るまでの残り Tick（出た後は 0）。</summary>
        public int TicksToBoss => Math.Max(0, _config.DurationTicks - Elapsed);

        /// <summary>ボスの猶予の残り Tick（出る前は猶予の全体）。</summary>
        public int BossTicksLeft => Math.Min(_config.BossTimeLimitTicks, _config.MaxRunTicks - Elapsed);

        public Vec2i Player { get; private set; }

        /// <summary>この Tick の始めの自機の位置。</summary>
        public Vec2i PlayerStart { get; private set; }

        public int Health { get; private set; }

        /// <summary>最後に動いた向き（-1〜1 の組。動いていなければ 0, 0）。</summary>
        public Vec2i Facing { get; private set; }

        /// <summary>最後に左右へ動いた向き（-1 か 1）。Lash はこの向きを斬る。</summary>
        public int FacingX { get; private set; }

        public bool IsInvincible => _invincible.IsActive;

        public int Level { get; private set; }

        /// <summary>今のレベルで貯めた経験値（次のレベルで 0 から数え直す）。</summary>
        public long Experience { get; private set; }

        public int ExperienceToNext => _config.ExperienceToNext(Level);

        /// <summary>試合で取った経験値の合計（取った宝石の値の合計と常に等しい）。</summary>
        public long TotalExperience { get; private set; }

        public int GemsCollected { get; private set; }

        /// <summary>試合で落とした経験値の合計（直接置いた宝石を含む）。常に <see cref="TotalExperience"/> + 落ちている宝石の値の合計に等しい。</summary>
        public long ExperienceDropped { get; private set; }

        /// <summary>まだ選んでいないレベルアップの数（表示中の 3 択を含む）。</summary>
        public int PendingLevelUps { get; private set; }

        public bool IsChoosing => _choiceCount > 0;

        public int ChoiceCount => _choiceCount;

        public int Kills { get; private set; }

        /// <summary>試合中に出した敵の数（置き直しは数えない）。</summary>
        public int Spawned { get; private set; }

        public bool BossSpawned { get; private set; }

        public EnemySet Enemies => _enemies;

        public ProjectileSet Projectiles => _projectiles;

        public GemSet Gems => _gems;

        /// <summary>直近の Pulse・Lash・Arc の発動（描画用。以後の進行には使わない）。</summary>
        public WeaponEffects Effects { get; private set; }

        /// <summary>画面に映る範囲（自機を中心にした視野）。カメラも同じ関数で決める。</summary>
        public Rect View => ViewOf(Player, _config.ViewWidth, _config.ViewHeight);

        /// <summary>敵の定義 <paramref name="def"/> の値（複製）。</summary>
        public EnemyDef EnemyDefOf(int def) => _config.Enemies[def].Copy();

        /// <summary>敵 <paramref name="i"/> の半径。</summary>
        public int EnemyRadius(int i) => _config.Enemies[_enemies.Def(i)].Radius;

        /// <summary>武器 <paramref name="weapon"/> のレベル（持っていなければ 0）。</summary>
        public int WeaponLevelOf(int weapon) => _weaponLevel[CheckWeapon(weapon)];

        /// <summary>武器 <paramref name="weapon"/> の今の値（複製。持っていなければ null）。</summary>
        public WeaponLevel? WeaponStatsOf(int weapon)
        {
            var level = WeaponLevelOf(weapon);
            return level == 0 ? null : _config.Weapons[weapon].At(level).Copy();
        }

        /// <summary>武器 <paramref name="weapon"/> の今のレベルの「数」（持っていなければ 0。描画用に複製しない）。</summary>
        public int WeaponCountOf(int weapon)
        {
            var level = WeaponLevelOf(weapon);
            return level == 0 ? 0 : _config.Weapons[weapon].At(level).Count;
        }

        /// <summary>武器 <paramref name="weapon"/> の今のレベルの「大きさ」（持っていなければ 0）。</summary>
        public int WeaponSizeOf(int weapon)
        {
            var level = WeaponLevelOf(weapon);
            return level == 0 ? 0 : _config.Weapons[weapon].At(level).Size;
        }

        /// <summary>武器 <paramref name="weapon"/> が次に発動するまでの Tick（0 なら次の Tick に発動を試みる）。</summary>
        public int WeaponCooldownOf(int weapon) => _weaponCooldown[CheckWeapon(weapon)];

        /// <summary>Orbit の刃の基準の角度。</summary>
        public int OrbitAngleOf(int weapon) => _orbitAngle[CheckWeapon(weapon)];

        /// <summary>Orbit の刃 <paramref name="blade"/> の今の位置。</summary>
        public Vec2i OrbitBlade(int weapon, int blade)
        {
            var level = WeaponLevelOf(weapon);
            if (level == 0) { throw new InvalidOperationException("持っていない武器です。"); }
            var stats = _config.Weapons[weapon].At(level);
            if (blade < 0 || blade >= stats.Count) { throw new ArgumentOutOfRangeException(nameof(blade)); }
            return BladeAt(Player, _orbitAngle[weapon], blade, stats);
        }

        public LevelChoice ChoiceAt(int index)
        {
            if (index < 0 || index >= _choiceCount) { throw new ArgumentOutOfRangeException(nameof(index), index, "表示中の候補の番号ではありません。"); }
            return _choices[index];
        }

        /// <summary>中心 <paramref name="center"/> の視野の矩形。</summary>
        public static Rect ViewOf(Vec2i center, int width, int height)
        {
            var minX = center.X - (width / 2);
            var minY = center.Y - (height / 2);
            return new Rect(minX, minY, minX + width, minY + height);
        }

        /// <summary>1 Tick 進める。終わった試合・3 択の間は何もしない。</summary>
        public void Step(PlayerCommand command)
        {
            if (IsOver || IsChoosing) { return; }
            Elapsed++;
            AdvanceTimers();
            MovePlayer(command);
            MoveEnemies();
            Separate();
            _grid.Build(Player, _enemies.Count, _enemies.X, _enemies.Y);
            FireWeapons();
            MoveProjectiles();
            if (!IsOver) { TouchPlayer(); }
            if (!IsOver) { CollectGems(); }
            _enemies.RemoveDead();
            if (IsOver) { return; }
            GainLevels();
            Relocate();
            SpawnStep();
            BossStep();
        }

        /// <summary>表示中の 3 択から <paramref name="index"/> 番を選ぶ。まだレベルアップが残っていれば次の 3 択を出す。</summary>
        public void Choose(int index)
        {
            if (IsOver) { throw new InvalidOperationException("終わった試合では選べません。"); }
            if (!IsChoosing) { throw new InvalidOperationException("選べる候補がありません。"); }
            var choice = ChoiceAt(index);
            switch (choice.Kind)
            {
                case ChoiceKind.Acquire:
                    _weaponLevel[choice.Weapon] = 1;
                    _weaponCooldown[choice.Weapon] = 0;
                    break;
                case ChoiceKind.Upgrade:
                    _weaponLevel[choice.Weapon] = choice.Level;
                    _weaponCooldown[choice.Weapon] = Math.Min(_weaponCooldown[choice.Weapon], _config.Weapons[choice.Weapon].At(choice.Level).IntervalTicks);
                    break;
                default:
                    Health = Math.Min(_config.PlayerHealth, Health + _config.HealAmount);
                    break;
            }

            PendingLevelUps--;
            _choiceCount = 0;
            if (PendingLevelUps > 0) { MakeChoices(); }
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public RunEvents TakeEvents()
        {
            var events = _events;
            _events = RunEvents.None;
            return events;
        }

        /// <summary>以後の動きを決める状態をすべて混ぜたハッシュ（022 の知見）。</summary>
        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix(Seed);
            hash.Mix((int)Status);
            hash.Mix((int)End);
            hash.Mix(Elapsed);
            hash.Mix(Player.X);
            hash.Mix(Player.Y);
            hash.Mix(Health);
            hash.Mix(Facing.X);
            hash.Mix(Facing.Y);
            hash.Mix(FacingX);
            hash.Mix(_invincible.Remaining);
            hash.Mix(Level);
            hash.Mix(Experience);
            hash.Mix(TotalExperience);
            hash.Mix(GemsCollected);
            hash.Mix(ExperienceDropped);
            hash.Mix(PendingLevelUps);
            hash.Mix(_choiceCount);
            for (var i = 0; i < _choiceCount; i++)
            {
                hash.Mix((int)_choices[i].Kind);
                hash.Mix(_choices[i].Weapon);
                hash.Mix(_choices[i].Level);
            }

            hash.Mix(Kills);
            hash.Mix(Spawned);
            hash.Mix(BossSpawned);
            hash.Mix(_spawnProgress);
            hash.Mix(_nextId);
            hash.Mix(_placeRandom.State);
            hash.Mix(_kindRandom.State);
            hash.Mix(_choiceRandom.State);
            for (var w = 0; w < _weaponLevel.Length; w++)
            {
                hash.Mix(_weaponLevel[w]);
                hash.Mix(_weaponCooldown[w]);
                hash.Mix(_orbitAngle[w]);
            }

            _enemies.Mix(ref hash);
            _projectiles.Mix(ref hash);
            _gems.Mix(ref hash);
            return hash.Value;
        }

        /// <summary>以後の進行を共有しない複製（先読み・テスト用）。</summary>
        public TideRun Clone()
        {
            var copy = (TideRun)MemberwiseClone();
            copy._enemies = _enemies.Clone();
            copy._projectiles = _projectiles.Clone();
            copy._gems = _gems.Clone();
            copy._placeRandom = _placeRandom.Clone();
            copy._kindRandom = _kindRandom.Clone();
            copy._choiceRandom = _choiceRandom.Clone();
            copy._weaponLevel = (int[])_weaponLevel.Clone();
            copy._weaponCooldown = (int[])_weaponCooldown.Clone();
            copy._orbitAngle = (int[])_orbitAngle.Clone();
            copy._choices = (LevelChoice[])_choices.Clone();
            copy.Effects = Effects.Clone();
            copy.CreateScratch();
            return copy;
        }

        /// <summary>敵を直接置く（テスト・撮影用）。上限（ボスを除いて <see cref="TideConfig.MaxEnemies"/>）を超える置き方は拒否する。</summary>
        public int Place(int def, Vec2i position, Vec2i velocity = default)
        {
            if (IsOver) { throw new InvalidOperationException("終わった試合には置けません。"); }
            if (def < 0 || def >= _config.Enemies.Length) { throw new ArgumentOutOfRangeException(nameof(def), def, "未定義の敵です。"); }
            if (def == _bossDef)
            {
                if (BossSpawned) { throw new InvalidOperationException("ボスはもう出ています。"); }
                BossSpawned = true;
            }
            else if (NormalCount >= _config.MaxEnemies) { throw new InvalidOperationException("敵の数の上限に達しています。"); }

            return _enemies.Add(_nextId++, def, position, velocity, _config.Enemies[def].Health, Elapsed);
        }

        /// <summary>宝石を直接置く（テスト・撮影用）。満杯なら近い宝石に値を足す。</summary>
        public void PlaceGem(Vec2i position, int value)
        {
            if (value < 1 || value > TideConfig.MaxExperience) { throw new ArgumentOutOfRangeException(nameof(value)); }
            DropGem(position, value);
        }

        /// <summary>自機を置く（テスト・撮影用）。</summary>
        public void PlacePlayer(Vec2i position)
        {
            Player = position;
            PlayerStart = position;
        }

        /// <summary>
        /// 武器のレベルを直接決める（テスト・撮影用。0 で手放す）。発動までの Tick は 0 にする。
        /// レベルを下げたり待ちを縮めたりできるので、弾の容量の上界（<see cref="TideConfig.ProjectileCapacityOf"/>）を守るため、弾が残っている間は拒否する。
        /// </summary>
        public void SetWeaponLevel(int weapon, int level)
        {
            if (level < 0 || level > _config.Weapons[CheckWeapon(weapon)].MaxLevel) { throw new ArgumentOutOfRangeException(nameof(level)); }
            if (_projectiles.Count > 0) { throw new InvalidOperationException("弾が残っている間は武器のレベルを直接変えられません。"); }
            _weaponLevel[weapon] = level;
            _weaponCooldown[weapon] = 0;
        }

        /// <summary>ボスを除いた敵の数。</summary>
        public int NormalCount
        {
            get
            {
                var count = _enemies.Count;
                if (BossSpawned && BossIndex() >= 0) { count--; }
                return count;
            }
        }

        /// <summary>生きているボスの番号（いなければ -1）。</summary>
        public int BossIndex()
        {
            for (var i = 0; i < _enemies.Count; i++)
            {
                if (_enemies.DefOf[i] == _bossDef && _enemies.Hp[i] > 0) { return i; }
            }

            return -1;
        }

        private void CreateScratch()
        {
            var capacity = _enemies.Capacity;
            _grid = new SpatialGrid(_config.GridCells, _config.GridCellSize, capacity);
            _candidates = new int[capacity];
            _pushX = new int[capacity];
            _pushY = new int[capacity];
            _stamp = new int[capacity];
            _stampValue = 0;
            _pool = new LevelChoice[_config.Weapons.Length];
            _nearIndex = new int[TideConfig.MaxCount];
            _nearDistance = new long[TideConfig.MaxCount];
            _hitTouch = new Touch[TideConfig.MaxCount];
        }

        private int CheckWeapon(int weapon)
        {
            if (weapon < 0 || weapon >= _weaponLevel.Length) { throw new ArgumentOutOfRangeException(nameof(weapon), weapon, "未定義の武器です。"); }
            return weapon;
        }

        private void AdvanceTimers()
        {
            _invincible.Tick();
            for (var i = 0; i < _enemies.Count; i++)
            {
                if (_enemies.Flash[i] > 0) { _enemies.Flash[i]--; }
                if (_enemies.OrbitWait[i] > 0) { _enemies.OrbitWait[i]--; }
            }
        }

        private void MovePlayer(PlayerCommand command)
        {
            PlayerStart = Player;
            var speed = command.MoveX != 0 && command.MoveY != 0 ? TideConfig.DiagonalOf(_config.PlayerSpeed) : _config.PlayerSpeed;
            Player += new Vec2i(command.MoveX * speed, command.MoveY * speed);
            if (command.MoveX != 0 || command.MoveY != 0) { Facing = new Vec2i(command.MoveX, command.MoveY); }
            if (command.MoveX != 0) { FacingX = command.MoveX; }
        }

        /// <summary>
        /// 敵の移動。Chase とボスは、番号と経過 Tick で決まる位相の Tick にだけ自機への向きを決め直す
        /// （<see cref="TideConfig.SteerIntervalTicks"/> ごとにちょうど 1 回。毎 Tick の計算は全体の 1/間隔 に減る）。
        /// 移動は毎 Tick、直前に決めた向きで進む。
        /// </summary>
        private void MoveEnemies()
        {
            var interval = _config.SteerIntervalTicks;
            for (var i = 0; i < _enemies.Count; i++)
            {
                _enemies.StartX[i] = _enemies.X[i];
                _enemies.StartY[i] = _enemies.Y[i];
                var def = _config.Enemies[_enemies.DefOf[i]];
                if (def.Behavior != EnemyBehavior.Swarm && IsSteerPhase(_enemies.IdOf[i], Elapsed, interval))
                {
                    var velocity = IntMath.Scale(Player - Position(i), def.Speed);
                    _enemies.Vx[i] = velocity.X;
                    _enemies.Vy[i] = velocity.Y;
                    _enemies.SteerTick[i] = Elapsed;
                }

                _enemies.X[i] += _enemies.Vx[i];
                _enemies.Y[i] += _enemies.Vy[i];
            }
        }

        /// <summary>番号 <paramref name="id"/> の敵が Tick <paramref name="tick"/> に向きを決め直すか。</summary>
        public static bool IsSteerPhase(int id, int tick, int interval) => ((id + tick) % interval + interval) % interval == 0;

        /// <summary>
        /// 敵どうしの押し合い。全員の押す量を同じ位置（移動の後）から計算してからまとめて動かすので、処理の順番に依らない。
        /// 1 体が見る近傍は <see cref="TideConfig.SeparationNeighbors"/> 体まで（グリッドの順）、動かされる量は <see cref="TideConfig.SeparationPush"/> まで。
        /// ボスは押されない（他の敵は押す）。
        /// </summary>
        private void Separate()
        {
            if (_config.SeparationNeighbors == 0 || _config.SeparationPush == 0) { return; }
            var count = _enemies.Count;
            _grid.Build(Player, count, _enemies.X, _enemies.Y);
            for (var i = 0; i < count; i++)
            {
                _pushX[i] = 0;
                _pushY[i] = 0;
                if (_enemies.DefOf[i] == _bossDef) { continue; }
                var position = Position(i);
                var radius = _config.Enemies[_enemies.DefOf[i]].Radius;
                var reach = radius + _largestRadius;
                var found = _grid.Collect(position.X - reach, position.Y - reach, position.X + reach, position.Y + reach, _candidates);
                var seen = 0;
                long pushX = 0;
                long pushY = 0;
                for (var c = 0; c < found && seen < _config.SeparationNeighbors; c++)
                {
                    var j = _candidates[c];
                    if (j == i) { continue; }
                    var other = Position(j);
                    var sum = radius + _config.Enemies[_enemies.DefOf[j]].Radius;
                    var distanceSquared = Vec2i.DistanceSquared(position, other);
                    if (distanceSquared >= (long)sum * sum) { continue; }
                    seen++;
                    var overlap = sum - (int)IntMath.Sqrt(distanceSquared);
                    var away = position - other;
                    if (away.IsZero) { away = new Vec2i(_enemies.IdOf[i] < _enemies.IdOf[j] ? 1 : -1, 0); }
                    var push = IntMath.Scale(away, Math.Max(1, overlap / 2));
                    pushX += push.X;
                    pushY += push.Y;
                }

                var total = new Vec2i((int)Math.Max(int.MinValue / 2, Math.Min(int.MaxValue / 2, pushX)), (int)Math.Max(int.MinValue / 2, Math.Min(int.MaxValue / 2, pushY)));
                if (total.LengthSquared > (long)_config.SeparationPush * _config.SeparationPush) { total = IntMath.Scale(total, _config.SeparationPush); }
                _pushX[i] = total.X;
                _pushY[i] = total.Y;
            }

            for (var i = 0; i < count; i++)
            {
                _enemies.X[i] += _pushX[i];
                _enemies.Y[i] += _pushY[i];
            }
        }

        /// <summary>
        /// 宝石：この Tick の自機の軌跡が <see cref="TideConfig.PickupRadius"/> 未満に近づいた宝石は吸い寄せ（以後ずっと自機へ向かう）、
        /// 自機に触れた Tick に値を 1 回だけ足してその場で消す。
        /// </summary>
        private void CollectGems()
        {
            var touch = _config.PlayerRadius + _config.GemRadius;
            for (var i = _gems.Count - 1; i >= 0; i--)
            {
                var gem = new Vec2i(_gems.X[i], _gems.Y[i]);
                // 吸い寄せの始まりも自機の Tick 中の軌跡で判定する（速く動いて範囲を通り過ぎた宝石を取り逃さない。028 のレビュー）。
                if (!_gems.Attracted[i] && Sweep.Touches(PlayerStart, Player, gem, gem, _config.PickupRadius)) { _gems.Attracted[i] = true; }
                if (!_gems.Attracted[i]) { continue; }

                var toward = Player - gem;
                var step = (int)Math.Min(_config.GemSpeed, IntMath.Sqrt(toward.LengthSquared));
                var end = gem + IntMath.Scale(toward, step);
                _gems.X[i] = end.X;
                _gems.Y[i] = end.Y;
                if (!Sweep.Touches(PlayerStart, Player, gem, end, touch)) { continue; }

                Experience += _gems.Value[i];
                TotalExperience += _gems.Value[i];
                GemsCollected++;
                _events |= RunEvents.GemCollected;
                _gems.RemoveAt(i);
            }
        }

        /// <summary>倒した敵の位置に宝石を落とす。満杯なら、いちばん近い宝石へ値を足す（落ちている値の合計は保たれる）。</summary>
        private void DropGem(Vec2i position, int value)
        {
            if (value <= 0) { return; }
            ExperienceDropped += value;
            if (_gems.Count < _gems.Capacity)
            {
                _gems.Add(_nextId++, position, value);
                return;
            }

            var nearest = 0;
            var best = long.MaxValue;
            for (var i = 0; i < _gems.Count; i++)
            {
                var distance = Vec2i.DistanceSquared(position, new Vec2i(_gems.X[i], _gems.Y[i]));
                if (distance < best)
                {
                    best = distance;
                    nearest = i;
                }
            }

            // 値は long で持つので、満杯の間に何度足しても切り捨てない（落ちている値の合計は保たれる。028 P2-3）。
            _gems.Value[nearest] += value;
        }

        private void GainLevels()
        {
            while (Experience >= ExperienceToNext)
            {
                Experience -= ExperienceToNext;
                Level++;
                PendingLevelUps++;
                _events |= RunEvents.LevelUp;
            }

            if (PendingLevelUps > 0 && !IsChoosing) { MakeChoices(); }
        }

        /// <summary>
        /// 3 択を作る：持っていない武器の取得と、最大でない武器の強化から、重複なく 3 つを乱数で選ぶ（定義の順に並べ直す）。
        /// 3 つに満たなければ「体力回復」を 1 つ足す。
        /// </summary>
        private void MakeChoices()
        {
            var available = 0;
            for (var w = 0; w < _weaponLevel.Length; w++)
            {
                var level = _weaponLevel[w];
                if (level == 0) { _pool[available++] = new LevelChoice(ChoiceKind.Acquire, w, 1); }
                else if (level < _config.Weapons[w].MaxLevel) { _pool[available++] = new LevelChoice(ChoiceKind.Upgrade, w, level + 1); }
            }

            // 部分的なフィッシャー–イェーツ：先頭から選んだ候補を置いていく。
            var picked = Math.Min(ChoiceSlots, available);
            for (var k = 0; k < picked; k++)
            {
                var j = k + _choiceRandom.Next(available - k);
                (_pool[k], _pool[j]) = (_pool[j], _pool[k]);
            }

            Array.Sort(_pool, 0, picked, ChoiceOrder.Instance);
            for (var k = 0; k < picked; k++) { _choices[k] = _pool[k]; }
            _choiceCount = picked;
            if (_choiceCount < ChoiceSlots) { _choices[_choiceCount++] = new LevelChoice(ChoiceKind.Heal, -1, 0); }
        }

        /// <summary>
        /// 自機から <see cref="TideConfig.DespawnDistance"/> より離れた敵（ボス以外）を、自機の進む向きの側の出現の輪へ置き直す。
        /// 体力・番号は保つので、敵の数は増えない。Swarm は新しい位置から自機へ向け直す。
        /// </summary>
        private void Relocate()
        {
            var limit = (long)_config.DespawnDistance * _config.DespawnDistance;
            for (var i = 0; i < _enemies.Count; i++)
            {
                if (_enemies.DefOf[i] == _bossDef || Vec2i.DistanceSquared(Position(i), Player) <= limit) { continue; }
                var spread = Trig.FullTurn / 8;
                var angle = AheadAngle() + _placeRandom.Range(-spread, spread);
                var position = OnRing(angle, _placeRandom.Range(_config.SpawnInner, _config.SpawnOuter));
                _enemies.X[i] = _enemies.StartX[i] = position.X;
                _enemies.Y[i] = _enemies.StartY[i] = position.Y;
                var def = _config.Enemies[_enemies.DefOf[i]];
                if (def.Behavior == EnemyBehavior.Swarm)
                {
                    var velocity = IntMath.Scale(Player - position, def.Speed);
                    _enemies.Vx[i] = velocity.X;
                    _enemies.Vy[i] = velocity.Y;
                }
            }
        }

        /// <summary>
        /// 出現：区間の間隔で予算を貯め、間隔 1 回分で頭打ちにする（上限から空いた瞬間にまとめて湧かない）。
        /// ボスを除いた生存数が区間の目標と <see cref="TideConfig.MaxEnemies"/> の小さいほうに届いている間は出さない。1 Tick に出すのは最大 1 体。
        /// </summary>
        private void SpawnStep()
        {
            var stage = _config.StageAt(Elapsed - 1);
            _spawnProgress = Math.Min(_spawnProgress + 1, stage.IntervalTicks);
            if (_spawnProgress < stage.IntervalTicks || NormalCount >= Math.Min(stage.TargetAlive, _config.MaxEnemies)) { return; }

            var def = PickKind(stage);
            var position = OnRing(_placeRandom.Next(Trig.FullTurn), _placeRandom.Range(_config.SpawnInner, _config.SpawnOuter));
            var enemy = _config.Enemies[def];
            var velocity = enemy.Behavior == EnemyBehavior.Swarm ? IntMath.Scale(Player - position, enemy.Speed) : Vec2i.Zero;
            _enemies.Add(_nextId++, def, position, velocity, enemy.Health, Elapsed);
            _spawnProgress = 0;
            Spawned++;
        }

        private int PickKind(SpawnStage stage)
        {
            var total = 0;
            foreach (var weight in stage.Weights) { total += weight; }
            var roll = _kindRandom.Next(total);
            for (var k = 0; k < stage.Weights.Length; k++)
            {
                roll -= stage.Weights[k];
                if (roll < 0) { return k; }
            }

            throw new InvalidOperationException("重みの合計が合いません。");
        }

        /// <summary>ボスが出る Tick に自機の進む向きの輪の内側へ出し、猶予が尽きたら時間切れの負けにする。</summary>
        private void BossStep()
        {
            if (!BossSpawned && Elapsed >= _config.DurationTicks)
            {
                var boss = _config.Enemies[_bossDef];
                Place(_bossDef, OnRing(AheadAngle(), _config.SpawnInner + boss.Radius));
                _events |= RunEvents.BossSpawned;
            }

            if (Elapsed >= _config.MaxRunTicks) { Finish(RunStatus.Lost, RunEnd.TimeUp); }
        }

        /// <summary>自機の進む向きの角度（まだ動いていなければ右）。</summary>
        private int AheadAngle() => Trig.Atan2(Facing.Y, Facing.X, Trig.Right);

        /// <summary>自機から角度 <paramref name="angle"/>・距離 <paramref name="distance"/> の点。</summary>
        private Vec2i OnRing(int angle, int distance) => Player + new Vec2i(
            IntMath.RoundDiv((long)Trig.Cos(angle) * distance, Trig.One),
            IntMath.RoundDiv((long)Trig.Sin(angle) * distance, Trig.One));

        private Vec2i Position(int i) => new(_enemies.X[i], _enemies.Y[i]);

        private void Finish(RunStatus status, RunEnd end)
        {
            if (IsOver) { return; }
            Status = status;
            End = end;
            _choiceCount = 0;
            _events |= status == RunStatus.Won ? RunEvents.Won : RunEvents.Lost;
        }

        /// <summary>候補を武器の番号の順に並べる（3 択の表示を安定させる）。</summary>
        private sealed class ChoiceOrder : System.Collections.Generic.IComparer<LevelChoice>
        {
            public static readonly ChoiceOrder Instance = new();

            public int Compare(LevelChoice x, LevelChoice y) => x.Weapon.CompareTo(y.Weapon);
        }
    }
}
