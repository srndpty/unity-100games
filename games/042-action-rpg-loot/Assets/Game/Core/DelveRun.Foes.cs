using System;
using IntGeometry;
using TileMotion;

namespace Game.Core
{
    /// <summary>敵の動きと飛び道具。</summary>
    public sealed partial class DelveRun
    {
        /// <summary>近接の打撃が届く余裕（構えの間に少し離れても当たる）。</summary>
        private const int StrikeLeeway = 32;

        private void StepFoes()
        {
            if (Status == RunStatus.Won) { return; }
            var h = _hero;
            foreach (var foe in _foes)
            {
                if (foe.Dead) { continue; }
                var stats = _config.Foe(foe.Kind);
                var toHero = h.Position - foe.Position;
                long dist = IntMath.Sqrt(toHero.LengthSquared);
                if (!foe.Aggro)
                {
                    if (h.Dead || dist > stats.Aggro) { continue; }
                    foe.Aggro = true;
                }

                if (h.Dead)
                {
                    // 自機が倒れている間は構えを解いて待つ（復活で初めの位置へ戻る）。
                    if (foe.State != FoeState.Charge) { foe.State = FoeState.Move; }
                }

                switch (foe.Kind)
                {
                    case FoeKind.Grunt: StepMelee(foe, stats, toHero, dist); break;
                    case FoeKind.Archer: StepArcher(foe, stats, toHero, dist); break;
                    case FoeKind.Charger: StepCharger(foe, stats, toHero, dist); break;
                    default: StepBoss(foe, stats, toHero, dist); break;
                }
            }
        }

        private void StepMelee(Foe foe, FoeStats stats, Vec2i toHero, long dist)
        {
            switch (foe.State)
            {
                case FoeState.Move:
                    if (_hero.Dead) { return; }
                    if (dist - stats.Radius - _config.HeroRadius <= stats.Range)
                    {
                        foe.State = FoeState.Windup;
                        foe.Timer = stats.Windup;
                        foe.Direction = toHero.IsZero ? foe.Direction : toHero;
                        return;
                    }

                    Walk(foe, stats, toHero, stats.Speed);
                    return;
                case FoeState.Windup:
                    if (--foe.Timer > 0) { return; }
                    if (dist <= stats.Radius + _config.HeroRadius + stats.Range + StrikeLeeway) { HurtHero(foe.Damage, stats.Element); }
                    foe.State = FoeState.Recover;
                    foe.Timer = stats.Cooldown;
                    return;
                default:
                    StepChargeStates(foe, stats);
                    return;
            }
        }

        private void StepArcher(Foe foe, FoeStats stats, Vec2i toHero, long dist)
        {
            switch (foe.State)
            {
                case FoeState.Move:
                    if (foe.Timer > 0) { foe.Timer--; }
                    if (_hero.Dead) { return; }
                    if (dist < stats.Keep) { Walk(foe, stats, -toHero, stats.Speed); }
                    else if (dist > stats.Range) { Walk(foe, stats, toHero, stats.Speed); }

                    if (foe.Timer == 0 && dist <= stats.Range + StrikeLeeway)
                    {
                        foe.State = FoeState.Windup;
                        foe.Timer = stats.Windup;
                    }

                    return;
                case FoeState.Windup:
                    if (--foe.Timer > 0) { return; }
                    Shoot(foe, stats, toHero, foe.Damage, stats.Element);
                    foe.State = FoeState.Move;
                    foe.Timer = stats.Cooldown;
                    return;
                default:
                    foe.State = FoeState.Move;
                    return;
            }
        }

        private void StepCharger(Foe foe, FoeStats stats, Vec2i toHero, long dist)
        {
            if (foe.State == FoeState.Move)
            {
                if (foe.Timer > 0) { foe.Timer--; }
                if (_hero.Dead) { return; }
                if (foe.Timer == 0 && dist <= stats.Range)
                {
                    BeginChargeWindup(foe, stats, toHero);
                    return;
                }

                Walk(foe, stats, toHero, stats.Speed);
                return;
            }

            StepChargeStates(foe, stats);
        }

        private void StepBoss(Foe foe, FoeStats stats, Vec2i toHero, long dist)
        {
            if (!_hero.Dead)
            {
                if (--foe.FanTimer <= 0)
                {
                    FireFan(foe, stats, toHero);
                    foe.FanTimer = foe.Enraged ? _config.BossFanIntervalEnraged : _config.BossFanInterval;
                }

                if (foe.Enraged && foe.ChargeTimer > 0) { foe.ChargeTimer--; }
            }

            if (foe.State == FoeState.Move && foe.Enraged && foe.ChargeTimer == 0 && !_hero.Dead && dist > stats.Radius + _config.HeroRadius + stats.Range)
            {
                BeginChargeWindup(foe, stats, toHero);
                foe.ChargeTimer = _config.BossChargeInterval;
                return;
            }

            StepMelee(foe, stats, toHero, dist);
        }

        private static void BeginChargeWindup(Foe foe, FoeStats stats, Vec2i toHero)
        {
            foe.State = FoeState.ChargeWindup;
            foe.Timer = stats.Windup;
            foe.Direction = toHero.IsZero ? foe.Direction : toHero;
        }

        /// <summary>突進の構え・突進・その後の待ち（近接の待ちも同じ）。</summary>
        private void StepChargeStates(Foe foe, FoeStats stats)
        {
            switch (foe.State)
            {
                case FoeState.ChargeWindup:
                    if (--foe.Timer > 0) { return; }
                    foe.State = FoeState.Charge;
                    foe.Timer = stats.ChargeTicks;
                    foe.ChargeHit = false;
                    return;
                case FoeState.Charge:
                    {
                        var start = foe.Position;
                        bool blocked = Walk(foe, stats, foe.Direction, stats.ChargeSpeed);
                        if (!foe.ChargeHit && !_hero.Dead
                            && Sweep.Touches(start, foe.Position, _hero.Position, _hero.Position, stats.Radius + _config.HeroRadius))
                        {
                            foe.ChargeHit = true;
                            HurtHero(foe.Damage, stats.Element);
                        }

                        if (blocked || --foe.Timer <= 0)
                        {
                            foe.State = FoeState.Recover;
                            foe.Timer = stats.Cooldown;
                        }

                        return;
                    }

                case FoeState.Recover:
                    if (--foe.Timer > 0) { return; }
                    foe.State = FoeState.Move;
                    foe.Timer = 0;
                    return;
                default:
                    foe.State = FoeState.Move;
                    return;
            }
        }

        /// <summary>壁で止まりながら向きへ歩く。壁に当たったら true。</summary>
        private bool Walk(Foe foe, FoeStats stats, Vec2i direction, int speed)
        {
            if (direction.IsZero || speed == 0) { return false; }
            var v = IntMath.Scale(direction, speed);
            int side = Math.Min(stats.Radius * 2, Units.Tile - 16);
            int half = side / 2;
            var box = new Box(foe.Position.X - half, foe.Position.Y - half, side, side);
            bool hitX = BodyMotion.MoveX(Map, ref box, v.X);
            bool hitY = BodyMotion.MoveY(Map, ref box, v.Y);
            foe.Position = new Vec2i(box.X + half, box.Y + half);
            return hitX || hitY;
        }

        private void Shoot(Foe foe, FoeStats stats, Vec2i direction, int damage, Element element)
        {
            if (_projectiles.Count >= _config.ProjectileCap || direction.IsZero) { return; }
            var velocity = IntMath.Scale(direction, stats.ProjectileSpeed);
            _projectiles.Add(new Projectile(foe.Position, velocity, stats.ProjectileRadius, damage, element, foe.Kind));
            _events |= RunEvents.FoeShot;
        }

        private void FireFan(Foe foe, FoeStats stats, Vec2i toHero)
        {
            int center = Trig.Atan2(toHero.Y, toHero.X, Trig.Down);
            int count = _config.BossFanCount;
            for (int i = 0; i < count; i++)
            {
                int angle = center + ((2 * i) - (count - 1)) * _config.BossFanSpread / 2;
                var dir = new Vec2i(Trig.Cos(angle), Trig.Sin(angle));
                Shoot(foe, stats, dir, _config.BossFanDamage, Element.Fire);
            }
        }

        private void StepProjectiles()
        {
            if (_projectiles.Count == 0) { return; }
            int write = 0;
            for (int i = 0; i < _projectiles.Count; i++)
            {
                var p = _projectiles[i];
                var start = p.Position;
                var end = start + p.Velocity;
                bool alive = true;
                if (!_hero.Dead && _hero.Invuln == 0
                    && Sweep.Touches(start, end, _hero.Position, _hero.Position, p.Radius + _config.HeroRadius))
                {
                    HurtHero(p.Damage, p.Element);
                    alive = false;
                }
                else if (Map.IsSolid(FloorMap.ColumnOf(end.X), FloorMap.RowOf(end.Y)))
                {
                    alive = false;
                }

                p.Position = end;
                if (alive) { _projectiles[write++] = p; }
            }

            _projectiles.RemoveRange(write, _projectiles.Count - write);
        }
    }
}
