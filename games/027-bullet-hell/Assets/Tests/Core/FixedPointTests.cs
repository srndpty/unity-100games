using System;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>小数の文字列を浮動小数点を通さずに整数の単位へ直す。</summary>
    public sealed class FixedPointTests
    {
        [TestCase("0", 65536, 1, 0)]
        [TestCase("1", 65536, 1, 65536)]
        [TestCase("1.8", 65536, 1, 117965)] // 117964.8
        [TestCase("0.02", 65536, 1, 1311)] // 1310.72
        [TestCase("0.004", 65536, 1, 262)] // 262.144
        [TestCase("-0.5", 256, 1, -128)]
        [TestCase("-1.25", 65536, 1, -81920)]
        [TestCase("7", 65536, 360, 1274)] // 1274.31
        [TestCase("45", 65536, 360, 8192)]
        [TestCase("-360", 65536, 360, -65536)]
        [TestCase("0.5", 1, 1, 1)] // 0.5 は 0 から遠いほうへ
        [TestCase("-0.5", 1, 1, -1)]
        [TestCase("0.4999", 1, 1, 0)]
        [TestCase("123456.123456", 1, 1, 123456)]
        public void Parses(string text, long numerator, long denominator, int expected)
        {
            Assert.That(FixedPoint.TryParse(text, numerator, denominator, out var value), Is.True);
            Assert.That(value, Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("-")]
        [TestCase(".5")]
        [TestCase("1.")]
        [TestCase("1.2.3")]
        [TestCase("1e3")]
        [TestCase("+1")]
        [TestCase("1 ")]
        [TestCase("1.1234567")] // 小数点以下 7 桁
        [TestCase("1234567")] // 整数部 7 桁
        [TestCase("12345678901234567890")]
        public void RejectsMalformed(string text)
        {
            Assert.That(FixedPoint.TryParse(text, 65536, 1, out _), Is.False);
        }

        [Test]
        public void RejectsResultOutsideInt()
        {
            Assert.That(FixedPoint.TryParse("999999", 65536, 1, out _), Is.False);
            Assert.That(FixedPoint.TryParse("32767", 65536, 1, out var max), Is.True);
            Assert.That(max, Is.EqualTo(32767 * 65536));
        }

        [Test]
        public void RejectsInvalidScale()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => FixedPoint.TryParse("1", 0, 1, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => FixedPoint.TryParse("1", 1, -1, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => FixedPoint.TryParse("1", (1L << 20) + 1, 1, out _));
        }

        [Test]
        public void MatchesExactRationalRounding()
        {
            // 0.000001 刻みのすべての値で、10 進の厳密な割り算の四捨五入と一致する（float を通すと外れる値がある）。
            for (var micro = 0; micro <= 2_000_000; micro += 37)
            {
                var text = $"{micro / 1_000_000}.{micro % 1_000_000:D6}";
                Assert.That(FixedPoint.TryParse(text, 65536, 360, out var value), Is.True);
                var expected = (int)(((long)micro * 65536 + (360L * 1_000_000 / 2)) / (360L * 1_000_000));
                Assert.That(value, Is.EqualTo(expected), text);
            }
        }
    }
}
