using System.Collections.Generic;
using TileMotion;

namespace Game.Core.Tests
{
    /// <summary>テスト用の設定・チャンク・操作の補助。</summary>
    internal static class TestKit
    {
        public const int M = Units.Tile;

        private static RunRules? _rules;

        /// <summary>既定の設定とチャンク（検査が重いので 1 つを使い回す）。</summary>
        public static RunRules Rules => _rules ??= RunRules.Build(new RunConfig(), ChunkLibrary.Default());

        public static ChunkSpec Spec(string name, int tier, params string[] rows) => new() { Name = name, Tier = tier, Weight = 1, Rows = rows };

        public static ChunkDef Chunk(params string[] rows) => ChunkDef.Parse(0, Spec("test", 1, rows), 1);

        private static readonly Dictionary<string, RunRules> _only = new();

        /// <summary>チャンク 1 種類だけの組（同じ表は使い回す）。</summary>
        public static RunRules Only(params string[] rows)
        {
            var key = string.Join("/", rows);
            if (!_only.TryGetValue(key, out var rules))
            {
                rules = RunRules.Build(new RunConfig(), new[] { Spec("only", 1, rows) });
                _only.Add(key, rules);
            }

            return rules;
        }

        public static Command Do(Steer steer = Steer.None, Leap leap = Leap.None) => new(steer, leap);

        /// <summary><paramref name="ticks"/> Tick の間、何もしない。</summary>
        public static RunEvents Wait(RunnerRun run, int ticks)
        {
            var events = RunEvents.None;
            for (var i = 0; i < ticks; i++)
            {
                run.Step(Command.None);
                events |= run.TakeEvents();
            }

            return events;
        }
    }
}
