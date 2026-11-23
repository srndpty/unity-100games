using System;
using NUnit.Framework;

namespace IntGeometry.Tests
{
    /// <summary>整数の角度表。Math.Sin を実行時に使わず、表の値は Mono と .NET で同じ。</summary>
    public sealed class TrigTests
    {
        [Test]
        public void Table_MatchesMathSinWithinOne()
        {
            var worst = 0.0;
            for (var i = 0; i < Trig.TableSize; i++)
            {
                var expected = Math.Sin(2 * Math.PI * i / Trig.TableSize) * Trig.One;
                worst = Math.Max(worst, Math.Abs(Trig.SinIndex(i) - expected));
            }

            Assert.That(worst, Is.LessThanOrEqualTo(1.0));
        }

        [Test]
        public void Table_HasExactKeyValuesAndSymmetry()
        {
            Assert.That(Trig.Sin(Trig.Right), Is.EqualTo(0));
            Assert.That(Trig.Sin(Trig.Up), Is.EqualTo(Trig.One));
            Assert.That(Trig.Sin(Trig.Left), Is.EqualTo(0));
            Assert.That(Trig.Sin(Trig.Down), Is.EqualTo(-Trig.One));
            Assert.That(Trig.Cos(Trig.Right), Is.EqualTo(Trig.One));
            Assert.That(Trig.Cos(Trig.Left), Is.EqualTo(-Trig.One));

            for (var i = 0; i < Trig.TableSize; i++)
            {
                Assert.That(Trig.SinIndex(-i), Is.EqualTo(-Trig.SinIndex(i)), "奇関数");
                Assert.That(Trig.SinIndex((Trig.TableSize / 2) - i), Is.EqualTo(Trig.SinIndex(i)), "sin(π - x) = sin x");
            }
        }

        [Test]
        public void Table_IsNonDecreasingInFirstQuarter()
        {
            // π/2 の近くでは隣どうしの差が Q14 の 1 未満なので、同じ値が続く。
            for (var i = 1; i <= Trig.TableSize / 4; i++)
            {
                Assert.That(Trig.SinIndex(i), Is.GreaterThanOrEqualTo(Trig.SinIndex(i - 1)));
            }
        }

        [Test]
        public void Table_FixedChecksum()
        {
            // 表は整数だけで作るので、どの実行環境でも同じ合計になる（.NET と Mono の両方で照合する）。
            long sum = 0;
            for (var i = 0; i < Trig.TableSize; i++) { sum = (sum * 31) + Trig.SinIndex(i); }
            Assert.That(sum, Is.EqualTo(TableChecksum));
        }

        [Test]
        public void Angles_OutsideOneTurn_Wrap()
        {
            Assert.That(Trig.Normalize(-1), Is.EqualTo(Trig.FullTurn - 1));
            Assert.That(Trig.Normalize(Trig.FullTurn), Is.EqualTo(0));
            Assert.That(Trig.Sin(Trig.Up + Trig.FullTurn * 3), Is.EqualTo(Trig.One));
            Assert.That(Trig.Cos(-Trig.Left), Is.EqualTo(-Trig.One));
        }

        [Test]
        public void Atan2_IsWithinOneTableStepOfMathAtan2()
        {
            var random = new SeededRandom(7);
            for (var n = 0; n < 5000; n++)
            {
                var x = random.Range(-100000, 100000);
                var y = random.Range(-100000, 100000);
                if (x == 0 && y == 0) { continue; }

                var angle = Trig.Atan2(y, x, 0);
                var expected = Math.Atan2(y, x) / (2 * Math.PI) * Trig.FullTurn;
                var diff = Math.Abs(angle - expected) % Trig.FullTurn;
                diff = Math.Min(diff, Trig.FullTurn - diff);
                Assert.That(diff, Is.LessThanOrEqualTo(16.0), $"({x}, {y})");
                Assert.That(angle % (1 << Trig.IndexShift), Is.EqualTo(0), "表の刻みに丸める");
            }
        }

        [TestCase(1, 0, Trig.Right)]
        [TestCase(0, 1, Trig.Up)]
        [TestCase(-1, 0, Trig.Left)]
        [TestCase(0, -1, Trig.Down)]
        [TestCase(5, 5, Trig.FullTurn / 8)]
        [TestCase(-5, -5, Trig.FullTurn * 5 / 8)]
        public void Atan2_ExactDirections(int x, int y, int expected)
        {
            Assert.That(Trig.Atan2(y, x, 0), Is.EqualTo(expected));
        }

        [Test]
        public void Atan2_OfZeroVector_ReturnsFallback()
        {
            Assert.That(Trig.Atan2(0, 0, Trig.Down), Is.EqualTo(Trig.Down));
            Assert.That(Trig.Atan2(0, 0, -16), Is.EqualTo(Trig.FullTurn - 16));
        }

        [Test]
        public void Atan2_OfHugeVector_DoesNotOverflow()
        {
            Assert.That(Trig.Atan2(long.MaxValue / 4, long.MaxValue / 4, 0), Is.EqualTo(Trig.FullTurn / 8));
            Assert.That(Trig.Atan2(-(long.MaxValue / 4), 1, 0), Is.EqualTo(Trig.Down));
        }

        private const long TableChecksum = 104113459082297344;
    }
}
