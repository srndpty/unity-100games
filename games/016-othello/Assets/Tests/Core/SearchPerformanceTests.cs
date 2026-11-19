using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using static Game.Core.Tests.OthelloTestKit;

namespace Game.Core.Tests
{
    /// <summary>
    /// 最大深さの探索が、メインスレッドで同期に呼んでも許される規模に収まるか。
    /// 局面数（ノード数）は環境に依存しないので上限を検査し、時間は記録だけする（README に転記する）。
    /// </summary>
    public sealed class SearchPerformanceTests
    {
        /// <summary>1手あたりのノード数の上限。超えたら同期探索の前提を見直す。</summary>
        private const long MaxNodesPerSearch = 200_000;

        [Test]
        public void MaxDepthSearchStaysWithinTheNodeBudget()
        {
            var positions = SamplePositions(20, 2000).Where((p, i) => i % 3 == 0 && p.LegalMoves() != 0).ToList();
            var nodes = new long[positions.Count];
            var millis = new double[positions.Count];
            for (var i = 0; i < positions.Count; i++)
            {
                var watch = Stopwatch.StartNew();
                nodes[i] = Searcher.Search(positions[i], OthelloConfig.MaxDepth).Nodes;
                watch.Stop();
                millis[i] = watch.Elapsed.TotalMilliseconds;
            }

            TestContext.WriteLine($"深さ{OthelloConfig.MaxDepth}・{positions.Count}局面");
            TestContext.WriteLine($"ノード数 p50 {Percentile(nodes.Select(n => (double)n).ToArray(), 0.5):F0} / p95 {Percentile(nodes.Select(n => (double)n).ToArray(), 0.95):F0} / 最大 {nodes.Max()}");
            TestContext.WriteLine($"時間(ms) p50 {Percentile(millis, 0.5):F2} / p95 {Percentile(millis, 0.95):F2} / 最大 {millis.Max():F2}");
            Assert.That(positions.Count, Is.GreaterThan(300));
            Assert.That(nodes.Max(), Is.LessThan(MaxNodesPerSearch));
        }

        private static double Percentile(double[] values, double rank)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            return sorted[(int)System.Math.Ceiling(rank * sorted.Length) - 1];
        }
    }
}
