using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>
    /// 題材固有テスト「未使用チャンクを回収」「同条件シードで出現順一致」。チャンクの列だけを直接進めて確かめる
    /// （自機の操作に依らないことは <see cref="RunnerRunTests"/> で試合を通して確かめる）。
    /// </summary>
    public sealed class ChunkTrackTests
    {
        private const long M = TestKit.M;

        [Test]
        public void LongTravel_RecyclesChunks_AndStaysWithinCapacity()
        {
            var rules = TestKit.Rules;
            var config = rules.Config;
            var track = new ChunkTrack(rules, 1);
            var bound = RunRules.ActiveChunkBound(config, rules.ShortestChunk);
            long z = 0;
            var maxCount = 0;

            // 最も速い段階で 100 万 Tick（約 4.6 時間）ぶん進める。
            for (var tick = 0; tick < 1_000_000; tick++)
            {
                z += track.SegmentAt(z).Speed;
                track.Update(z);
                maxCount = System.Math.Max(maxCount, track.Count);
                if (tick % 997 != 0) { continue; }
                Assert.That(track.Count, Is.LessThanOrEqualTo(bound).And.LessThanOrEqualTo(track.Capacity));
                Assert.That(track.Spawned - track.Recycled, Is.EqualTo(track.Count));
                var first = track.StartAt(0) + track.ChunkAt(0).Length(config);
                Assert.That(first, Is.GreaterThan(z - (config.BehindMeters * M)), "後方を過ぎたチャンクは残っていない");
                Assert.That(track.StartAt(track.Count - 1), Is.LessThan(z + (config.LookAheadMeters * M)));
                Assert.That(track.NextStart, Is.GreaterThanOrEqualTo(z + (config.LookAheadMeters * M)), "前方は並べ終わっている");
            }

            Assert.That(track.Recycled, Is.GreaterThan(10_000), "回収が繰り返し起きた");
            Assert.That(maxCount, Is.LessThanOrEqualTo(bound));
        }

        [Test]
        public void ChunksAreContiguous_AndUseTheSpeedOfTheirEntrance()
        {
            var rules = TestKit.Rules;
            var track = new ChunkTrack(rules, 3);
            for (long z = 0; z < 3000 * M; z += 80) { track.Update(z); }
            for (var i = 0; i < track.Count; i++)
            {
                Assert.That(track.SpeedAt(i), Is.EqualTo(Difficulty.Speed(rules.Config, track.StartAt(i))));
                Assert.That(track.ChunkAt(i).Tier, Is.LessThanOrEqualTo(Difficulty.Tier(rules.Config, track.StartAt(i))));
                if (i > 0) { Assert.That(track.StartAt(i), Is.EqualTo(track.StartAt(i - 1) + track.ChunkAt(i - 1).Length(rules.Config))); }
            }
        }

        [Test]
        public void SameSeed_GivesTheSameOrder_RegardlessOfHowItIsTravelled()
        {
            var rules = TestKit.Rules;
            var smooth = Sequence(rules, 5, z => z + 40, 400);
            var jerky = Sequence(rules, 5, z => z + (7 * M) + 13, 400);
            Assert.That(jerky, Is.EqualTo(smooth));
        }

        [Test]
        public void DifferentSeeds_GiveDifferentOrders()
        {
            var rules = TestKit.Rules;
            var orders = Enumerable.Range(1, 6).Select(seed => string.Join(",", Sequence(rules, seed, z => z + 80, 60))).ToList();
            Assert.That(orders.Distinct().Count(), Is.EqualTo(orders.Count));
        }

        [Test]
        public void SameChunk_DoesNotAppearTwiceInARow_AndEveryChunkAppears()
        {
            var rules = TestKit.Rules;
            var order = Sequence(rules, 9, z => z + 80, 3000);
            for (var i = 1; i < order.Count; i++) { Assert.That(order[i], Is.Not.EqualTo(order[i - 1])); }
            Assert.That(order.Distinct().Count(), Is.EqualTo(rules.Chunks.Count));
        }

        [Test]
        public void OnlyOneEligibleChunk_RepeatsItself()
        {
            var order = Sequence(TestKit.Only("...", "H..", "...", "...", "...", "..."), 1, z => z + 80, 20);
            Assert.That(order.Distinct().Single(), Is.Zero);
        }

        [Test]
        public void CapacityBelowTheBound_IsRejected()
        {
            var config = new RunConfig { MaxActiveChunks = 10 };
            Assert.That(() => RunRules.Build(config, ChunkLibrary.Default()), Throws.ArgumentException.With.Message.Contains("上限"));
        }

        /// <summary>シード <paramref name="seed"/> の列を、<paramref name="advance"/> で進めながら最初の <paramref name="count"/> 個まで記録する。</summary>
        private static List<int> Sequence(RunRules rules, int seed, System.Func<long, long> advance, int count)
        {
            var track = new ChunkTrack(rules, seed);
            var order = new List<int>();
            long z = 0;
            track.Update(z);
            while (order.Count < count)
            {
                for (var i = 0; i < track.Count; i++)
                {
                    if (track.SerialAt(i) == order.Count) { order.Add(track.IdAt(i)); }
                }

                z = advance(z);
                track.Update(z);
            }

            return order.Take(count).ToList();
        }
    }
}
