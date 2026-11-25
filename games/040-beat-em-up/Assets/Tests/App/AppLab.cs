using System.IO;
using System.Runtime.CompilerServices;
using Game.Core;

namespace Game.App.Tests
{
    internal static class AppLab
    {
        public static string DataText(string name, [CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "Game", "Data", name)));
        }

        public static MoveTable Table() => MoveTable.Parse(DataText("Moves.txt"));

        public static StageData Stage() => StageData.Parse(DataText("Stage.txt"));

        public static BrawlFlow Flow(int seed = 1, BrawlConfig? config = null) => new(Table(), Stage(), config ?? new BrawlConfig(), seed);

        /// <summary>操縦役で 1 面を通す。終わるか上限で止まる。</summary>
        public static Brawl Play(int seed, Autopilot pilot, int limit, BrawlConfig? config = null)
        {
            var brawl = new Brawl(Table(), config ?? new BrawlConfig(), Stage(), seed);
            pilot.Reset(seed);
            for (int t = 0; t < limit && brawl.Outcome == Outcome.Running; t++) { brawl.Step(pilot.PadFor(brawl)); }
            return brawl;
        }
    }
}
