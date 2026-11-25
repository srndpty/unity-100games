using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using IntGeometry;

namespace Game.Core.Tests
{
    /// <summary>テストの道具。データは作品の Assets/Game/Data から読む。</summary>
    internal static class Lab
    {
        private static LootData? _loot;

        public static string DataText(string name, [CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "Game", "Data", name)));
        }

        public static string ReadmeText([CallerFilePath] string source = "")
        {
            var dir = Path.GetDirectoryName(source) ?? ".";
            return File.ReadAllText(Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "README.md")));
        }

        /// <summary>既定の戦利品の表（読み込みは 1 回だけ）。</summary>
        public static LootData Loot() => _loot ??= LootData.Parse(DataText("Loot.txt"));

        /// <summary>多数のシード・全階・全希少度で品を引く。</summary>
        public static IEnumerable<(Item Item, int Floor)> ManyItems(LootData data, int seeds)
        {
            var gen = new LootGenerator(data);
            for (int seed = 1; seed <= seeds; seed++)
            {
                for (int floor = 1; floor <= LootData.MaxFloor; floor++)
                {
                    var rng = new SeededRandom(seed * 7919 + floor);
                    for (int r = 0; r < RarityInfo.Count; r++)
                    {
                        yield return (gen.Roll(rng, floor, (Rarity)r), floor);
                    }
                }
            }
        }

        /// <summary>最小の表。<paramref name="extra"/> の行を足して負例・特別な表を作る。</summary>
        public static string MinimalText(string extra) =>
            "hero life 100 dmgmin 2 dmgmax 4 speed 100 move 100 armor 0 crit 5 critdmg 150 leech 0 fireres 0 coldres 0\n"
            + "base stick weapon 1 10 棒 dmgmin flat 1 dmgmax flat 2\n"
            + "drop grunt chance 50 count 1 normal 1 magic 1 rare 1 potion 10\n"
            + "drop archer chance 50 count 1 normal 1 magic 1 rare 1 potion 10\n"
            + "drop charger chance 50 count 1 normal 1 magic 1 rare 1 potion 10\n"
            + "drop boss chance 100 count 2 normal 0 magic 0 rare 1 potion 0\n"
            + extra;
    }
}
