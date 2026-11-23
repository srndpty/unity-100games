using System;
using NUnit.Framework;

namespace IntGeometry.Tests
{
    /// <summary>シード付きの乱数。同じシードなら同じ系列で、値は Mono と .NET で一致する。</summary>
    public sealed class SeededRandomTests
    {
        [Test]
        public void SameSeed_GivesSameSequence_AndCloneIsIndependent()
        {
            var a = new SeededRandom(42);
            var b = new SeededRandom(42);
            for (var i = 0; i < 100; i++) { Assert.That(a.NextUInt(), Is.EqualTo(b.NextUInt())); }

            var clone = a.Clone();
            var expected = a.NextUInt();
            Assert.That(clone.NextUInt(), Is.EqualTo(expected));
            a.NextUInt();
            Assert.That(clone.State, Is.Not.EqualTo(a.State), "複製は元の乱数と状態を共有しない");
        }

        [Test]
        public void NearSeeds_StartDifferently()
        {
            Assert.That(new SeededRandom(1).NextUInt(), Is.Not.EqualTo(new SeededRandom(2).NextUInt()));
            Assert.That(new SeededRandom(0).State, Is.Not.Zero, "状態 0 では xorshift が止まる");
        }

        [Test]
        public void Range_AndNext_StayInBounds()
        {
            var random = new SeededRandom(5);
            var seenMin = false;
            var seenMax = false;
            for (var i = 0; i < 5000; i++)
            {
                var value = random.Range(-3, 3);
                Assert.That(value, Is.InRange(-3, 3));
                seenMin |= value == -3;
                seenMax |= value == 3;
                Assert.That(random.Next(7), Is.InRange(0, 6));
            }

            Assert.That(seenMin && seenMax, Is.True, "両端も出る");
            Assert.Throws<ArgumentOutOfRangeException>(() => random.Range(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(0));
        }

        [Test]
        public void FixedSequence_IsTheSameOnEveryRuntime()
        {
            // 025〜027 と同じ系列（抽出で変わっていない）。.NET と Mono の両方で照合する。
            var random = new SeededRandom(1);
            uint hash = 0;
            for (var i = 0; i < 1000; i++) { hash = unchecked((hash * 31) + random.NextUInt()); }
            Assert.That(hash, Is.EqualTo(SequenceChecksum));
        }

        private const uint SequenceChecksum = 164080971;
    }
}
