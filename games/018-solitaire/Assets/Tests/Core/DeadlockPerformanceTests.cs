using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using static Game.Core.Tests.SolitaireTestKit;

namespace Game.Core.Tests
{
    /// <summary>
    /// 手詰まりの判定は 1 手ごとにメインスレッドで同期に呼ぶ。1 回の手間（調べた局面数）が設定の上限に収まることを検査し、
    /// 時間は記録だけする（README に転記する）。局面は乱数で遊んだ全局面で、人の手より組札との行き来が多く重い側に寄る。
    /// </summary>
    public sealed class DeadlockPerformanceTests
    {
        [TestCase(1)]
        [TestCase(3)]
        public void EveryCheckStaysWithinTheStateBudget(int drawCount)
        {
            var config = Config(drawCount);
            var states = new List<double>();
            var millis = new List<double>();
            var verdicts = new Dictionary<DeadlockVerdict, int>();
            for (var seed = 1; seed <= 20; seed++)
            {
                var game = new SolitaireGame(config, seed);
                PlayRandomly(game, seed * 31, 400, onStep: g =>
                {
                    var watch = Stopwatch.StartNew();
                    var verdict = DeadlockDetector.Check(g.Table, config.DeadlockSearchStates, out var visited);
                    watch.Stop();
                    states.Add(visited);
                    millis.Add(watch.Elapsed.TotalMilliseconds);
                    verdicts[verdict] = verdicts.TryGetValue(verdict, out var count) ? count + 1 : 1;
                });
            }

            TestContext.WriteLine($"{drawCount} 枚めくり・{states.Count} 局面・上限 {config.DeadlockSearchStates}・" + string.Join(" ", verdicts.Select(v => $"{v.Key} {v.Value}")));
            TestContext.WriteLine($"調べた局面数 p50 {Percentile(states, .5):F0} / p95 {Percentile(states, .95):F0} / 最大 {states.Max():F0}");
            TestContext.WriteLine($"時間(ms) p50 {Percentile(millis, .5):F3} / p95 {Percentile(millis, .95):F3} / p99 {Percentile(millis, .99):F2} / 最大 {millis.Max():F2}");
            Assert.That(states.Count, Is.GreaterThan(3000));
            Assert.That(states.Max(), Is.LessThanOrEqualTo(config.DeadlockSearchStates + 1));
        }

        private static double Percentile(List<double> values, double rank)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            return sorted[(int)System.Math.Ceiling(rank * sorted.Length) - 1];
        }
    }
}
