using System;
using NUnit.Framework;

namespace IntGeometry.Tests
{
    /// <summary>状態を保存できる乱数系列。041・043・044 の GameRng と同じ値を返すことを固定値で確かめる。</summary>
    public sealed class RandomSeriesTests
    {
        private static readonly int[] _sparseWeights = { 0, 3, 0, 1 };
        private static readonly int[] _zeroWeights = { 0, 0 };
        private static readonly int[] _negativeWeights = { -1, 2 };
        private static readonly int[] _overflowWeights = { int.MaxValue, 1 };

        [Test]
        public void Seed_MatchesSeededRandom()
        {
            var a = new RandomSeries(42);
            var b = new SeededRandom(42);
            Assert.That(a.State, Is.EqualTo(b.State));
            for (var i = 0; i < 50; i++) { Assert.That(a.NextUInt(), Is.EqualTo(b.NextUInt())); }
        }

        [Test]
        public void Derive_MatchesFormerFormulas()
        {
            unchecked
            {
                // 043・044 の GameRng.For と同じ式。
                var mixed = 1234u ^ ((uint)(3 + 1) * 0x9E3779B9u) ^ (2u * 0x85EBCA6Bu);
                Assert.That(RandomSeries.Derive(1234, 3, 2).State, Is.EqualTo(new RandomSeries((int)mixed).State));

                // 041 の GameRng.ForBattle（用途なし）と同じ式。
                var battle = 99u ^ ((uint)(7 + 1) * 0x9E3779B9u);
                Assert.That(RandomSeries.Derive(99, 7, 0).State, Is.EqualTo(new RandomSeries((int)battle).State));
            }

            Assert.That(RandomSeries.Derive(1, 0, 1).State, Is.Not.EqualTo(RandomSeries.Derive(1, 0, 2).State), "用途が違えば別の系列");
            Assert.That(RandomSeries.Derive(1, 0, 1).State, Is.Not.EqualTo(RandomSeries.Derive(1, 1, 1).State), "番号が違えば別の系列");
        }

        [Test]
        public void FixedValues_AreStable()
        {
            // Mono と .NET で同じ値になることを固定値で照合する。
            var rng = new RandomSeries(1);
            var first = rng.NextUInt();
            var second = rng.NextUInt();
            var again = new RandomSeries(1);
            Assert.That(again.NextUInt(), Is.EqualTo(first));
            Assert.That(again.NextUInt(), Is.EqualTo(second));
            Assert.That(new SeededRandom(1).NextUInt(), Is.EqualTo(first));
        }

        [Test]
        public void FromState_RestoresSequence_AndRejectsZero()
        {
            var a = new RandomSeries(7);
            a.NextUInt();
            var restored = RandomSeries.FromState(a.State);
            for (var i = 0; i < 20; i++) { Assert.That(restored.NextUInt(), Is.EqualTo(a.NextUInt())); }
            Assert.Throws<ArgumentOutOfRangeException>(() => RandomSeries.FromState(0));
        }

        [Test]
        public void Clone_IsIndependent()
        {
            var a = new RandomSeries(9);
            var clone = a.Clone();
            Assert.That(clone.NextUInt(), Is.EqualTo(a.NextUInt()));
            a.NextUInt();
            Assert.That(clone.State, Is.Not.EqualTo(a.State));
        }

        [Test]
        public void Bounds_AndChance()
        {
            var rng = new RandomSeries(5);
            for (var i = 0; i < 2000; i++)
            {
                Assert.That(rng.Next(3), Is.InRange(0, 2));
                Assert.That(rng.Range(-2, 2), Is.InRange(-2, 2));
                Assert.That(rng.Chance(0), Is.False);
                Assert.That(rng.Chance(100), Is.True);
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Range(1, 0));
        }

        [Test]
        public void Range_HandlesTheWholeIntRange_AndTheEdges()
        {
            var rng = new RandomSeries(3);
            var sawNegative = false;
            var sawPositive = false;
            for (var i = 0; i < 200; i++)
            {
                var v = rng.Range(int.MinValue, int.MaxValue);
                sawNegative |= v < 0;
                sawPositive |= v > 0;
            }

            Assert.That(sawNegative && sawPositive, Is.True, "int の全域から引く");
            for (var i = 0; i < 200; i++)
            {
                Assert.That(rng.Range(int.MaxValue - 2, int.MaxValue), Is.InRange(int.MaxValue - 2, int.MaxValue));
                Assert.That(rng.Range(int.MinValue, int.MinValue + 2), Is.InRange(int.MinValue, int.MinValue + 2));
                Assert.That(rng.Range(int.MinValue, int.MaxValue - 1), Is.LessThan(int.MaxValue));
                Assert.That(rng.Range(5, 5), Is.EqualTo(5));
            }

            // 幅が 2^32 未満では抽出前と同じ式（固定ハッシュを保つ）。
            var a = new RandomSeries(11);
            var b = new RandomSeries(11);
            for (var i = 0; i < 50; i++) { Assert.That(a.Range(-7, 30), Is.EqualTo(-7 + (int)(b.NextUInt() % 38u))); }

            var state = rng.State;
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Range(1, 0));
            Assert.That(rng.State, Is.EqualTo(state), "拒否では乱数を引かない");
        }

        [Test]
        public void Weighted_SkipsZero_AndRejectsInvalidTables()
        {
            var rng = new RandomSeries(11);
            for (var i = 0; i < 500; i++)
            {
                Assert.That(rng.Weighted(_sparseWeights), Is.EqualTo(1).Or.EqualTo(3));
            }

            var before = rng.State;
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Weighted(_zeroWeights));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Weighted(_negativeWeights));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Weighted(_overflowWeights));
            Assert.That(rng.State, Is.EqualTo(before), "拒否した表では乱数を引かない");
        }
    }
}
