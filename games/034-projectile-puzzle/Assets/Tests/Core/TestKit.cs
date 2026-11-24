using System;
using System.IO;
using System.Text;

namespace Game.Core.Tests
{
    /// <summary>テストの共通の部品。</summary>
    internal static class TestKit
    {
        /// <summary>本番のデータ（Assets/Game/Data/<paramref name="name"/>）の文。.NET（テストの出力先）と Unity（プロジェクトの直下）の両方から探す。</summary>
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

        public static StageBook Production() => StageBook.Production(DataText("Stages.txt"), new SiegeConfig());

        /// <summary>
        /// 小さな面：部材 0 = 木の柱、1 = 灯籠（柱の上）、2 = 灯籠（地面）、3 = 石、接合 0 = 柱と灯籠 1。弾は <paramref name="shots"/>。
        /// </summary>
        public static Stage Small(string shots = "normal normal") => One(
            $"launcher 4 2.5\nshots {shots}\n" +
            "block wood 20 1 0.4 2\n" +
            "block lantern 20 2.3 0.6 0.6\n" +
            "block lantern 24 0.3 0.6 0.6\n" +
            "block stone 27 0.5 1 1\n" +
            "joint 0 1 500\n");

        /// <summary>1 面だけの文から面を作る。</summary>
        public static Stage One(string body) => StageBook.Parse($"stage テスト\nhint 案内\n{body}\nend\n").Stages[0];

        public static SiegeRun Run(string shots = "normal normal") => new(new SiegeConfig(), Small(shots));

        /// <summary>引き：撃てる長さで、右上へ飛ぶ向き。</summary>
        public static readonly V2 Pull = new(-1.5f, -1f);

        /// <summary>何も起きない物理の 1 Tick（速さは <paramref name="speed"/>）。</summary>
        public static TickReport Quiet(float speed = 0f) => new() { MaxSpeed = speed };

        /// <summary>命令を受けて、報告 <paramref name="report"/> で 1 Tick 進める。</summary>
        public static TickCommands Step(SiegeRun run, TickReport? report = null)
        {
            var commands = run.BeginTick();
            run.EndTick(report ?? Quiet());
            return commands;
        }

        /// <summary><paramref name="count"/> Tick を同じ報告で進める。</summary>
        public static void Steps(SiegeRun run, int count, float speed = 0f)
        {
            for (var i = 0; i < count; i++) { Step(run, Quiet(speed)); }
        }

        /// <summary>撃って、動いている 1 Tick を進める（飛行中にする）。</summary>
        public static void Shoot(SiegeRun run)
        {
            if (!run.Launch(Pull)) { throw new InvalidOperationException("撃てませんでした。"); }
            Step(run, Quiet(10f));
        }

        public static TickReport Hit(int a, int b, float impulse)
        {
            var report = Quiet(5f);
            report.Impacts.Add(new Impact(a, b, impulse));
            return report;
        }

        public static TickReport Fell(params int[] pieces)
        {
            var report = Quiet(5f);
            report.FellOut.AddRange(pieces);
            return report;
        }
    }
}
