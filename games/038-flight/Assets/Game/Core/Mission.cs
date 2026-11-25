using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>ミッションの状態。</summary>
    public enum Outcome
    {
        Running,
        Won,
        ShotDown,
        Crashed,
        OutOfArea,
        TimeUp,
    }

    /// <summary>発射の要求の結果。</summary>
    public enum FireResult
    {
        /// <summary>要求なし。</summary>
        None,
        Launched,
        NotLocked,
        NoAmmo,
        Reloading,
        Grounded,
        Down,
        Finished,
    }

    /// <summary>プレイヤーの 1 Tick の命令。</summary>
    public readonly struct PlayerCommand : IEquatable<PlayerCommand>
    {
        public PlayerCommand(Controls controls, bool fire, bool cycleTarget)
        {
            Controls = controls;
            Fire = fire;
            CycleTarget = cycleTarget;
        }

        public Controls Controls { get; }

        public bool Fire { get; }

        public bool CycleTarget { get; }

        public static bool operator ==(PlayerCommand left, PlayerCommand right) => left.Equals(right);

        public static bool operator !=(PlayerCommand left, PlayerCommand right) => !left.Equals(right);

        public bool Equals(PlayerCommand other) => Controls == other.Controls && Fire == other.Fire && CycleTarget == other.CycleTarget;

        public override bool Equals(object? obj) => obj is PlayerCommand other && Equals(other);

        public override int GetHashCode() => (Controls.GetHashCode() * 397) ^ (Fire ? 2 : 0) ^ (CycleTarget ? 1 : 0);
    }

    /// <summary>1 Tick で起きたこと（音と HUD のため）。</summary>
    [Flags]
    public enum MissionMoment
    {
        None = 0,
        TookOff = 1,
        PlayerFired = 1 << 1,
        EnemyFired = 1 << 2,
        EnemyDown = 1 << 3,
        PlayerHit = 1 << 4,
        Locked = 1 << 5,
        Finished = 1 << 6,
        MissileMissed = 1 << 7,
    }

    /// <summary>
    /// 1 回のミッション。1 Tick の順序：
    /// 目標の切り替え → 発射（前の Tick の終わりの LOCK で判定）→ 全機の飛行 → ミサイルの誘導・移動・命中（発射の順）→ ロックオンの更新 → 勝敗。
    /// 乱数は敵の配置と操縦のぶれで別の系列にする。
    /// </summary>
    public sealed class Mission
    {
        private readonly FlightConfig _flight;
        private readonly MissionConfig _config;
        private Fighter[] _fighters;
        private List<Missile> _missiles;
        private Vec3l[] _starts;
        private SeededRandom _pilotRandom;
        private int _lastAttacker;
        private ReadOnlyCollection<Fighter> _fighterView;
        private ReadOnlyCollection<Missile> _missileView;

        public Mission(FlightConfig flight, MissionConfig config, int seed)
        {
            if (flight == null) { throw new ArgumentNullException(nameof(flight)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            _flight = flight.Validate();
            _config = config.Validate(_flight);
            Seed = seed;
            Runway = _config.Runway;
            Guidance = new ProNav(_config.NavGain);

            var layout = new SeededRandom(seed);
            _pilotRandom = new SeededRandom(unchecked(seed + 0x5BD1E995));
            _fighters = new Fighter[MissionConfig.EnemyCount + 1];
            _fighters[0] = new Fighter(0, Aircraft.OnRunway(_flight, Runway), _config.PlayerHealth, _config.Missiles);
            for (var i = 1; i <= MissionConfig.EnemyCount; i++)
            {
                var jitter = _config.EnemyJitter;
                var position = new Vec3l(
                    MissionConfig.Meters(((i - 2) * _config.EnemySpacing) + layout.Range(-jitter, jitter)),
                    MissionConfig.Meters(_config.EnemyAltitude + layout.Range(-jitter, jitter)),
                    MissionConfig.Meters(_config.EnemyCenterZ + layout.Range(-jitter, jitter)));
                var heading = layout.Range(0, Units.FullTurn - 1);
                var plane = Aircraft.InFlight(_flight, position, heading, _config.EnemySpeed, 600);
                _fighters[i] = new Fighter(i, plane, 1, _config.EnemyMissiles) { PatrolCenter = position };
            }

            _missiles = new List<Missile>(_config.MissileCapacity);
            _starts = new Vec3l[_fighters.Length];
            _fighterView = Array.AsReadOnly(_fighters);
            _missileView = _missiles.AsReadOnly();
        }

        /// <summary>検証済みの飛行の設定の複製（書き換えてもミッションは変わらない）。</summary>
        public FlightConfig Flight => _flight.Copy();

        /// <summary>検証済みのミッションの設定の複製。</summary>
        public MissionConfig Config => _config.Copy();

        public int Seed { get; }

        public Runway Runway { get; }

        public IGuidance Guidance { get; }

        /// <summary>進めた Tick 数。</summary>
        public int Tick { get; private set; }

        public Outcome Outcome { get; private set; }

        public bool IsOver => Outcome != Outcome.Running;

        /// <summary>終わった Tick（続いていれば -1）。</summary>
        public int FinishedTick { get; private set; } = -1;

        /// <summary>攻撃役の敵の番号（いなければ -1）。ロックと発射をするのは攻撃役だけ。</summary>
        public int Attacker { get; internal set; } = -1;

        /// <summary>区域の外にいる Tick 数（中に戻ると 0）。</summary>
        public int OutOfArea { get; private set; }

        public IReadOnlyList<Fighter> Fighters => _fighterView;

        public Fighter Player => _fighters[0];

        /// <summary>発射した全てのミサイル（消えたものも <see cref="Missile.Active"/> が false で残る）。</summary>
        public IReadOnlyList<Missile> Missiles => _missileView;

        public int EnemiesLeft
        {
            get
            {
                var count = 0;
                for (var i = 1; i < _fighters.Length; i++) { if (!_fighters[i].Down) { count++; } }
                return count;
            }
        }

        /// <summary>プレイヤーが LOCK しているか。</summary>
        public bool PlayerLocked => IsLocked(_fighters[0]);

        /// <summary>1 Tick 進める。終わった後は状態を変えない。</summary>
        public (FireResult Fire, MissionMoment Moments) Step(PlayerCommand command)
        {
            command.Controls.Validate();
            if (IsOver) { return (command.Fire ? FireResult.Finished : FireResult.None, MissionMoment.None); }

            Tick++;
            var moments = MissionMoment.None;
            var player = _fighters[0];

            if (command.CycleTarget) { CycleTarget(player); }
            var fire = command.Fire ? TryFire(player) : FireResult.None;
            if (fire == FireResult.Launched) { moments |= MissionMoment.PlayerFired; }
            if (EnemyPilots && Attacker > 0)
            {
                var attacker = _fighters[Attacker];
                if (IsLocked(attacker) && TryFire(attacker) == FireResult.Launched)
                {
                    moments |= MissionMoment.EnemyFired;
                    StartBreak(attacker);
                }
            }

            if (EnemyPilots) { UpdateRoles(); }

            if (Tick % _config.EnemyJitterTicks == 1) { RedrawJitter(); }
            moments |= Fly(command.Controls);
            moments |= MoveMissiles();
            var wasLocked = IsLocked(player);
            for (var i = 0; i < _fighters.Length; i++) { UpdateLock(_fighters[i]); }
            if (!wasLocked && IsLocked(player)) { moments |= MissionMoment.Locked; }

            Judge();
            if (IsOver)
            {
                FinishedTick = Tick;
                moments |= MissionMoment.Finished;
            }

            return (fire, moments);
        }

        /// <summary>LOCK しているか（目標があり、続けた Tick が必要な数に達した）。</summary>
        public bool IsLocked(Fighter fighter)
        {
            if (fighter == null) { throw new ArgumentNullException(nameof(fighter)); }
            return fighter.LockTarget >= 0 && fighter.LockTicks >= LockTicksFor(fighter);
        }

        /// <summary>LOCK に要る Tick 数。</summary>
        public int LockTicksFor(Fighter fighter)
        {
            if (fighter == null) { throw new ArgumentNullException(nameof(fighter)); }
            return fighter.IsPlayer ? _config.LockTicks : _config.EnemyLockTicks;
        }

        /// <summary>目標が射手の円錐と距離の中にあれば、機首とのなす角の cos（Q30）。なければ null。</summary>
        public long? ConeScore(Fighter shooter, Fighter target)
        {
            if (shooter == null) { throw new ArgumentNullException(nameof(shooter)); }
            if (target == null) { throw new ArgumentNullException(nameof(target)); }
            if (target.Down || target.Index == shooter.Index) { return null; }
            var d = target.Plane.Position - shooter.Plane.Position;
            var dm = new Vec3l(d.X / Units.PerMillimeter, d.Y / Units.PerMillimeter, d.Z / Units.PerMillimeter);
            var distance = dm.Length;
            var range = shooter.IsPlayer ? _config.LockRange : _config.EnemyLockRange;
            if (distance > range * Units.PerMillimeter) { return null; }
            if (distance == 0) { return Fixed.One; }
            var cos = Vec3l.Dot(shooter.Plane.Attitude.Forward, dm) / distance;
            var cone = shooter.IsPlayer ? _config.LockCone : _config.EnemyLockCone;
            return cos >= Fixed.Cos(cone) ? cos : null;
        }

        public Mission Clone()
        {
            var copy = (Mission)MemberwiseClone();
            var fighters = new Fighter[_fighters.Length];
            for (var i = 0; i < fighters.Length; i++) { fighters[i] = _fighters[i].Clone(); }
            var missiles = new List<Missile>(_missiles.Capacity);
            foreach (var missile in _missiles) { missiles.Add(missile.Clone()); }
            copy._fighters = fighters;
            copy._missiles = missiles;
            copy._starts = new Vec3l[fighters.Length];
            copy._fighterView = Array.AsReadOnly(fighters);
            copy._missileView = missiles.AsReadOnly();
            copy._pilotRandom = _pilotRandom.Clone();
            return copy;
        }

        /// <summary>以後の動きを決める状態を全て混ぜたハッシュ。</summary>
        public uint Hash()
        {
            var hash = StateHash.Start();
            hash.Mix(Tick);
            hash.Mix((int)Outcome);
            hash.Mix(FinishedTick);
            hash.Mix(OutOfArea);
            hash.Mix(Attacker);
            hash.Mix(_pilotRandom.State);
            hash.Mix(_lastAttacker);
            foreach (var fighter in _fighters) { fighter.Mix(ref hash); }
            hash.Mix(_missiles.Count);
            foreach (var missile in _missiles) { missile.Mix(ref hash); }
            return hash.Value;
        }

        // ---- テスト用の配置 ----

        /// <summary>機体の飛行状態を置き換える（テストで場面を作る）。</summary>
        internal void Place(int index, Aircraft plane)
        {
            var old = _fighters[index];
            var fighter = new Fighter(index, plane, old.Health, old.Ammo)
            {
                PatrolCenter = plane.Position,
                Engaged = old.Engaged,
            };
            _fighters[index] = fighter;
        }

        internal Fighter FighterAt(int index) => _fighters[index];

        /// <summary>false にすると敵は操縦も発射もしない（テストで目標を直進させる）。</summary>
        internal bool EnemyPilots { get; set; } = true;

        /// <summary>ミサイルを直接置く（テストで同じ Tick の命中を作る）。</summary>
        internal Missile Launch(int owner, int target, Vec3l position, Vec3l velocity)
        {
            var missile = new Missile(_missiles.Count, owner, target, position, velocity, _config.MissileTurn, _config.MissileSpeed);
            _missiles.Add(missile);
            return missile;
        }

        // ---- 1 Tick の各段 ----

        private FireResult TryFire(Fighter shooter)
        {
            if (shooter.Down) { return FireResult.Down; }
            if (shooter.Plane.Grounded) { return FireResult.Grounded; }
            if (shooter.Ammo <= 0) { return FireResult.NoAmmo; }
            if (shooter.Reload > 0) { return FireResult.Reloading; }
            if (!IsLocked(shooter)) { return FireResult.NotLocked; }

            var plane = shooter.Plane;
            var velocity = plane.Velocity + Vec3l.ScaleUnit(plane.Attitude.Forward, _config.LaunchBoost);
            var turn = shooter.IsPlayer ? _config.MissileTurn : _config.EnemyMissileTurn;
            var speed = shooter.IsPlayer ? _config.MissileSpeed : _config.EnemyMissileSpeed;
            _missiles.Add(new Missile(_missiles.Count, shooter.Index, shooter.LockTarget, plane.Position, velocity, turn, speed));
            shooter.Ammo--;
            shooter.Reload = shooter.IsPlayer ? _config.Reload : _config.EnemyReload;
            return FireResult.Launched;
        }

        private void CycleTarget(Fighter player)
        {
            if (player.Down) { return; }
            var current = player.LockTarget;
            for (var step = 1; step <= MissionConfig.EnemyCount; step++)
            {
                var index = ((Math.Max(current, 0) + step - 1) % MissionConfig.EnemyCount) + 1;
                if (index == current) { continue; }
                if (ConeScore(player, _fighters[index]) == null) { continue; }
                player.LockTarget = index;
                player.LockTicks = 0;
                return;
            }
        }

        private void RedrawJitter()
        {
            var jitter = _config.EnemyAimJitter;
            for (var i = 1; i < _fighters.Length; i++)
            {
                _fighters[i].AimJitter = new Vec3l(
                    MissionConfig.Meters(_pilotRandom.Range(-jitter, jitter)),
                    MissionConfig.Meters(_pilotRandom.Range(-jitter, jitter)),
                    MissionConfig.Meters(_pilotRandom.Range(-jitter, jitter)));
            }
        }

        private MissionMoment Fly(Controls playerControls)
        {
            var moments = MissionMoment.None;
            var player = _fighters[0];

            // 敵の操縦は全機が動く前の状態から決める。
            var controls = new Controls[_fighters.Length];
            controls[0] = playerControls;
            for (var i = 1; i < _fighters.Length; i++)
            {
                if (!_fighters[i].Down && EnemyPilots) { controls[i] = EnemyControls(_fighters[i], player); }
            }

            for (var i = 0; i < _fighters.Length; i++)
            {
                var fighter = _fighters[i];
                _starts[i] = fighter.Plane.Position;
                if (fighter.Down) { continue; }
                if (fighter.Reload > 0) { fighter.Reload--; }
                var result = FlightModel.Step(_flight, fighter.Plane, controls[i], Runway);
                if (result == FlightEvent.TookOff && fighter.IsPlayer) { moments |= MissionMoment.TookOff; }
                if (result == FlightEvent.Crashed)
                {
                    fighter.Down = true;
                    fighter.DownTick = Tick;
                    if (!fighter.IsPlayer) { moments |= MissionMoment.EnemyDown; }
                }
            }

            return moments;
        }

        private Controls EnemyControls(Fighter enemy, Fighter player)
        {
            var view = enemy.View;
            var playerPlane = player.Plane;
            if (!enemy.Engaged && !player.Down && !playerPlane.Grounded)
            {
                var gap = (playerPlane.Position - enemy.Plane.Position).Length;
                if (gap < MissionConfig.Meters(_config.EngageRange)) { enemy.Engaged = true; }
            }

            Vec3l point;
            var recovery = Steering.Recovery(view, MissionConfig.Meters(_config.EnemyFloor));
            var threat = Threat(enemy);
            if (recovery.HasValue)
            {
                point = recovery.Value;
            }
            else if (threat.HasValue)
            {
                point = Steering.Evade(view, threat.Value);
            }
            else if (enemy.BreakTicks > 0)
            {
                // 離脱：今の進む向きへ水平にまっすぐ離れる。
                var v = enemy.Plane.Velocity;
                point = enemy.Plane.Position + new Vec3l(v.X * 600, 0, v.Z * 600);
            }
            else if (enemy.Index == Attacker && !player.Down)
            {
                point = playerPlane.Position + Vec3l.MulDiv(playerPlane.Velocity, _config.EnemyLeadTicks, 1) + enemy.AimJitter;
            }
            else if (enemy.Engaged && !player.Down)
            {
                // 攻撃役でない交戦中の敵は、プレイヤーのまわりを回って待つ。
                var center = new Vec3l(playerPlane.Position.X, Math.Max(playerPlane.Position.Y, MissionConfig.Meters(_config.EnemyFloor * 2)), playerPlane.Position.Z);
                point = OrbitPoint(enemy, center, MissionConfig.Meters(_config.EnemyWaitRadius));
            }
            else
            {
                point = OrbitPoint(enemy, enemy.PatrolCenter, MissionConfig.Meters(_config.PatrolRadius));
            }

            var controls = Steering.Toward(_flight, view, point, _config.EnemyCruiseSpeed);
            var agility = _config.EnemyAgility;
            return new Controls(controls.Pitch * agility / 100, controls.Roll * agility / 100, controls.Yaw * agility / 100, controls.Throttle);
        }

        /// <summary>
        /// 攻撃役を決める。離脱の残りを減らし、攻撃役が倒れた・離脱した・いなければ、前の攻撃役の次の番号から順に、
        /// 交戦中で離脱していない敵を選ぶ。攻撃役は追い続けた Tick を数え、上限で離脱する。
        /// </summary>
        private void UpdateRoles()
        {
            for (var i = 1; i < _fighters.Length; i++)
            {
                var enemy = _fighters[i];
                if (enemy.BreakTicks > 0)
                {
                    enemy.BreakTicks--;
                    if (enemy.BreakTicks == 0) { enemy.PursuitTicks = 0; }
                }
            }

            if (Attacker > 0 && (_fighters[Attacker].Down || _fighters[Attacker].BreakTicks > 0)) { Attacker = -1; }
            if (Attacker < 0 && !_fighters[0].Down)
            {
                for (var step = 1; step <= MissionConfig.EnemyCount; step++)
                {
                    var index = ((Math.Max(_lastAttacker, 0) + step - 1) % MissionConfig.EnemyCount) + 1;
                    var enemy = _fighters[index];
                    if (enemy.Down || !enemy.Engaged || enemy.BreakTicks > 0) { continue; }
                    Attacker = index;
                    _lastAttacker = index;
                    break;
                }
            }

            if (Attacker < 0) { return; }
            var attacker = _fighters[Attacker];
            attacker.PursuitTicks++;
            if (attacker.PursuitTicks >= _config.EnemyPursuitTicks) { StartBreak(attacker); }
        }

        private void StartBreak(Fighter enemy)
        {
            enemy.BreakTicks = _config.EnemyBreakTicks;
            enemy.PursuitTicks = 0;
            enemy.LockTicks = 0;
            enemy.LockTarget = -1;
            if (enemy.BreakTicks == 0) { return; }
            if (Attacker == enemy.Index) { Attacker = -1; }
        }

        /// <summary>敵を追うプレイヤーのミサイルのうち、避け始める距離より近い最も近いものの位置。</summary>
        private Vec3l? Threat(Fighter enemy)
        {
            Vec3l? closest = null;
            var closestDistance = MissionConfig.Meters(_config.EnemyEvadeRange);
            foreach (var missile in _missiles)
            {
                if (!missile.Active || !missile.Tracking || missile.Target != enemy.Index) { continue; }
                var distance = (missile.Position - enemy.Plane.Position).Length;
                if (distance < closestDistance)
                {
                    closest = missile.Position;
                    closestDistance = distance;
                }
            }

            return closest;
        }

        /// <summary>中心のまわりの半径 <paramref name="radius"/> の円の上で、今の位置から 30° 先の点（右回り）。</summary>
        private static Vec3l OrbitPoint(Fighter enemy, Vec3l center, long radius)
        {
            var offset = enemy.Plane.Position - center;
            var angle = Trig.Atan2(offset.X, offset.Z, 0) + Units.Degrees(30);
            return new Vec3l(center.X + Fixed.Round(Fixed.Sin(angle) * radius), center.Y, center.Z + Fixed.Round(Fixed.Cos(angle) * radius));
        }

        private MissionMoment MoveMissiles()
        {
            var moments = MissionMoment.None;
            foreach (var missile in _missiles)
            {
                if (!missile.Active) { continue; }
                var target = _fighters[missile.Target];
                if (target.Down) { missile.Tracking = false; }

                var start = missile.Position;
                var command = missile.Tracking
                    ? Guidance.Command(missile.Position, missile.Velocity, target.Plane.Position, target.Plane.Velocity)
                    : Vec3l.Zero;
                missile.Velocity = MissileBody.Step(missile.Velocity, command, missile.MaxTurn, _config.MissileAccel, missile.MaxSpeed);
                missile.Position = start + missile.Velocity;
                missile.Age++;

                if (missile.Tracking && Proximity.Within(start, missile.Position, _starts[target.Index], target.Plane.Position, _config.FuseRadius))
                {
                    missile.Active = false;
                    missile.End = MissileEnd.Hit;
                    moments |= Damage(target);
                    continue;
                }

                if (missile.Position.Y <= 0)
                {
                    missile.Active = false;
                    missile.End = MissileEnd.Ground;
                    moments |= MissionMoment.MissileMissed;
                }
                else if (missile.Age >= _config.MissileLife)
                {
                    missile.Active = false;
                    missile.End = MissileEnd.Expired;
                    moments |= MissionMoment.MissileMissed;
                }
            }

            return moments;
        }

        private MissionMoment Damage(Fighter target)
        {
            target.Health--;
            target.HitTick = Tick;
            if (target.Health > 0) { return target.IsPlayer ? MissionMoment.PlayerHit : MissionMoment.None; }

            target.Down = true;
            target.DownTick = Tick;
            target.ShotDown = true;

            // 撃墜した目標を追っていたミサイルは、同じ Tick の後の順番のものも含めて追尾をやめる。
            foreach (var missile in _missiles)
            {
                if (missile.Active && missile.Target == target.Index) { missile.Tracking = false; }
            }

            return target.IsPlayer ? MissionMoment.PlayerHit : MissionMoment.EnemyDown;
        }

        private void UpdateLock(Fighter fighter)
        {
            if (fighter.Down || fighter.Plane.Grounded || fighter.Plane.Air.Stalled)
            {
                fighter.LockTicks = 0;
                if (fighter.Down) { fighter.LockTarget = -1; }
                return;
            }

            if (!fighter.IsPlayer)
            {
                var player = _fighters[0];
                var inCone = fighter.Index == Attacker && fighter.BreakTicks == 0 && ConeScore(fighter, player) != null;
                fighter.LockTarget = inCone ? 0 : -1;
                fighter.LockTicks = inCone ? Math.Min(fighter.LockTicks + 1, _config.EnemyLockTicks) : 0;
                return;
            }

            // 今の目標が円錐から外れたら、円錐の中で最も機首に近い敵へ移り、数え直す。
            var current = fighter.LockTarget;
            if (current < 0 || ConeScore(fighter, _fighters[current]) == null)
            {
                var best = -1;
                long bestScore = long.MinValue;
                for (var i = 1; i < _fighters.Length; i++)
                {
                    var score = ConeScore(fighter, _fighters[i]);
                    if (score.HasValue && score.Value > bestScore)
                    {
                        best = i;
                        bestScore = score.Value;
                    }
                }

                fighter.LockTarget = best;
                fighter.LockTicks = 0;
            }

            if (fighter.LockTarget >= 0) { fighter.LockTicks = Math.Min(fighter.LockTicks + 1, _config.LockTicks); }
        }

        private void Judge()
        {
            var player = _fighters[0];
            if (player.Down)
            {
                Outcome = player.ShotDown ? Outcome.ShotDown : Outcome.Crashed;
                return;
            }

            if (EnemiesLeft == 0)
            {
                Outcome = Outcome.Won;
                return;
            }

            var p = player.Plane.Position;
            var half = MissionConfig.Meters(_config.ArenaHalfWidth);
            var inside = Math.Abs(p.X) <= half && Math.Abs(p.Z) <= half && p.Y <= MissionConfig.Meters(_config.Ceiling);
            OutOfArea = inside ? 0 : OutOfArea + 1;
            if (OutOfArea >= _config.OutOfAreaTicks)
            {
                Outcome = Outcome.OutOfArea;
                return;
            }

            if (Tick >= _config.MissionTicks) { Outcome = Outcome.TimeUp; }
        }
    }
}
