using System;
using IntGeometry;

namespace Game.Core
{
    /// <summary>直近の Pulse・Lash・Arc の発動（描画用。以後の進行には使わないので状態のハッシュに含めない）。</summary>
    public sealed class WeaponEffects
    {
        private Rect[] _lash = new Rect[2];
        private Vec2i[] _arc = new Vec2i[TideConfig.MaxCount + 1];

        /// <summary>最後に Pulse が発動した Tick（まだなら -1）。</summary>
        public int PulseTick { get; internal set; } = -1;

        public Vec2i PulseCenter { get; internal set; }

        public int PulseRange { get; internal set; }

        public int LashTick { get; internal set; } = -1;

        public int LashCount { get; internal set; }

        public int ArcTick { get; internal set; } = -1;

        /// <summary>連鎖の点の数（自機を含む）。</summary>
        public int ArcCount { get; internal set; }

        public Rect LashRect(int i)
        {
            if (i < 0 || i >= LashCount) { throw new ArgumentOutOfRangeException(nameof(i)); }
            return _lash[i];
        }

        /// <summary>連鎖の i 番目の点（0 は自機）。</summary>
        public Vec2i ArcPoint(int i)
        {
            if (i < 0 || i >= ArcCount) { throw new ArgumentOutOfRangeException(nameof(i)); }
            return _arc[i];
        }

        internal void SetLash(int i, Rect rect) => _lash[i] = rect;

        internal void SetArc(int i, Vec2i point) => _arc[i] = point;

        internal WeaponEffects Clone()
        {
            var copy = (WeaponEffects)MemberwiseClone();
            copy._lash = (Rect[])_lash.Clone();
            copy._arc = (Vec2i[])_arc.Clone();
            return copy;
        }
    }

    /// <summary>武器の発動・弾・自機への接触。</summary>
    public sealed partial class TideRun
    {
        private int[] _nearIndex = null!;
        private long[] _nearDistance = null!;
        private Touch[] _hitTouch = null!;

        /// <summary>
        /// 武器を定義の順に発動する。間隔のある武器は、発動の待ちが 0 になった Tick に発動し、待ちを間隔に戻す
        /// （Bolt・Arc は射程に敵がいなければ発動せず、待ち 0 のまま次の Tick も試す）。Orbit は毎 Tick 回って当たる。
        /// </summary>
        private void FireWeapons()
        {
            for (var w = 0; w < _weaponLevel.Length; w++)
            {
                if (_weaponLevel[w] == 0) { continue; }
                var weapon = _config.Weapons[w];
                var stats = weapon.At(_weaponLevel[w]);
                if (weapon.Kind == WeaponKind.Orbit)
                {
                    SpinOrbit(w, stats);
                    continue;
                }

                if (_weaponCooldown[w] > 0) { _weaponCooldown[w]--; }
                if (_weaponCooldown[w] > 0) { continue; }
                var fired = weapon.Kind switch
                {
                    WeaponKind.Bolt => FireBolt(stats),
                    WeaponKind.Pulse => FirePulse(stats),
                    WeaponKind.Lash => FireLash(stats),
                    _ => FireArc(stats),
                };
                if (fired) { _weaponCooldown[w] = stats.IntervalTicks; }
            }
        }

        /// <summary>射程の中の近い順に <see cref="WeaponLevel.Count"/> 体を狙う（敵が少なければ近い敵から順に重ねて狙う）。</summary>
        private bool FireBolt(WeaponLevel stats)
        {
            var found = Nearest(Player, stats.Range, stats.Count, false);
            if (found == 0) { return false; }
            for (var k = 0; k < stats.Count; k++)
            {
                var velocity = IntMath.Scale(Position(_nearIndex[k % found]) - Player, stats.Speed);
                if (velocity.IsZero) { velocity = IntMath.Scale(new Vec2i(FacingX, 0), stats.Speed); }
                _projectiles.Add(_nextId++, Player, velocity, stats.LifeTicks, stats.Damage, stats.Size, stats.Pierce);
            }

            _events |= RunEvents.BoltFired;
            return true;
        }

        /// <summary>
        /// 刃を回して当てる。刃は Tick の始めの位置（始めの自機・角度）から終わりの位置へ直線で動くとみなし、敵の軌跡と掃引する
        /// （速く回る刃が敵を飛び越えない。円弧を弦で近似する）。同じ敵へは <see cref="WeaponLevel.IntervalTicks"/> ごとに 1 回。
        /// </summary>
        private void SpinOrbit(int w, WeaponLevel stats)
        {
            var startAngle = _orbitAngle[w];
            var endAngle = Trig.Normalize(startAngle + stats.Speed);
            _orbitAngle[w] = endAngle;
            NextStamp();
            for (var b = 0; b < stats.Count; b++)
            {
                var from = BladeAt(PlayerStart, startAngle, b, stats);
                var to = BladeAt(Player, endAngle, b, stats);
                var reach = stats.Size + _largestRadius + (2 * TideConfig.MaxSpeed);
                var found = _grid.Collect(Math.Min(from.X, to.X) - reach, Math.Min(from.Y, to.Y) - reach, Math.Max(from.X, to.X) + reach, Math.Max(from.Y, to.Y) + reach, _candidates);
                for (var c = 0; c < found; c++)
                {
                    var e = _candidates[c];
                    if (_enemies.Hp[e] <= 0 || _enemies.OrbitWait[e] > 0 || _stamp[e] == _stampValue) { continue; }
                    if (!Sweep.Touches(from, to, StartOf(e), Position(e), stats.Size + RadiusOf(e))) { continue; }
                    _stamp[e] = _stampValue;
                    _enemies.OrbitWait[e] = stats.IntervalTicks;
                    Damage(e, stats.Damage);
                }
            }
        }

        /// <summary>自機を中心とする半径 <see cref="WeaponLevel.Range"/> の円に重なる敵すべてへ。</summary>
        private bool FirePulse(WeaponLevel stats)
        {
            var reach = stats.Range + _largestRadius;
            var found = _grid.Collect(Player.X - reach, Player.Y - reach, Player.X + reach, Player.Y + reach, _candidates);
            for (var c = 0; c < found; c++)
            {
                var e = _candidates[c];
                if (_enemies.Hp[e] <= 0) { continue; }
                var sum = (long)stats.Range + RadiusOf(e);
                if (Vec2i.DistanceSquared(Position(e), Player) < sum * sum) { Damage(e, stats.Damage); }
            }

            Effects.PulseTick = Elapsed;
            Effects.PulseCenter = Player;
            Effects.PulseRange = stats.Range;
            _events |= RunEvents.Pulsed;
            return true;
        }

        /// <summary>向いている左右（Count 2 なら両側）の矩形に重なる敵へ。両側に重なる敵にも 1 回だけ当たる。</summary>
        private bool FireLash(WeaponLevel stats)
        {
            NextStamp();
            for (var s = 0; s < stats.Count; s++)
            {
                var side = s == 0 ? FacingX : -FacingX;
                var reachX = Player.X + (side * stats.Range);
                var rect = new Rect(Math.Min(Player.X, reachX), Player.Y - stats.Size, Math.Max(Player.X, reachX), Player.Y + stats.Size);
                Effects.SetLash(s, rect);
                var found = _grid.Collect(rect.MinX - _largestRadius, rect.MinY - _largestRadius, rect.MaxX + _largestRadius, rect.MaxY + _largestRadius, _candidates);
                for (var c = 0; c < found; c++)
                {
                    var e = _candidates[c];
                    if (_enemies.Hp[e] <= 0 || _stamp[e] == _stampValue || !rect.Overlaps(Position(e), RadiusOf(e))) { continue; }
                    _stamp[e] = _stampValue;
                    Damage(e, stats.Damage);
                }
            }

            Effects.LashTick = Elapsed;
            Effects.LashCount = stats.Count;
            _events |= RunEvents.Lashed;
            return true;
        }

        /// <summary>射程の中の最も近い敵から、1 段の距離の中の最も近い（まだ打っていない）敵へ順に連鎖する。</summary>
        private bool FireArc(WeaponLevel stats)
        {
            NextStamp();
            var from = Player;
            var reach = stats.Range;
            var count = 0;
            Effects.SetArc(0, Player);
            for (var k = 0; k < stats.Count; k++)
            {
                if (Nearest(from, reach, 1, true) == 0) { break; }
                var e = _nearIndex[0];
                _stamp[e] = _stampValue;
                from = Position(e);
                reach = stats.Size;
                Effects.SetArc(++count, from);
                Damage(e, stats.Damage);
            }

            if (count == 0) { return false; }
            Effects.ArcTick = Elapsed;
            Effects.ArcCount = count + 1;
            _events |= RunEvents.Arced;
            return true;
        }

        /// <summary>
        /// 弾を動かし、Tick の中で触れた時刻の順に、まだ当たっていない敵へ貫通数まで当てる（同じ時刻なら番号の小さい敵）。
        /// 弾は並びの順に解決し、先の弾が倒した敵は素通りする（倒れた敵に弾を使わない）。
        /// </summary>
        private void MoveProjectiles()
        {
            for (var p = 0; p < _projectiles.Count; p++)
            {
                var start = new Vec2i(_projectiles.X[p], _projectiles.Y[p]);
                var end = start + new Vec2i(_projectiles.Vx[p], _projectiles.Vy[p]);
                _projectiles.StartX[p] = start.X;
                _projectiles.StartY[p] = start.Y;
                _projectiles.X[p] = end.X;
                _projectiles.Y[p] = end.Y;
                _projectiles.Life[p]--;

                var radius = _projectiles.Radius[p];
                var reach = radius + _largestRadius + (2 * TideConfig.MaxSpeed);
                var found = _grid.Collect(Math.Min(start.X, end.X) - reach, Math.Min(start.Y, end.Y) - reach, Math.Max(start.X, end.X) + reach, Math.Max(start.Y, end.Y) + reach, _candidates);
                var hits = 0;
                var limit = _projectiles.PierceLeft[p];
                for (var c = 0; c < found; c++)
                {
                    var e = _candidates[c];
                    if (_enemies.Hp[e] <= 0 || _projectiles.HasHit(p, _enemies.IdOf[e])) { continue; }
                    var touch = Sweep.First(start, end, StartOf(e), Position(e), radius + RadiusOf(e));
                    if (touch.Hit) { hits = InsertHit(e, touch, hits, limit); }
                }

                for (var h = 0; h < hits; h++)
                {
                    var e = _nearIndex[h];
                    _projectiles.RecordHit(p, _enemies.IdOf[e]);
                    Damage(e, _projectiles.Damage[p]);
                }
            }

            _projectiles.RemoveSpent();
        }

        /// <summary>触れた時刻の早い順（同時なら番号の小さい順）に <paramref name="limit"/> 件までを保つ。</summary>
        private int InsertHit(int e, Touch touch, int count, int limit)
        {
            var at = count;
            while (at > 0 && Before(e, touch, _nearIndex[at - 1], _hitTouch[at - 1])) { at--; }
            if (at >= limit) { return count; }
            var last = Math.Min(count, limit - 1);
            for (var k = last; k > at; k--)
            {
                _nearIndex[k] = _nearIndex[k - 1];
                _hitTouch[k] = _hitTouch[k - 1];
            }

            _nearIndex[at] = e;
            _hitTouch[at] = touch;
            return Math.Min(count + 1, limit);
        }

        private bool Before(int a, Touch touchA, int b, Touch touchB)
        {
            if (touchA.IsEarlierThan(touchB)) { return true; }
            return !touchB.IsEarlierThan(touchA) && _enemies.IdOf[a] < _enemies.IdOf[b];
        }

        /// <summary>無敵でなければ、Tick の中で最初に触れた敵（同時なら番号の小さい敵）の接触ダメージを受ける。敵は消えない。</summary>
        private void TouchPlayer()
        {
            if (_invincible.IsActive) { return; }
            var reach = _config.PlayerRadius + _largestRadius + (2 * TideConfig.MaxSpeed);
            var found = _grid.Collect(Math.Min(PlayerStart.X, Player.X) - reach, Math.Min(PlayerStart.Y, Player.Y) - reach, Math.Max(PlayerStart.X, Player.X) + reach, Math.Max(PlayerStart.Y, Player.Y) + reach, _candidates);
            var target = -1;
            var first = Touch.None;
            for (var c = 0; c < found; c++)
            {
                var e = _candidates[c];
                if (_enemies.Hp[e] <= 0 || _config.Enemies[_enemies.DefOf[e]].ContactDamage == 0) { continue; }
                var touch = Sweep.First(PlayerStart, Player, StartOf(e), Position(e), _config.PlayerRadius + RadiusOf(e));
                if (touch.Hit && (target < 0 || Before(e, touch, target, first)))
                {
                    target = e;
                    first = touch;
                }
            }

            if (target < 0) { return; }
            Health = Math.Max(0, Health - _config.Enemies[_enemies.DefOf[target]].ContactDamage);
            _invincible.Arm(_config.InvincibleTicks);
            _events |= RunEvents.Hurt;
            if (Health == 0) { Finish(RunStatus.Lost, RunEnd.Died); }
        }

        private void Damage(int e, int amount)
        {
            if (_enemies.Hp[e] <= 0) { return; }
            _enemies.Hp[e] = Math.Max(0, _enemies.Hp[e] - amount);
            _enemies.Flash[e] = EnemySet.FlashTicks;
            _events |= RunEvents.EnemyHit;
            if (_enemies.Hp[e] > 0) { return; }

            Kills++;
            _events |= RunEvents.EnemyKilled;
            var def = _enemies.DefOf[e];
            DropGem(Position(e), _config.Enemies[def].Experience);
            if (def == _bossDef) { Finish(RunStatus.Won, RunEnd.BossDefeated); }
        }

        /// <summary>
        /// 点 <paramref name="from"/> から中心の距離が <paramref name="range"/> 未満の生きている敵を、近い順（同じ距離なら番号の小さい順）に
        /// <paramref name="k"/> 体まで <see cref="_nearIndex"/> に書き、数を返す。<paramref name="skipStamped"/> なら今の印のついた敵を除く。
        /// </summary>
        private int Nearest(Vec2i from, int range, int k, bool skipStamped)
        {
            var found = _grid.Collect(from.X - range, from.Y - range, from.X + range, from.Y + range, _candidates);
            var limit = (long)range * range;
            var count = 0;
            for (var c = 0; c < found; c++)
            {
                var e = _candidates[c];
                if (_enemies.Hp[e] <= 0 || (skipStamped && _stamp[e] == _stampValue)) { continue; }
                var distance = Vec2i.DistanceSquared(Position(e), from);
                if (distance >= limit) { continue; }

                var at = count;
                while (at > 0 && (distance < _nearDistance[at - 1] || (distance == _nearDistance[at - 1] && _enemies.IdOf[e] < _enemies.IdOf[_nearIndex[at - 1]]))) { at--; }
                if (at >= k) { continue; }
                var last = Math.Min(count, k - 1);
                for (var m = last; m > at; m--)
                {
                    _nearIndex[m] = _nearIndex[m - 1];
                    _nearDistance[m] = _nearDistance[m - 1];
                }

                _nearIndex[at] = e;
                _nearDistance[at] = distance;
                count = Math.Min(count + 1, k);
            }

            return count;
        }

        private void NextStamp()
        {
            _stampValue++;
            if (_stampValue != int.MaxValue) { return; }
            Array.Clear(_stamp, 0, _stamp.Length);
            _stampValue = 1;
        }

        private Vec2i StartOf(int e) => new(_enemies.StartX[e], _enemies.StartY[e]);

        private int RadiusOf(int e) => _config.Enemies[_enemies.DefOf[e]].Radius;

        private static Vec2i BladeAt(Vec2i center, int angle, int blade, WeaponLevel stats)
        {
            var a = angle + (blade * (Trig.FullTurn / stats.Count));
            return center + new Vec2i(
                IntMath.RoundDiv((long)Trig.Cos(a) * stats.Range, Trig.One),
                IntMath.RoundDiv((long)Trig.Sin(a) * stats.Range, Trig.One));
        }
    }
}
