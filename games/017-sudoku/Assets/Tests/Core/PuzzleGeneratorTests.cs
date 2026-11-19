using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using static Game.Core.Tests.SudokuTestKit;

namespace Game.Core.Tests
{
    public sealed class PuzzleGeneratorTests
    {
        private const int SeedCount = 100;

        private static readonly Difficulty[] _all = { Difficulty.Easy, Difficulty.Normal, Difficulty.Hard };

        [TestCase(Difficulty.Easy)]
        [TestCase(Difficulty.Normal)]
        [TestCase(Difficulty.Hard)]
        public void GeneratedPuzzlesHaveExactlyOneSolutionWhichIsTheStoredSolution(Difficulty difficulty)
        {
            var config = new SudokuConfig();
            var target = config.GivensOf(difficulty);
            var counts = new List<int>();
            for (var seed = 0; seed < SeedCount; seed++)
            {
                var puzzle = PuzzleGenerator.Generate(seed, config, difficulty);
                var givens = puzzle.Givens;
                Assert.That(IsValidSolution(puzzle.Solution), Is.True);
                var result = Solver.Count(givens, 2);
                Assert.That(result.Solutions, Is.EqualTo(1), $"seed {seed}");
                Assert.That(result.First, Is.EqualTo(puzzle.Solution), $"seed {seed}");
                Assert.That(givens.FilledCount, Is.GreaterThanOrEqualTo(target), $"seed {seed}");
                counts.Add(givens.FilledCount);
            }

            TestContext.WriteLine($"{difficulty} 与えの数 最小 {counts.Min()} / 平均 {counts.Average():F2} / 最大 {counts.Max()}（目標 {target}）");

            // 伝播だけで解ける条件の付く難しさでは、目標の近くまで抜けることを確かめる。むずかしいは目標が下限に近く、届かないことがある。
            if (SudokuConfig.IsLogicOnly(difficulty)) { Assert.That(counts.Count(c => c <= target + 1), Is.GreaterThan(SeedCount * 9 / 10)); }
        }

        [TestCase(Difficulty.Easy)]
        [TestCase(Difficulty.Normal)]
        public void EasyAndNormalPuzzlesAreSolvedByPropagationAlone(Difficulty difficulty)
        {
            for (var seed = 0; seed < SeedCount; seed++)
            {
                var puzzle = PuzzleGenerator.Generate(seed, new SudokuConfig(), difficulty);
                var result = Propagator.Run(puzzle.Givens);
                Assert.That(result.Status, Is.EqualTo(PropagationStatus.Solved), $"seed {seed}");
                Assert.That(result.Grid, Is.EqualTo(puzzle.Solution));
                Assert.That(Solver.Count(puzzle.Givens, 2).Branches, Is.EqualTo(0), $"seed {seed}");
            }
        }

        [Test]
        public void SomeHardPuzzlesNeedMoreThanPropagation()
        {
            var stuck = 0;
            for (var seed = 0; seed < SeedCount; seed++)
            {
                var puzzle = PuzzleGenerator.Generate(seed, new SudokuConfig(), Difficulty.Hard);
                if (Propagator.Run(puzzle.Givens).Status == PropagationStatus.Stuck) { stuck++; }
            }

            TestContext.WriteLine($"Hard で伝播だけでは解けない問題 {stuck}/{SeedCount}");
            Assert.That(stuck, Is.GreaterThan(SeedCount / 10));
        }

        [Test]
        public void DifficultyChangesOnlyTheRemovalSoTheSolutionIsShared()
        {
            for (var seed = 0; seed < 10; seed++)
            {
                var solutions = _all.Select(d => PuzzleGenerator.Generate(seed, new SudokuConfig(), d).Solution).ToList();
                Assert.That(solutions[1], Is.EqualTo(solutions[0]));
                Assert.That(solutions[2], Is.EqualTo(solutions[0]));
            }
        }

        [Test]
        public void GivensAreSymmetricAboutTheCentre()
        {
            foreach (var difficulty in _all)
            {
                for (var seed = 0; seed < 10; seed++)
                {
                    var givens = PuzzleGenerator.Generate(seed, new SudokuConfig(), difficulty).Givens;
                    for (var cell = 0; cell < Board.CellCount; cell++)
                    {
                        Assert.That(givens[cell] == 0, Is.EqualTo(givens[Board.CellCount - 1 - cell] == 0), $"{difficulty} seed {seed} cell {cell}");
                    }
                }
            }
        }

        [TestCase(Difficulty.Normal, SudokuConfig.MinTargetGivens)]
        [TestCase(Difficulty.Normal, 40)]
        [TestCase(Difficulty.Normal, SudokuConfig.MaxTargetGivens)]
        [TestCase(Difficulty.Hard, SudokuConfig.MinTargetGivens)]
        [TestCase(Difficulty.Hard, 40)]
        [TestCase(Difficulty.Hard, SudokuConfig.MaxTargetGivens)]
        public void TargetGivensIsRespected(Difficulty difficulty, int target)
        {
            var config = new SudokuConfig { NormalGivens = target, HardGivens = target };
            for (var seed = 0; seed < 5; seed++)
            {
                var puzzle = PuzzleGenerator.Generate(seed, config, difficulty);
                Assert.That(puzzle.Givens.FilledCount, Is.GreaterThanOrEqualTo(target));
                Assert.That(Solver.Count(puzzle.Givens, 2).Solutions, Is.EqualTo(1));
                if (target >= 40) { Assert.That(puzzle.Givens.FilledCount, Is.LessThanOrEqualTo(target + 1)); }
            }
        }

        [Test]
        public void SameSeedGivesTheSamePuzzleAndDifferentSeedsDiffer()
        {
            var config = new SudokuConfig();
            var seen = new HashSet<Grid>();
            for (var seed = 0; seed < 50; seed++)
            {
                var puzzle = PuzzleGenerator.Generate(seed, config, Difficulty.Normal);
                Assert.That(PuzzleGenerator.Generate(seed, config, Difficulty.Normal).Givens, Is.EqualTo(puzzle.Givens));
                seen.Add(puzzle.Givens);
            }

            Assert.That(seen, Has.Count.EqualTo(50));
        }

        /// <summary>整数だけで生成するので、Mono と .NET で同じ問題になる。</summary>
        [TestCase(Difficulty.Easy, 1525007951u)]
        [TestCase(Difficulty.Normal, 4215151686u)]
        [TestCase(Difficulty.Hard, 1469544732u)]
        public void SeedOneGivesAFixedPuzzleOnEveryRuntime(Difficulty difficulty, uint givensHash)
        {
            var puzzle = PuzzleGenerator.Generate(1, new SudokuConfig(), difficulty);
            TestContext.WriteLine($"{difficulty} {puzzle.Givens} {puzzle.Givens.StableHash()}");
            Assert.That(puzzle.Givens.StableHash(), Is.EqualTo(givensHash));
            Assert.That(puzzle.Solution.StableHash(), Is.EqualTo(2471139612u));
        }

        [TestCase(SudokuConfig.MinTargetGivens - 1)]
        [TestCase(SudokuConfig.MaxTargetGivens + 1)]
        [TestCase(-1)]
        public void TargetOutOfRangeIsRejected(int target)
        {
            foreach (var config in new[]
            {
                new SudokuConfig { EasyGivens = target },
                new SudokuConfig { NormalGivens = target },
                new SudokuConfig { HardGivens = target },
            })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => PuzzleGenerator.Generate(1, config, Difficulty.Normal));
            }
        }

        [Test]
        public void UndefinedDifficultyIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PuzzleGenerator.Generate(1, new SudokuConfig(), (Difficulty)3));
            Assert.Throws<ArgumentOutOfRangeException>(() => SudokuConfig.IsLogicOnly((Difficulty)(-1)));
        }

        [Test]
        public void PuzzleRejectsGivensThatDisagreeWithTheSolution()
        {
            Assert.Throws<ArgumentException>(() => _ = new Puzzle(Easy.With(2, 1), EasySolution));
            Assert.Throws<ArgumentException>(() => _ = new Puzzle(Easy, Easy));
        }

        /// <summary>
        /// 生成はメインスレッドで同期に呼ぶ。時間を記録し（README に転記する）、極端に遅くなっていないかだけを検査する。
        /// </summary>
        [TestCase(Difficulty.Normal)]
        [TestCase(Difficulty.Hard)]
        public void GenerationIsFastEnoughToRunOnTheMainThread(Difficulty difficulty)
        {
            var config = new SudokuConfig { NormalGivens = SudokuConfig.MinTargetGivens, HardGivens = SudokuConfig.MinTargetGivens };
            var millis = new double[SeedCount];
            for (var seed = 0; seed < SeedCount; seed++)
            {
                var watch = Stopwatch.StartNew();
                PuzzleGenerator.Generate(seed, config, difficulty);
                watch.Stop();
                millis[seed] = watch.Elapsed.TotalMilliseconds;
            }

            var sorted = millis.OrderBy(m => m).ToArray();
            TestContext.WriteLine($"{difficulty} 目標 {SudokuConfig.MinTargetGivens}・{SeedCount}問 時間(ms) p50 {sorted[SeedCount / 2]:F2} / p95 {sorted[SeedCount * 95 / 100]:F2} / 最大 {sorted[SeedCount - 1]:F2}");
            Assert.That(sorted[SeedCount - 1], Is.LessThan(1000));
        }
    }
}
