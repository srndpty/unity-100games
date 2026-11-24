using System;
using System.IO;
using System.Text;
using Game.Core;

namespace Game.App.Tests
{
    /// <summary>App のテストの共通の部品。</summary>
    internal static class AppKit
    {
        /// <summary>Assets/Game/Data の文。.NET（テストの出力先）と Unity（プロジェクトの直下）の両方から探す。</summary>
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

        public static Table Production() => Table.Parse(DataText("Table.txt"), new PinballConfig());

        public static PinballFlow Flow(int seed = 1) => new(new PinballConfig(), Production(), seed);

        /// <summary>操縦役で 1 試合を終わるまで（上限 <paramref name="limit"/> Tick）進め、試合を返す。</summary>
        public static PinballRun PlayOut(int seed, long limit)
        {
            var pilot = new Autopilot();
            pilot.Reset(seed);
            var run = new PinballRun(new PinballConfig(), Production(), seed);
            while (run.Status == RunStatus.Playing && run.Ticks < limit) { run.Step(pilot.InputFor(run)); }
            return run;
        }
    }
}
