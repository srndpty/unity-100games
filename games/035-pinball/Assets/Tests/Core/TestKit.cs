using System;
using System.IO;
using System.Text;
using IntGeometry;

namespace Game.Core.Tests
{
    /// <summary>テストの共通の部品。</summary>
    internal static class TestKit
    {
        public const int U = Board.Unit;

        public static readonly PinballInput None = new(false, false, false);

        public static readonly PinballInput Plunger = new(false, false, true);

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

        public static PinballRun Run(int seed = 1) => new(new PinballConfig(), Production(), seed);

        /// <summary>
        /// 検証用の小さな台：本番の部品（フリッパー・レーン・ドレイン・バンパー 3・ターゲット 3）を台の隅に寄せ、
        /// <paramref name="extra"/> の行を足す。球を真ん中の空いた場所で動かして、1 つの相手との当たりを確かめる。
        /// </summary>
        public static Table Lab(string extra = "") => Table.Parse(LabText(extra), new PinballConfig());

        public static string LabText(string extra) =>
            "bumper 2 41 1\nbumper 5 41 1\nbumper 8 41 1\n" +
            "target 12 42 13 42 0.2\ntarget 14 42 15 42 0.2\ntarget 16 42 17 42 0.2\n" +
            "flipper L 3 3 3 0.3 -30 30\nflipper R 18 3 3 0.3 210 150\n" +
            "lane 21 23.875 2.5\ndrain 0.5\n" + extra;

        public static PinballRun LabRun(string extra = "", PinballConfig? config = null) => new(config ?? new PinballConfig(), Table.Parse(LabText(extra), config ?? new PinballConfig()), 1);

        /// <summary>レーンの球を外し、置き場 0 に球を置く（重力は残る）。</summary>
        public static void Drop(PinballRun run, Vec2i position, Vec2i velocity)
        {
            for (var slot = 0; slot < run.BallSlots; slot++) { run.Remove(slot); }
            run.Place(0, position, velocity);
        }

        public static Vec2i Cell(double x, double y) => new((int)Math.Round(x * U), (int)Math.Round(y * U));

        public static void Ticks(PinballRun run, int count, PinballInput input = default)
        {
            for (var i = 0; i < count; i++) { run.Step(input); }
        }

        /// <summary>レーンに球があり、引きが <paramref name="pull"/> Tick に届くまでは押す（届いたら離して打ち出す）。</summary>
        public static bool HoldPlunger(PinballRun run, int pull)
        {
            for (var slot = 0; slot < run.BallSlots; slot++)
            {
                if (run.BallAt(slot).State == BallState.Lane) { return run.Pull < pull; }
            }

            return false;
        }

        /// <summary>プランジャーを <paramref name="pull"/> Tick 引いて離す。</summary>
        public static void Plunge(PinballRun run, int pull)
        {
            Ticks(run, pull, Plunger);
            run.Step(None);
        }
    }
}
