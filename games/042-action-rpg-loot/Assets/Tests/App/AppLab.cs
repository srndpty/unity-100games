using System.IO;
using System.Runtime.CompilerServices;
using Game.Core;

namespace Game.App.Tests
{
    /// <summary>App のテストの道具。データは作品の Assets/Game/Data から読む。</summary>
    internal static class AppLab
    {
        private static LootData? _loot;
        private static FloorSet? _floors;

        public static string DataText(string name, [CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "Game", "Data", name)));
        }

        public static LootData Loot() => _loot ??= LootData.Parse(DataText("Loot.txt"));

        public static FloorSet Floors() => _floors ??= FloorSet.Parse(DataText("Floors.txt"));

        public static DelveRun NewRun(int seed, DelveConfig? config = null) => new(config ?? new DelveConfig(), Loot(), Floors(), seed);

        public static DelveFlow NewFlow(int firstSeed = 1, DelveConfig? config = null) => new(config ?? new DelveConfig(), Loot(), Floors(), firstSeed);

        /// <summary>操縦役で終わるまで（または上限まで）進める。</summary>
        public static DelveRun Play(int seed, IPilot pilot, int maxTicks, DelveConfig? config = null)
        {
            var run = NewRun(seed, config);
            while (!run.IsOver && run.Tick < maxTicks) { run.Step(pilot.CommandFor(run)); }
            return run;
        }
    }
}
