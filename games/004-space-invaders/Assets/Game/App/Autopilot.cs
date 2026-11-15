using System;
using Game.Core;

namespace Game.App
{
    public static class Autopilot
    {
        public static PlayerCommand Next(InvadersState game, InvadersConfig config)
        {
            if (game == null) { throw new ArgumentNullException(nameof(game)); }
            if (config == null) { throw new ArgumentNullException(nameof(config)); }
            var target = game.PlayerX;
            var best = float.MaxValue;
            for (var e = 0; e < game.EnemyCapacity; e++)
            {
                if (!game.EnemyAlive(e)) { continue; }
                var enemy = game.EnemyAt(e);
                var lead = (enemy.Y - InvadersConfig.PlayerY) / config.PlayerBulletSpeed;
                var aim = Math.Clamp(enemy.X + game.Direction * (config.FormationSpeed + (game.Wave - 1) * config.WaveSpeedIncrease) * lead, -9f, 9f);
                var distance = Math.Abs(aim - game.PlayerX);
                if (distance < best) { best = distance; target = aim; }
            }
            for (var b = 0; b < game.BulletCount; b++)
            {
                var bullet = game.BulletAt(b);
                if (bullet.Owner != BulletOwner.Enemy) { continue; }
                var time = (bullet.Position.Y - InvadersConfig.PlayerY) / config.EnemyBulletSpeed;
                if (time < -.1f || time > .8f) { continue; }
                if (Math.Abs(bullet.Position.X - game.PlayerX) < 1.2f)
                {
                    var side = game.PlayerX < bullet.Position.X ? -1 : 1;
                    if (game.PlayerX < -8f) { side = 1; }
                    if (game.PlayerX > 8f) { side = -1; }
                    target = bullet.Position.X + side * 1.4f;
                    break;
                }
            }
            return new PlayerCommand(Math.Abs(target - game.PlayerX) < .08f ? 0 : Math.Sign(target - game.PlayerX), true);
        }
    }
}
