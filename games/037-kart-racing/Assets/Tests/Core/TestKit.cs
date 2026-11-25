using System;
using System.IO;
using System.Text;

namespace Game.Core.Tests
{
    internal static class TestKit
    {
        public static string DataText(string name)
        {
            foreach (var start in new[] { NUnit.Framework.TestContext.CurrentContext.TestDirectory, Environment.CurrentDirectory })
            {
                for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
                {
                    var candidate = Path.Combine(directory.FullName, "Assets", "Game", "Data", name);
                    if (File.Exists(candidate)) { return File.ReadAllText(candidate, Encoding.UTF8); }
                }
            }

            throw new FileNotFoundException($"Assets/Game/Data/{name} が見つかりません。");
        }

        public static Track Circuit() => Track.Parse(DataText("Track.txt"), new KartConfig());

        public static Race CircuitRace() => new(new KartConfig(), Circuit());

        /// <summary>角で壁に当たらずにドリフトを試せる、幅の広い矩形。スタートは下の直線の中ほど（グリッドが直線に収まる）。</summary>
        public static Track Wide()
        {
            var text = new StringBuilder();
            void Station(int x, int y) => text.Append("station ").Append(x).Append(' ').Append(y).Append(" 4000\n");
            for (var x = 28000; x <= 60000; x += 4000) { Station(x, 0); }
            for (var y = 4000; y <= 40000; y += 4000) { Station(60000, y); }
            for (var x = 56000; x >= 0; x -= 4000) { Station(x, 40000); }
            for (var y = 36000; y >= 4000; y -= 4000) { Station(0, y); }
            for (var x = 0; x <= 24000; x += 4000) { Station(x, 0); }
            text.Append("runoff 0\ncheckpoint 0\ncheckpoint 15\ncheckpoint 25\ncheckpoint 40\n");
            return Track.Parse(text.ToString(), new KartConfig());
        }
    }
}
