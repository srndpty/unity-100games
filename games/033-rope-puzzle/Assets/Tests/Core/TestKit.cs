using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using IntGeometry;

namespace Game.Core.Tests
{
    /// <summary>テストの共通の部品。</summary>
    internal static class TestKit
    {
        public const int U = Board.DataUnit;

        /// <summary>本番の問題集（Assets/Game/Data/Puzzles.txt）の文。.NET（テストの出力先）と Unity（プロジェクトの直下）の両方から探す。</summary>
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

        public static PuzzleBook Production() => PuzzleBook.Production(DataText("Puzzles.txt"), new RopeConfig());

        /// <summary>1 問だけの問題集の文から問を作る（座標は 1/16 マス）。</summary>
        public static Puzzle One(string body) => PuzzleBook.Parse($"puzzle テスト\npar 1\n{body}\nend\n").Puzzles[0];

        public static PuzzleRun Run(string body) => new(new RopeConfig(), One(body));

        public static Vec2i P(int x, int y) => new(x * U, y * U);

        public static void Ticks(PuzzleRun run, int count)
        {
            for (var i = 0; i < count; i++) { run.Tick(); }
        }

        /// <summary>
        /// ロープ <paramref name="rope"/> の節を垂直に横切る短いなぞりで、そのロープだけを切る（他のロープを横切らない節を探す）。
        /// 切れなければ例外。
        /// </summary>
        public static void CutRope(PuzzleRun run, int rope)
        {
            var swipe = CutPlan.SwipeAcross(run, rope) ?? throw new InvalidOperationException($"ロープ {rope} だけを横切るなぞりが見つかりません。");
            if (run.Cut(swipe.From, swipe.To) != 1) { throw new InvalidOperationException($"ロープ {rope} が切れませんでした。"); }
        }

        /// <summary>ロープ（切っていない）の今の全長を、本来の全長に対する比で返す（1 = 伸びなし）。</summary>
        public static double Stretch(PuzzleRun run, int rope)
        {
            double length = 0;
            for (var k = 0; k < run.LinksOf(rope); k++) { length += Math.Sqrt((run.RopePoint(rope, k + 1) - run.RopePoint(rope, k)).LengthSquared); }
            return length / run.Puzzle.Ropes[rope].Length;
        }

        public static IReadOnlyList<IReadOnlyList<CutStep>> Solutions() => CutPlan.Parse(DataText("Solutions.txt"));

        /// <summary>手順どおりに切りながら、終わるか <paramref name="limit"/> Tick まで進める。</summary>
        public static void Play(PuzzleRun run, IEnumerable<CutStep> plan, int limit = 1800)
        {
            var steps = new List<CutStep>(plan);
            var next = 0;
            while (run.Status == RunStatus.Playing && run.Ticks < limit)
            {
                while (next < steps.Count && steps[next].Tick == run.Ticks) { CutRope(run, steps[next++].Rope); }
                run.Tick();
            }
        }
    }
}
