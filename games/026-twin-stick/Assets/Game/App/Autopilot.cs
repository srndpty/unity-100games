using System;
using Game.Core;
using TileMotion;

namespace Game.App
{
    /// <summary>
    /// 自動操縦（撮影・テスト用）。照準と移動を別々に決める：
    /// 照準はいちばん近い敵の少し先（弾が届くまでの移動を見込む）、移動は 9 通り（止まるを含む）から、
    /// 数 Tick 先までの敵との隙間がいちばん広く、アリーナの中央に近い向きを選ぶ。
    /// 判断の状態を持たないので、試合をまたいで持ち越すものはない。整数だけで計算する（Mono と .NET で一致）。
    /// </summary>
    public sealed class Autopilot : IPilot
    {
        /// <summary>先読みする時刻（Tick 後）。</summary>
        private static readonly int[] _horizons = { 4, 10, 18 };

        /// <summary>これ以上の隙間は同じに扱う（中央への寄りで選ぶ）。</summary>
        private const long SafeGap = 3 * Units.Tile;

        public ShipCommand CommandFor(SiegeRun run) => Decide(run);

        public static ShipCommand Decide(SiegeRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            var config = run.Config;
            var (aim, hasTarget) = AimPoint(run, config);
            var (dx, dy) = ChooseMove(run, config);
            return new ShipCommand(dx, dy, hasTarget, aim);
        }

        private static (Vec2i Aim, bool HasTarget) AimPoint(SiegeRun run, SiegeConfig config)
        {
            var ship = run.Ship.Position;
            Enemy? nearest = null;
            var best = long.MaxValue;
            foreach (var enemy in run.Enemies)
            {
                if (!enemy.IsActive) { continue; }
                var distance = Vec2i.DistanceSquared(enemy.Position, ship);
                if (distance < best)
                {
                    best = distance;
                    nearest = enemy;
                }
            }

            if (nearest == null) { return (ship + new Vec2i(Units.Tile, 0), false); }
            var lead = (int)Math.Min(30, IntMath.Sqrt(best) / config.BulletSpeed);
            var moving = nearest.Kind == EnemyKind.Lancer && nearest.Mode != LancerMode.Dash ? Vec2i.Zero : nearest.Velocity;
            return (nearest.Position + new Vec2i(moving.X * lead, moving.Y * lead), true);
        }

        private static (int Dx, int Dy) ChooseMove(SiegeRun run, SiegeConfig config)
        {
            var bestScore = long.MinValue;
            var choice = (0, 0);
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var score = Evaluate(run, config, dx, dy);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        choice = (dx, dy);
                    }
                }
            }

            return choice;
        }

        private static long Evaluate(SiegeRun run, SiegeConfig config, int dx, int dy)
        {
            var speed = dx != 0 && dy != 0 ? SiegeConfig.DiagonalOf(config.ShipSpeed) : config.ShipSpeed;
            var start = run.Ship.Position;
            var gap = SafeGap;
            var final = start;
            foreach (var t in _horizons)
            {
                var ship = Clamp(start + new Vec2i(dx * speed * t, dy * speed * t), config.ShipRadius, config);
                final = ship;
                foreach (var enemy in run.Enemies)
                {
                    var future = Predict(enemy, ship, t, config);
                    var clearance = IntMath.Sqrt(Vec2i.DistanceSquared(future, ship)) - enemy.Radius - config.ShipRadius;
                    if (clearance < gap) { gap = clearance; }
                }
            }

            // 隙間を最優先にし、同じなら中央に近い・壁から離れた向きを選ぶ。
            var center = new Vec2i(config.ArenaWidth / 2, config.ArenaHeight / 2);
            var fromCenter = IntMath.Sqrt(Vec2i.DistanceSquared(final, center));
            return (gap * 64) - fromCenter;
        }

        /// <summary><paramref name="ticks"/> Tick 後の敵の位置のおおよそ（予告中は出現位置にとどまる）。</summary>
        private static Vec2i Predict(Enemy enemy, Vec2i ship, int ticks, SiegeConfig config)
        {
            if (!enemy.IsActive) { return enemy.Position; }
            Vec2i velocity;
            switch (enemy.Kind)
            {
                case EnemyKind.Chaser:
                    velocity = IntMath.Scale(ship - enemy.Position, config.Chaser.Speed);
                    break;
                case EnemyKind.Lancer:
                    // 狙い中・突進中は突進の向きへ進むとみなす（用心する）。
                    velocity = enemy.Mode == LancerMode.Rest ? IntMath.Scale(ship - enemy.Position, config.Lancer.Speed) : enemy.Velocity;
                    break;
                default:
                    velocity = enemy.Velocity;
                    break;
            }

            return Clamp(enemy.Position + new Vec2i(velocity.X * ticks, velocity.Y * ticks), enemy.Radius, config);
        }

        private static Vec2i Clamp(Vec2i position, int radius, SiegeConfig config) => new(
            IntMath.Clamp(position.X, radius, config.ArenaWidth - radius),
            IntMath.Clamp(position.Y, radius, config.ArenaHeight - radius));
    }
}
