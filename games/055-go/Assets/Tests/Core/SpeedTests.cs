using System.Collections.Generic;
using System.Diagnostics;
using Game.Core;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>
    /// プレイアウトの速さの記録（README の計測に使う）。空の盤から 20 回ずつ回した 1 回分の時間の p50・p95 と、1 秒あたりの回数を出す。
    /// 合否は極端に遅い場合だけ（機械に依る値なので固定しない）。
    /// </summary>
    public sealed class SpeedTests
    {
        [Test]
        public void PlayoutSpeed_IsRecorded()
        {
            const int Chunk = 20;
            const int Chunks = 100;
            var game = new GoGame(new GoConfig());
            var search = new Mcts(game, new MctsConfig(), Chunk * Chunks, new RandomSeries(1));
            search.Run(Chunk); // JIT の準備
            var times = new List<double>();
            var total = Stopwatch.StartNew();
            for (var i = 1; i < Chunks; i++)
            {
                var sw = Stopwatch.StartNew();
                search.Run(Chunk);
                sw.Stop();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }

            total.Stop();
            times.Sort();
            var perSecond = Chunk * (Chunks - 1) / total.Elapsed.TotalSeconds;
            var p50 = times[times.Count / 2];
            var p95 = times[(times.Count * 95) / 100];
            TestContext.WriteLine($"空の盤からの探索：{perSecond:F0} 回/秒、{Chunk} 回あたり p50 {p50:F2} ms・p95 {p95:F2} ms");
            Assert.That(perSecond, Is.GreaterThan(200));
        }
    }
}
