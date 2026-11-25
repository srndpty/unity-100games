using System;
using System.IO;
using System.Text;
using Game.Core;

namespace Game.App.Tests
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

        public static RaceFlow Flow() => new(new KartConfig(), Track.Parse(DataText("Track.txt"), new KartConfig()));
    }
}
