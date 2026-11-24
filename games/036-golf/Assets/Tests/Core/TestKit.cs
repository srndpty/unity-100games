using System;
using System.IO;
using System.Text;
using IntGeometry;

namespace Game.Core.Tests
{
    /// <summary>テストの共通の部品。</summary>
    internal static class TestKit
    {
        public const int M = Units.PerMeter;

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

        public static GolfRound Round(int seed = 1) => new(new GolfConfig(), Production(), seed);

        /// <summary>
        /// 検証用の小さなホール：奥行き 200 m × 横幅 60 m のフェアウェイ、左にティー、奥にグリーンとカップ。
        /// <paramref name="extra"/> の行（塗り・木・傾き）を足す。
        /// </summary>
        public static Hole Lab(string extra = "", GolfConfig? config = null) =>
            Course.ParseHoles(LabText(extra), config ?? new GolfConfig())[0];

        public static string LabText(string extra) =>
            "hole 1 par 4 size 200 60\n" +
            "paint fairway rect 0 0 200 60\n" +
            "paint tee rect 2 26 10 34\n" +
            "paint green ellipse 170 30 15 12\n" +
            "tee 5 30\n" +
            "cup 170 30\n" + extra;

        /// <summary>ちょうど 3 ホールの検証用コース（どれも <see cref="LabText"/> と同じ形）。</summary>
        public static Course LabCourse(string extra = "") =>
            Course.Parse(LabText(extra) + "\n" + LabText(extra).Replace("hole 1", "hole 2") + "\n" + LabText(extra).Replace("hole 1", "hole 3"), new GolfConfig());

        public static Vec2i At(double x, double y) => new((int)Math.Round(x * M), (int)Math.Round(y * M));

        /// <summary>止まる・入る・池/OB まで進める。</summary>
        public static BallState Run(BallState ball, Hole hole, Vec2i wind = default, GolfConfig? config = null) =>
            Predictor.Finish(ball, hole, wind, config ?? new GolfConfig());

        /// <summary>転がっている球。</summary>
        public static BallState Rolling(Vec2i position, Vec2i velocity) =>
            new() { Phase = BallPhase.Rolling, Position = position, Velocity = velocity, FirstBite = Units.Ratio };

        /// <summary>飛んでいる球。</summary>
        public static BallState Flying(Vec2i position, int z, Vec2i velocity, int velocityZ) =>
            new() { Phase = BallPhase.Flying, Position = position, Z = z, Velocity = velocity, VelocityZ = velocityZ, FirstBite = Units.Ratio };

        public static double Meters(long micro) => micro / (double)M;
    }
}
