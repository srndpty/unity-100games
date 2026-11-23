using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Numerics;
using TileMotion;

namespace Game.Core
{
    public enum RunStatus
    {
        Playing,

        /// <summary>2 体のボスの 6 弾幕をすべて終えた。</summary>
        Won,

        /// <summary>残機が 0 になった。</summary>
        Lost,
    }

    /// <summary>弾幕の間か、弾幕の最中か。</summary>
    public enum RunPhase
    {
        /// <summary>弾幕と弾幕の間（試合の初めを含む）。ボスは撃たず、ショットも効かない。</summary>
        Interval,

        Pattern,
    }

    /// <summary>1 Tick に起きた出来事（音・表示用）。</summary>
    [Flags]
    public enum RunEvents
    {
        None = 0,
        Shot = 1 << 0,
        BossHit = 1 << 1,
        Bomb = 1 << 2,
        Hurt = 1 << 3,
        PatternStarted = 1 << 4,

        /// <summary>弾幕の体力を削りきった。</summary>
        PatternDefeated = 1 << 5,

        /// <summary>弾幕が時間切れで終わった。</summary>
        PatternTimedOut = 1 << 6,

        /// <summary>被弾もボムもなく削りきり、ボーナスが入った。</summary>
        PatternBonus = 1 << 7,

        /// <summary>ボスの 3 つ目の弾幕が終わった。</summary>
        BossDefeated = 1 << 8,
        Won = 1 << 9,
        Lost = 1 << 10,
    }

    /// <summary>1 Tick の自機の操作。</summary>
    public readonly struct PlayerCommand : IEquatable<PlayerCommand>
    {
        public PlayerCommand(int horizontal, int vertical, bool slow, bool shoot, bool bomb)
        {
            if (horizontal < -1 || horizontal > 1) { throw new ArgumentOutOfRangeException(nameof(horizontal), horizontal, "-1〜1 にしてください。"); }
            if (vertical < -1 || vertical > 1) { throw new ArgumentOutOfRangeException(nameof(vertical), vertical, "-1〜1 にしてください。"); }
            Horizontal = horizontal;
            Vertical = vertical;
            Slow = slow;
            Shoot = shoot;
            Bomb = bomb;
        }

        public static PlayerCommand None => default;

        /// <summary>左 -1・なし 0・右 1。</summary>
        public int Horizontal { get; }

        /// <summary>下 -1・なし 0・上 1。</summary>
        public int Vertical { get; }

        public bool Slow { get; }

        public bool Shoot { get; }

        /// <summary>この Tick にボムを使う。</summary>
        public bool Bomb { get; }

        public bool Equals(PlayerCommand other) =>
            Horizontal == other.Horizontal && Vertical == other.Vertical && Slow == other.Slow && Shoot == other.Shoot && Bomb == other.Bomb;

        public override bool Equals(object? obj) => obj is PlayerCommand other && Equals(other);

        public override int GetHashCode() => (Horizontal + 1) | ((Vertical + 1) << 2) | (Slow ? 16 : 0) | (Shoot ? 32 : 0) | (Bomb ? 64 : 0);

        public static bool operator ==(PlayerCommand left, PlayerCommand right) => left.Equals(right);

        public static bool operator !=(PlayerCommand left, PlayerCommand right) => !left.Equals(right);
    }

    /// <summary>自機のショット 1 発。真上へ直進する。</summary>
    public readonly struct Shot
    {
        public Shot(Vec2i start, Vec2i position)
        {
            Start = start;
            Position = position;
        }

        /// <summary>この Tick の移動の始めの位置。</summary>
        public Vec2i Start { get; }

        public Vec2i Position { get; }
    }

    /// <summary>
    /// 試合 1 回。1 Tick の順序：
    /// 時間を進める（無敵・ボム・連射）→ 自機の移動 → ボム → ショットを撃つ → ショットの移動 →
    /// ボス（弾幕の間の待ち、または弾幕：ボスの移動と発射）→ 敵弾の移動 → ボム中は敵弾を消す →
    /// ショット×ボス・ボムのダメージ → 弾幕の終わりの判定（敵弾を消す）→ 敵弾×自機 → 勝敗。
    /// <para>
    /// 同じ Tick では、ボムを当たりより先に、弾幕の終わり（敵弾の全消去）を自機の被弾より先に解決する。
    /// 自機の被弾は、その Tick に触れた弾が何発でも 1 回だけ数える。
    /// </para>
    /// </summary>
    public sealed class VeilRun
    {
        /// <summary>掃引に渡す座標の各成分の上限。これ以下なら掃引の 2 次式（判別式など）が long に収まる。</summary>
        private const int SweepLimit = 1 << 14;

        private readonly VeilConfig _config;
        private readonly ReadOnlyCollection<BossScript> _bosses;
        private BulletPool _pool;
        private List<Shot> _shots;
        private PatternPlayer? _pattern;
        private Countdown _invincible;
        private Countdown _bomb;
        private Countdown _shotCooldown;
        private Countdown _interval;
        private RunEvents _events;

        /// <param name="config">設定（検証して複製する）。</param>
        /// <param name="bosses">ボス 2 体の台本。</param>
        /// <param name="seed">試合のシード。弾幕ごとにこのシードから系列を分ける。</param>
        /// <param name="startBoss">何体目のボスから始めるか（0 始まり。「このボスから再挑戦」に使う）。</param>
        /// <param name="startScore">始めの得点（再挑戦では、そのボスを始めたときの得点）。</param>
        public VeilRun(VeilConfig config, IReadOnlyList<BossScript> bosses, int seed, int startBoss = 0, long startScore = 0)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            config.Validate();
            VeilConfig.ValidateBosses(bosses);
            if (startBoss < 0 || startBoss >= VeilConfig.BossCount) { throw new ArgumentOutOfRangeException(nameof(startBoss), startBoss, "ボスの番号が範囲外です。"); }
            if (startScore < 0) { throw new ArgumentOutOfRangeException(nameof(startScore), startScore, "0 以上にしてください。"); }

            _config = config.Copy();
            var copy = new BossScript[bosses.Count];
            for (var i = 0; i < copy.Length; i++) { copy[i] = bosses[i]; }
            _bosses = Array.AsReadOnly(copy);
            Seed = seed;
            _pool = new BulletPool(_config.MaxBullets);
            // Tick の途中（画面の上へ出たショットを消す前）は 1 回分多くなりうるので、その分も先に取っておく（増やすときの割り当てをしない）。
            _shots = new List<Shot>((int)_config.ShotCapacity + VeilConfig.ShotsPerVolley);
            Shots = _shots.AsReadOnly();
            Player = _config.PlayerStart;
            PlayerStart = Player;
            Lives = _config.Lives;
            Bombs = _config.Bombs;
            BossIndex = startBoss;
            Boss = _config.BossHome;
            BossStart = Boss;
            Score = startScore;
            BossStartScore = startScore;
            EnterInterval();
        }

        public VeilConfig Config => _config.Copy();

        public ReadOnlyCollection<BossScript> Bosses => _bosses;

        public int Seed { get; }

        public RunStatus Status { get; private set; }

        public bool IsOver => Status != RunStatus.Playing;

        public RunPhase Phase { get; private set; }

        /// <summary>進めた Tick 数。</summary>
        public int Elapsed { get; private set; }

        public long Score { get; private set; }

        /// <summary>今のボスを始めたときの得点（「このボスから再挑戦」で戻す値）。</summary>
        public long BossStartScore { get; private set; }

        public int Lives { get; private set; }

        public int Bombs { get; private set; }

        /// <summary>自機の位置（<see cref="Field.Unit"/> 単位）。</summary>
        public Vec2i Player { get; private set; }

        /// <summary>この Tick の自機の移動の始めの位置。</summary>
        public Vec2i PlayerStart { get; private set; }

        /// <summary>直前の Tick に低速だったか（当たり判定の点の表示）。</summary>
        public bool Slow { get; private set; }

        public bool IsInvincible => _invincible.IsActive || _bomb.IsActive;

        /// <summary>被弾の無敵の残り Tick（この Tick を含む）。</summary>
        public int InvincibleRemaining => _invincible.Remaining;

        /// <summary>ボムの残り Tick（この Tick を含む）。</summary>
        public int BombRemaining => _bomb.Remaining;

        public int BossIndex { get; private set; }

        /// <summary>ボスの中の弾幕の番号（0 始まり）。弾幕の間は次に始まる弾幕の番号。</summary>
        public int PatternIndex { get; private set; }

        /// <summary>試合を通した弾幕の番号（0〜5）。</summary>
        public int StageIndex => (BossIndex * VeilConfig.PatternsPerBoss) + PatternIndex;

        public BossScript CurrentBoss => _bosses[BossIndex];

        public PatternSpec CurrentPattern => CurrentBoss.Patterns[PatternIndex];

        /// <summary>ボスの位置。</summary>
        public Vec2i Boss { get; private set; }

        /// <summary>この Tick のボスの移動の始めの位置。</summary>
        public Vec2i BossStart { get; private set; }

        /// <summary>今の弾幕の残りの体力（弾幕の間は次の弾幕の体力）。</summary>
        public int BossHp { get; private set; }

        /// <summary>今の弾幕の残り Tick（弾幕の間は次の弾幕の制限時間）。</summary>
        public int PatternTicksLeft { get; private set; }

        /// <summary>弾幕の間の残り Tick。</summary>
        public int IntervalRemaining => _interval.Remaining;

        /// <summary>今の弾幕で被弾またはボムを使ったか（ボーナスの条件）。</summary>
        public bool PatternSpoiled { get; private set; }

        /// <summary>被弾した回数（試合を通して）。</summary>
        public int Hits { get; private set; }

        /// <summary>弾幕ごとの同時に存在した敵弾の最大数（計測用）。</summary>
        public int PeakBullets { get; private set; }

        /// <summary>敵弾（読み取り専用で使う。状態を変えるのは試合だけ）。</summary>
        public BulletPool Bullets => _pool;

        public ReadOnlyCollection<Shot> Shots { get; private set; }

        /// <summary>1 Tick 進める。終わった試合では何もしない。</summary>
        public void Step(PlayerCommand command)
        {
            if (IsOver) { return; }
            Elapsed++;
            _invincible.Tick();
            _bomb.Tick();
            _shotCooldown.Tick();

            MovePlayer(command);
            if (command.Bomb) { UseBomb(); }
            if (command.Shoot) { Shoot(); }
            MoveShots();

            BossStart = Boss;
            var inPattern = Phase == RunPhase.Pattern;
            if (inPattern)
            {
                _pattern!.Step(Player, _pool);
                Boss = _pattern.Boss;
                PatternTicksLeft--;
            }
            else
            {
                WaitInterval();
            }

            _pool.Step();
            if (_bomb.IsActive) { _pool.Clear(); }
            if (_pool.Count > PeakBullets) { PeakBullets = _pool.Count; }

            if (inPattern)
            {
                HitBoss();
                EndPatternIfDone();
            }

            HitPlayer();
            RemoveOffscreenShots();
        }

        /// <summary>前回の呼び出し以降に起きた出来事を取り出す。</summary>
        public RunEvents TakeEvents()
        {
            var events = _events;
            _events = RunEvents.None;
            return events;
        }

        /// <summary>
        /// 自機を 1 Tick 動かした位置。斜めは各軸 181/256 倍（最低 1）。領域の端から <see cref="VeilConfig.PlayerMargin"/> の内側で止まる。
        /// 操縦役の先読みも同じ関数を使う。
        /// </summary>
        public static Vec2i Moved(Vec2i position, int horizontal, int vertical, bool slow, VeilConfig config)
        {
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var speed = slow ? config.PlayerSlowSpeed : config.PlayerSpeed;
            if (horizontal != 0 && vertical != 0) { speed = Math.Max(1, speed * 181 / 256); }
            var margin = config.PlayerMargin;
            return new Vec2i(
                IntMath.Clamp(position.X + (horizontal * speed), margin, Field.Width - margin),
                IntMath.Clamp(position.Y + (vertical * speed), margin, Field.Height - margin));
        }

        /// <summary>
        /// 2 つの動く円が、この Tick に触れたか（<see cref="Sweep.First"/>）。
        /// <para>
        /// ある軸の始めの隔たりが「両者の移動量と半径の和」より大きい組は、その軸で近づききれないので掃引しない（厳密な除外）。
        /// 残った組は、座標が小さければ（自機と弾のような近い組）long の <see cref="Sweep.First"/> で、大きければ（ボスが 1 Tick で遠くへ動くなど）
        /// 2 次式を <see cref="BigInteger"/> で厳密に判定する。どちらも「中心の距離が半径の和より小さくなったら触れた」で、ちょうど接するだけでは触れない。
        /// 大きな組は滅多に出ないので、BigInteger の割り当ては弾と自機の判定（毎 Tick 数千組）には起きない。
        /// </para>
        /// </summary>
        public static bool Touches(Vec2i aStart, Vec2i aEnd, Vec2i bStart, Vec2i bEnd, int radiusSum)
        {
            if (radiusSum <= 0) { throw new ArgumentOutOfRangeException(nameof(radiusSum), radiusSum, "半径の和は正にしてください。"); }
            long p0x = aStart.X - bStart.X;
            long p0y = aStart.Y - bStart.Y;
            long ax = aEnd.X - aStart.X;
            long ay = aEnd.Y - aStart.Y;
            long bx = bEnd.X - bStart.X;
            long by = bEnd.Y - bStart.Y;
            if (Math.Abs(p0x) > Math.Abs(ax) + Math.Abs(bx) + radiusSum || Math.Abs(p0y) > Math.Abs(ay) + Math.Abs(by) + radiusSum) { return false; }

            var dx = ax - bx;
            var dy = ay - by;
            var largest = Math.Max(Math.Max(Math.Abs(p0x), Math.Abs(p0y)), Math.Max(Math.Max(Math.Abs(dx), Math.Abs(dy)), radiusSum));
            if (largest <= SweepLimit)
            {
                var a0 = new Vec2i((int)p0x, (int)p0y);
                return Sweep.First(a0, a0 + new Vec2i((int)dx, (int)dy), Vec2i.Zero, Vec2i.Zero, radiusSum).Hit;
            }

            return ExactlyTouches(p0x, p0y, dx, dy, radiusSum);
        }

        /// <summary>
        /// 相対位置 p(t) = p0 + t d（0 ≤ t ≤ 1）が、半径 <paramref name="radius"/> の円の内側に入るか。|p(t)|^2 = a t^2 + b t + c（c = |p0|^2 - R^2）を
        /// 桁あふれしない整数（<see cref="BigInteger"/>）で判定する（<see cref="Sweep.First"/> と同じ式）。
        /// </summary>
        private static bool ExactlyTouches(long p0x, long p0y, long dx, long dy, long radius)
        {
            var c = (p0x * p0x) + (p0y * p0y) - (radius * radius);
            if (c < 0) { return true; }
            var a = (dx * dx) + (dy * dy);
            var b = 2 * ((p0x * dx) + (p0y * dy));
            if (a == 0 || b >= 0) { return false; }

            // 最も近づく時刻 t* = -b / 2a。t* が 1 以上なら終わりの位置で、そうでなければ t* での近さ（判別式が正か）で判定する。
            if (-b >= 2 * a) { return a + b + c < 0; }
            return (new BigInteger(b) * b) - (new BigInteger(4) * a * c) > 0;
        }

        /// <summary>中身を写した別の試合（操縦役の先読み用。共有しない）。</summary>
        public VeilRun Clone()
        {
            var copy = (VeilRun)MemberwiseClone();
            copy._pool = _pool.Clone();
            copy._shots = new List<Shot>(_shots);
            copy.Shots = copy._shots.AsReadOnly();
            copy._pattern = _pattern?.Clone();
            return copy;
        }

        /// <summary>以後の動きを決める状態をすべて混ぜたハッシュ（022 の知見）。</summary>
        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix(Seed);
            hash.Mix((int)Status);
            hash.Mix((int)Phase);
            hash.Mix(Elapsed);
            hash.Mix(Score);
            hash.Mix(BossStartScore);
            hash.Mix(Lives);
            hash.Mix(Bombs);
            hash.Mix(Player.X);
            hash.Mix(Player.Y);
            hash.Mix(PlayerStart.X);
            hash.Mix(PlayerStart.Y);
            hash.Mix(Slow);
            hash.Mix(_invincible.Remaining);
            hash.Mix(_bomb.Remaining);
            hash.Mix(_shotCooldown.Remaining);
            hash.Mix(_interval.Remaining);
            hash.Mix(BossIndex);
            hash.Mix(PatternIndex);
            hash.Mix(Boss.X);
            hash.Mix(Boss.Y);
            hash.Mix(BossHp);
            hash.Mix(PatternTicksLeft);
            hash.Mix(PatternSpoiled);
            hash.Mix(Hits);
            hash.Mix(_shots.Count);
            foreach (var shot in _shots)
            {
                hash.Mix(shot.Position.X);
                hash.Mix(shot.Position.Y);
            }

            hash.Mix(_pattern != null);
            _pattern?.MixInto(ref hash);
            _pool.MixInto(ref hash);
            return hash.Value;
        }

        private void MovePlayer(PlayerCommand command)
        {
            PlayerStart = Player;
            Slow = command.Slow;
            Player = Moved(Player, command.Horizontal, command.Vertical, command.Slow, _config);
        }

        /// <summary>ボム：残りがあり、ボム中でなければ使う。敵弾を消し、ボムの間は無敵。</summary>
        private void UseBomb()
        {
            if (Bombs == 0 || _bomb.IsActive) { return; }
            Bombs--;
            _bomb.Arm(_config.BombTicks);
            _pool.Clear();
            PatternSpoiled = true;
            _events |= RunEvents.Bomb;
        }

        private void Shoot()
        {
            if (_shotCooldown.IsActive) { return; }
            _shotCooldown.Arm(_config.ShotIntervalTicks);
            var spread = Slow ? _config.ShotSlowSpread : _config.ShotSpread;
            for (var i = -1; i <= 1; i++)
            {
                var origin = Player + new Vec2i(i * spread, 0);
                _shots.Add(new Shot(origin, origin));
            }

            _events |= RunEvents.Shot;
        }

        private void MoveShots()
        {
            for (var i = 0; i < _shots.Count; i++)
            {
                var from = _shots[i].Position;
                _shots[i] = new Shot(from, from + new Vec2i(0, _config.ShotSpeed));
            }
        }

        private void RemoveOffscreenShots()
        {
            var write = 0;
            for (var read = 0; read < _shots.Count; read++)
            {
                if (_shots[read].Position.Y - _config.ShotRadius > Field.Height) { continue; }
                _shots[write++] = _shots[read];
            }

            _shots.RemoveRange(write, _shots.Count - write);
        }

        /// <summary>弾幕の間を 1 Tick 進める。間の最後の Tick に次の弾幕を始め、次の Tick から撃つ。</summary>
        private void WaitInterval()
        {
            _interval.Tick();
            if (!_interval.IsActive) { StartPattern(); }
        }

        private void StartPattern()
        {
            var pattern = CurrentPattern;
            Phase = RunPhase.Pattern;
            BossHp = pattern.Hp;
            PatternTicksLeft = pattern.TimeTicks;

            // 弾幕の間に使ったボムがまだ続いていれば、この弾幕もボムを使ったことにする。
            PatternSpoiled = _bomb.IsActive;
            PeakBullets = 0;
            var patternSeed = unchecked((Seed * 31) + (BossIndex * VeilConfig.PatternsPerBoss) + PatternIndex);
            _pattern = new PatternPlayer(pattern, Boss, patternSeed);
            _events |= RunEvents.PatternStarted;
        }

        /// <summary>ショットとボスを掃引する。当たったショットは消える。ボム中は毎 Tick ダメージ。</summary>
        private void HitBoss()
        {
            var radius = CurrentBoss.Radius + _config.ShotRadius;
            var damage = 0;
            for (var i = _shots.Count - 1; i >= 0; i--)
            {
                if (!Touches(_shots[i].Start, _shots[i].Position, BossStart, Boss, radius)) { continue; }
                _shots.RemoveAt(i);
                damage += _config.ShotDamage;
                Score += _config.ShotHitScore;
                _events |= RunEvents.BossHit;
            }

            if (_bomb.IsActive) { damage += _config.BombDamagePerTick; }
            BossHp = Math.Max(0, BossHp - damage);
        }

        private void EndPatternIfDone()
        {
            var defeated = BossHp == 0;
            if (!defeated && PatternTicksLeft > 0) { return; }

            _pool.Clear();
            _pattern = null;
            if (defeated)
            {
                _events |= RunEvents.PatternDefeated;
                if (!PatternSpoiled)
                {
                    Score += (long)_config.PatternBonus * (StageIndex + 1);
                    _events |= RunEvents.PatternBonus;
                }
            }
            else
            {
                _events |= RunEvents.PatternTimedOut;
            }

            PatternIndex++;
            if (PatternIndex == VeilConfig.PatternsPerBoss)
            {
                _events |= RunEvents.BossDefeated;
                if (BossIndex == VeilConfig.BossCount - 1)
                {
                    PatternIndex--;
                    Win();
                    return;
                }

                BossIndex++;
                PatternIndex = 0;
                Boss = _config.BossHome;
                BossStartScore = Score;
            }

            EnterInterval();
        }

        /// <summary>弾幕の間に入る。体力と残り時間は次の弾幕の値を見せる。</summary>
        private void EnterInterval()
        {
            Phase = RunPhase.Interval;
            BossHp = CurrentPattern.Hp;
            PatternTicksLeft = CurrentPattern.TimeTicks;
            _interval.Arm(_config.IntervalTicks);
        }

        /// <summary>敵弾と自機を掃引する。無敵でなければ、触れた弾の数によらず 1 回だけ被弾する。</summary>
        private void HitPlayer()
        {
            if (IsOver || IsInvincible) { return; }
            for (var i = 0; i < _pool.Count; i++)
            {
                if (!Touches(_pool.StartOf(i), _pool.PositionOf(i), PlayerStart, Player, _pool.RadiusOf(i) + _config.PlayerRadius)) { continue; }
                Hurt();
                return;
            }
        }

        private void Hurt()
        {
            Lives--;
            Hits++;
            PatternSpoiled = true;
            _pool.Clear();
            _events |= RunEvents.Hurt;
            if (Lives == 0)
            {
                Status = RunStatus.Lost;
                _events |= RunEvents.Lost;
                return;
            }

            Bombs = _config.Bombs;
            _invincible.Arm(_config.InvincibleTicks);
        }

        private void Win()
        {
            Score += ((long)Lives * _config.LifeBonus) + ((long)Bombs * _config.BombBonus);
            Status = RunStatus.Won;
            _events |= RunEvents.Won;
        }
    }
}
