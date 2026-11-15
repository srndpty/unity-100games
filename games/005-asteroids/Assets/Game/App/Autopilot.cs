using System;
using Game.Core;

namespace Game.App
{
    public static class Autopilot
    {
        public static PlayerCommand Next(AsteroidsState game, AsteroidsConfig config)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            if (game.Phase != GamePhase.Playing || game.AsteroidCount == 0) { return default; }
            var best = float.MaxValue; var direction = Point.Forward(game.ShipAngle);
            var aim = game.ShipAngle;
            for (var i = 0; i < game.AsteroidCount; i++)
            {
                var asteroid = game.AsteroidAt(i);
                var offset = Torus.Difference(asteroid.Position, game.ShipPosition);
                var relative = asteroid.Velocity.Plus(game.ShipVelocity.Scale(-1));
                var time = offset.Length / config.BulletSpeed;
                for (var j = 0; j < 4; j++) { direction = Torus.Wrap(offset.Plus(relative.Scale(time))); time = direction.Length / config.BulletSpeed; }
                var angle = MathF.Atan2(-direction.X, direction.Y);
                var cost = time + Math.Abs(Torus.Wrap(angle - game.ShipAngle, MathF.PI * 2)) * .25f;
                if (cost < best) { best = cost; aim = angle; }
            }
            // 操縦専用の乱数。Coreの乱数状態を消費せず、同じ試合を単独で再現できる。
            var random = unchecked((uint)game.Seed ^ (uint)(game.TickCount / 30 + 1) * 0x9e3779b9u);
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            var thrust = random % 7 == 0;
            var error = Torus.Wrap(aim - game.ShipAngle, MathF.PI * 2);
            var step = config.RotationDegrees * MathF.PI / 180 / AsteroidsConfig.TickRate;
            return new PlayerCommand(Math.Abs(error) < step * .55f ? 0 : Math.Sign(error), thrust, true);
        }
    }
}
