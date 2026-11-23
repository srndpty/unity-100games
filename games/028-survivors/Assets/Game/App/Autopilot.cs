using System;
using Game.Core;
using IntGeometry;
using TileMotion;

namespace Game.App
{
    /// <summary>
    /// 自動操縦（撮影・テスト用）。移動は 9 通り（止まるを含む）から、数 Tick 先の自機の位置で「近くの敵との隙間」の危なさが
    /// いちばん小さく、近くの宝石へ寄る向きを選ぶ。3 択は「持っていない武器（番号の小さい順）→ レベルの低い武器の強化 → 回復」の固定の好み。
    /// 判断の状態を持たないので、試合をまたいで持ち越すものはない（023 の知見）。整数だけで計算する（Mono と .NET で一致）。
    /// </summary>
    public sealed class Autopilot : IPilot
    {
        /// <summary>何 Tick 先の位置で比べるか。</summary>
        private const int Horizon = 10;

        /// <summary>この距離より遠い敵は見ない。</summary>
        private const int Watch = 8 * Units.Tile;

        /// <summary>敵との隙間がこれより狭いと危ないとみなす。</summary>
        private const long Comfort = 3 * Units.Tile / 2;

        /// <summary>この距離の中の宝石へ寄る。</summary>
        private const int GemReach = 10 * Units.Tile;

        private readonly int[] _near = new int[TideConfig.MaxEnemiesLimit + 1];
        private TideRun? _configOwner;
        private TideConfig _config = new();

        public PlayerCommand CommandFor(TideRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            if (!ReferenceEquals(_configOwner, run))
            {
                // 設定の複製は試合ごとに 1 回だけ（試合の判断ではなく、読むための写し）。
                _config = run.Config;
                _configOwner = run;
            }

            var config = _config;
            var player = run.Player;
            var boss = run.BossIndex();

            // 近くの敵だけを先に選ぶ（9 通りで毎回全員を見ない）。
            var count = 0;
            var watch = (long)Watch * Watch;
            for (var i = 0; i < run.Enemies.Count; i++)
            {
                if (Vec2i.DistanceSquared(run.Enemies.Position(i), player) < watch) { _near[count++] = i; }
            }

            var gem = NearestGem(run, player);
            var (target, keep) = EngageTarget(run, player, boss);
            var best = long.MinValue;
            var choice = PlayerCommand.Idle;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var speed = dx != 0 && dy != 0 ? TideConfig.DiagonalOf(config.PlayerSpeed) : config.PlayerSpeed;
                    var future = player + new Vec2i(dx * speed * Horizon, dy * speed * Horizon);
                    var score = -Danger(run, config, future, count, boss);
                    if (gem.HasValue) { score -= IntMath.Sqrt(Vec2i.DistanceSquared(future, gem.Value)) * 4; }
                    if (target.HasValue) { score -= Math.Abs(IntMath.Sqrt(Vec2i.DistanceSquared(future, target.Value)) - keep) * 2; }
                    if (score > best)
                    {
                        best = score;
                        choice = new PlayerCommand(dx, dy);
                    }
                }
            }

            return choice;
        }

        public int ChoiceFor(TideRun run)
        {
            if (run == null) { throw new ArgumentNullException(nameof(run)); }
            var pick = 0;
            var rank = int.MaxValue;
            for (var i = 0; i < run.ChoiceCount; i++)
            {
                var choice = run.ChoiceAt(i);
                var value = choice.Kind switch
                {
                    ChoiceKind.Acquire => choice.Weapon,
                    ChoiceKind.Upgrade => 100 + (choice.Level * 16) + choice.Weapon,
                    _ => 10000,
                };
                if (value < rank)
                {
                    rank = value;
                    pick = i;
                }
            }

            return pick;
        }

        /// <summary>
        /// 武器の届く距離に保つ相手と、保つ距離。ボスが出ていればボス（体の外から 2 セル）、いなければいちばん近い敵（3 セル）。
        /// 逃げ続けると近接の武器（Orbit・Pulse・Lash）が当たらず、倒せずに時間切れになるため。
        /// </summary>
        private static (Vec2i? Target, long Keep) EngageTarget(TideRun run, Vec2i player, int boss)
        {
            if (boss >= 0) { return (run.Enemies.Position(boss), run.EnemyRadius(boss) + (2L * Units.Tile)); }
            Vec2i? nearest = null;
            var best = long.MaxValue;
            for (var i = 0; i < run.Enemies.Count; i++)
            {
                var distance = Vec2i.DistanceSquared(run.Enemies.Position(i), player);
                if (distance < best)
                {
                    best = distance;
                    nearest = run.Enemies.Position(i);
                }
            }

            return (nearest, 3L * Units.Tile);
        }

        /// <summary>位置 <paramref name="at"/> の危なさ：近くの敵の数 Tick 先の位置との隙間が 1.5 セルを切ると急に大きくなる。</summary>
        private long Danger(TideRun run, TideConfig config, Vec2i at, int count, int boss)
        {
            long danger = 0;
            for (var k = 0; k < count; k++)
            {
                var i = _near[k];
                var velocity = run.Enemies.Velocity(i);
                var enemy = run.Enemies.Position(i) + new Vec2i(velocity.X * Horizon, velocity.Y * Horizon);
                var radius = run.EnemyRadius(i) + config.PlayerRadius;
                var gap = IntMath.Sqrt(Vec2i.DistanceSquared(at, enemy)) - radius;
                var close = Comfort - gap;
                if (close <= 0) { continue; }
                var weight = boss == i ? 4 : 1;
                danger += weight * close * close / 16;
            }

            return danger;
        }

        private static Vec2i? NearestGem(TideRun run, Vec2i player)
        {
            Vec2i? nearest = null;
            var best = (long)GemReach * GemReach;
            for (var i = 0; i < run.Gems.Count; i++)
            {
                var distance = Vec2i.DistanceSquared(run.Gems.Position(i), player);
                if (distance < best)
                {
                    best = distance;
                    nearest = run.Gems.Position(i);
                }
            }

            return nearest;
        }
    }
}
