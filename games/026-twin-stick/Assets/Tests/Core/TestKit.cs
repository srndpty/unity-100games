using System;
using TileMotion;

namespace Game.Core.Tests
{
    /// <summary>テスト用の設定と操作の補助。</summary>
    internal static class TestKit
    {
        public const int T = Units.Tile;

        public static SiegeConfig Config() => new();

        /// <summary>敵が出ない設定（区間 1 つ・試合の長さより長い間隔）。</summary>
        public static SiegeConfig Quiet(Action<SiegeConfig>? edit = null)
        {
            var config = new SiegeConfig
            {
                StageTicks = SiegeConfig.MaxDurationTicks,
                Stages = new[] { new SpawnStage { IntervalTicks = SiegeConfig.MaxDurationTicks, ChaserWeight = 1 } },
            };
            edit?.Invoke(config);
            return config;
        }

        public static SiegeRun Run(SiegeConfig? config = null, int seed = 1) => new(config ?? Quiet(), seed);

        /// <summary>動かず撃たず、自機の右を狙う。</summary>
        public static ShipCommand Idle(SiegeRun run) => new(0, 0, false, run.Ship.Position + new Vec2i(T, 0));

        public static ShipCommand Move(int x, int y, Vec2i aim, bool fire = false) => new(x, y, fire, aim);

        /// <summary><paramref name="ticks"/> Tick の間、何もしない。</summary>
        public static RunEvents Wait(SiegeRun run, int ticks)
        {
            var events = RunEvents.None;
            for (var i = 0; i < ticks; i++)
            {
                run.Step(Idle(run));
                events |= run.TakeEvents();
            }

            return events;
        }

        public static Vec2i At(int x, int y) => new(x, y);

        /// <summary>アリーナの中央。</summary>
        public static Vec2i Center(SiegeConfig config) => new(config.ArenaWidth / 2, config.ArenaHeight / 2);
    }
}
