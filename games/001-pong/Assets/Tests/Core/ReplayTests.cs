using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>シードと入力列が同じなら、試合結果が完全に一致することを確認する。</summary>
    public sealed class ReplayTests
    {
        [Test]
        public void SameSeedAndInputs_ProduceSameMatch()
        {
            var inputs = RecordInputs(seed: 7, count: TestCourt.MaxTicksPerMatch);

            var first = Replay(matchSeed: 42, cpuSeed: 43, inputs);
            var second = Replay(matchSeed: 42, cpuSeed: 43, inputs);

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void CpuAgainstCpu_ReachesWinner()
        {
            var match = new MatchState(new PongConfig(), 99);
            var left = new CpuController(new CpuConfig(), Side.Left, 1);
            var right = new CpuController(new CpuConfig(), Side.Right, 2);

            TestCourt.PlayToEnd(match, left.Decide, right.Decide);

            Assert.That(match.Winner, Is.Not.Null);
        }

        private static (Side? Winner, int Left, int Right, long Ticks, Vec2 Ball) Replay(int matchSeed, int cpuSeed, List<float> inputs)
        {
            var match = new MatchState(new PongConfig(), matchSeed);
            var cpu = new CpuController(new CpuConfig(), Side.Right, cpuSeed);
            var index = 0;
            TestCourt.PlayToEnd(match, _ => inputs[index++], cpu.Decide);
            return (match.Winner, match.LeftScore, match.RightScore, match.Ticks, match.Ball.Position);
        }

        // 人間の操作を模した入力列（同じ向きにしばらく押し続ける）。
        private static List<float> RecordInputs(int seed, int count)
        {
            var random = new SeededRandom(seed);
            var inputs = new List<float>(count);
            var current = 0f;
            for (var i = 0; i < count; i++)
            {
                if (i % 20 == 0)
                {
                    current = random.Range(-1f, 1f);
                }

                inputs.Add(current);
            }

            return inputs;
        }
    }
}
