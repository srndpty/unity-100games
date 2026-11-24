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

        public static Course Production() => Course.Parse(DataText("Course.txt"), new GolfConfig());

        public static GolfFlow Flow(int seed = 1) => new(new GolfConfig(), Production(), seed);

        /// <summary>操縦役で 1 ラウンドを終わるまで（上限 <paramref name="limit"/> Tick）進め、ラウンドを返す。</summary>
        public static GolfRound PlayOut(int seed, long limit = 200_000, Course? course = null)
        {
            var pilot = new Autopilot();
            pilot.Reset(seed);
            var round = new GolfRound(new GolfConfig(), course ?? Production(), seed);
            while (round.Phase != RoundPhase.Finished && round.Ticks < limit)
            {
                pilot.Act(round);
                round.Step();
            }

            return round;
        }
    }
}
