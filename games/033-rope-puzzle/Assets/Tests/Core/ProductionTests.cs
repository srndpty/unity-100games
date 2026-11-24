using System;
using System.Linq;
using NUnit.Framework;
using TileMotion;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>本番の 10 問：解答の手順で解け、何もしなければ解けない。</summary>
    public sealed class ProductionTests
    {
        /// <summary>全 10 問を解答の手順で解いた最終状態のハッシュを混ぜた値。.NET と Mono で一致する。</summary>
        private const uint SolvedHash = 0xAAC0BF2B;

        [Test]
        public void EverySolutionSolvesItsPuzzleWithParMoves()
        {
            var book = Production();
            var solutions = Solutions();
            Assert.That(solutions.Count, Is.EqualTo(book.Puzzles.Count));
            for (var i = 0; i < book.Puzzles.Count; i++)
            {
                var run = new PuzzleRun(new RopeConfig(), book.Puzzles[i]);
                Play(run, solutions[i]);
                Assert.That(run.Status, Is.EqualTo(RunStatus.Succeeded), $"{i + 1} 問目「{book.Puzzles[i].Name}」");
                Assert.That(run.Moves, Is.EqualTo(book.Puzzles[i].Par), $"{i + 1} 問目の手数");
            }
        }

        [Test]
        public void NoPuzzleSolvesItselfWithoutCutting()
        {
            foreach (var puzzle in Production().Puzzles)
            {
                var run = new PuzzleRun(new RopeConfig(), puzzle);
                Ticks(run, 3600);
                Assert.That(run.Status, Is.EqualTo(RunStatus.Playing), puzzle.Name);
            }
        }

        [Test]
        public void SolvedHashIsFixed()
        {
            var book = Production();
            var solutions = Solutions();
            var hash = StateHash.Start();
            for (var i = 0; i < book.Puzzles.Count; i++)
            {
                var run = new PuzzleRun(new RopeConfig(), book.Puzzles[i]);
                Play(run, solutions[i]);
                hash.Mix(run.Hash());
            }

            TestContext.Out.WriteLine($"0x{hash.Value:X8}");
            Assert.That(hash.Value, Is.EqualTo(SolvedHash), $"0x{hash.Value:X8}");
        }

        [Test]
        public void SolutionsParseRejectsBadText()
        {
            Assert.Throws<FormatException>(() => CutPlan.Parse("10:0 x"));
            Assert.Throws<FormatException>(() => CutPlan.Parse("10:0 5:1"));
            Assert.Throws<FormatException>(() => CutPlan.Parse("-1:0"));
            Assert.That(CutPlan.Parse("# 注釈\n\n0:0 0:1\n").Single().Select(s => s.Rope), Is.EqualTo(Enumerable.Range(0, 2)));
        }
    }
}
