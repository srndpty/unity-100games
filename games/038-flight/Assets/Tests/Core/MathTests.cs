using System;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>Q30 の三角関数とクォータニオンを、テスト側の倍精度の参照実装と照合する。</summary>
    public sealed class MathTests
    {
        private const double One = 1 << 30;

        [Test]
        public void SinAndCosMatchDoubleAtEveryTableStep()
        {
            for (var angle = -70000; angle <= 70000; angle += 37)
            {
                var radians = angle * Math.PI * 2 / Units.FullTurn;
                Assert.That(Fixed.Sin(angle) / One, Is.EqualTo(Math.Sin(radians)).Within(8e-9), $"sin {angle}");
                Assert.That(Fixed.Cos(angle) / One, Is.EqualTo(Math.Cos(radians)).Within(8e-9), $"cos {angle}");
            }
        }

        [Test]
        public void WrapKeepsHalfTurnPositive()
        {
            Assert.That(Fixed.Wrap(32768), Is.EqualTo(32768));
            Assert.That(Fixed.Wrap(-32768), Is.EqualTo(32768));
            Assert.That(Fixed.Wrap(32769), Is.EqualTo(-32767));
            Assert.That(Fixed.Wrap(65536 + 5), Is.EqualTo(5));
            Assert.That(Fixed.Wrap(-5), Is.EqualTo(-5));
        }

        [Test]
        public void ProductAndRotationMatchReference()
        {
            var random = new SeededRandom(38);
            for (var i = 0; i < 2000; i++)
            {
                var a = RandomRotation(random);
                var b = RandomRotation(random);
                var product = a * b;
                var expected = Ref.Of(a).Mul(Ref.Of(b));
                AssertClose(product, expected, 4e-9);

                var v = new Vec3l(random.Range(-2_000_000_000, 2_000_000_000), random.Range(-2_000_000_000, 2_000_000_000), random.Range(-2_000_000_000, 2_000_000_000));
                var rotated = a.Rotate(v);
                var (x, y, z) = Ref.Of(a).Rotate(v.X, v.Y, v.Z);
                Assert.That(rotated.X, Is.EqualTo(x).Within(16));
                Assert.That(rotated.Y, Is.EqualTo(y).Within(16));
                Assert.That(rotated.Z, Is.EqualTo(z).Within(16));
            }
        }

        [Test]
        public void AxesAreTheRotatedBasisAndOrthonormal()
        {
            var random = new SeededRandom(7);
            for (var i = 0; i < 500; i++)
            {
                var q = RandomRotation(random);
                var r = q.Right;
                var u = q.Up;
                var f = q.Forward;
                AssertVector(r, Ref.Of(q).Rotate(One, 0, 0));
                AssertVector(u, Ref.Of(q).Rotate(0, One, 0));
                AssertVector(f, Ref.Of(q).Rotate(0, 0, One));
                Assert.That(Math.Abs(Vec3l.Dot(r, u)) / (One * One), Is.LessThan(1e-8));
                Assert.That(Math.Abs(Vec3l.Dot(u, f)) / (One * One), Is.LessThan(1e-8));
                Assert.That(r.LengthSquared / (One * One), Is.EqualTo(1).Within(1e-8));
            }
        }

        [Test]
        public void UnityDirectionsMatchAxisConventions()
        {
            // 方位 90°（東）は機首が +x。機首上げ 90° は機首が +y。
            var east = Quat.FromHeadingPitch(Units.FullTurn / 4, 0).Forward;
            Assert.That(east.X / One, Is.EqualTo(1).Within(8e-9));
            var up = Quat.FromHeadingPitch(0, Units.FullTurn / 4).Forward;
            Assert.That(up.Y / One, Is.EqualTo(1).Within(8e-9));

            // 機体の +x 回転は機首下げ、+z 回転は右翼が上がる（左へ傾く）、+y 回転は機首が右。
            var level = Quat.Identity;
            Assert.That(level.RotateBody(100, 0, 0).Forward.Y, Is.LessThan(0));
            Assert.That(level.RotateBody(0, 0, 100).Right.Y, Is.GreaterThan(0));
            Assert.That(level.RotateBody(0, 100, 0).Forward.X, Is.GreaterThan(0));
        }

        [Test]
        public void NormStaysUnitOverLongManeuvers()
        {
            var q = Quat.Identity;
            var random = new SeededRandom(3);
            for (var tick = 0; tick < 100_000; tick++)
            {
                q = q.RotateBody(random.Range(-200, 200), random.Range(-60, 60), random.Range(-400, 400));
                var norm = Math.Sqrt(q.NormSquared) / One;
                Assert.That(norm, Is.EqualTo(1).Within(1e-8), $"tick {tick}");
            }
        }

        [Test]
        public void FullRollReturnsToStartWithinSmallError()
        {
            // 1 Tick に 364（2°）ずつ 180 回で 1 周する。
            var q = Quat.FromHeadingPitch(1000, 2000);
            var start = q;
            for (var tick = 0; tick < 180; tick++) { q = q.RotateBody(0, 0, 364); }
            var forwardDot = Vec3l.Dot(q.Forward, start.Forward) / (One * One);
            var upDot = Vec3l.Dot(q.Up, start.Up) / (One * One);
            Assert.That(forwardDot, Is.EqualTo(1).Within(1e-6));
            Assert.That(upDot, Is.EqualTo(1).Within(1e-3), "1 周の角度の誤差は 1 次の近似と表の丸めの分（0.1° 程度）まで");
        }

        [Test]
        public void ZeroQuaternionCannotBeNormalized()
        {
            Assert.Throws<InvalidOperationException>(() => new Quat(0, 0, 0, 0).Normalized());
        }

        private static Quat RandomRotation(SeededRandom random)
        {
            var axis = new Vec3l(random.Range(-1000, 1000), random.Range(-1000, 1000), random.Range(-1000, 1000) + 1);
            var length = Math.Sqrt(axis.LengthSquared);
            var unit = new Vec3l((long)(axis.X / length * One), (long)(axis.Y / length * One), (long)(axis.Z / length * One));
            return Quat.AxisAngle(unit, random.Range(-32768, 32768)).Normalized();
        }

        private static void AssertClose(Quat actual, Ref expected, double tolerance)
        {
            Assert.That(actual.W / One, Is.EqualTo(expected.W).Within(tolerance));
            Assert.That(actual.X / One, Is.EqualTo(expected.X).Within(tolerance));
            Assert.That(actual.Y / One, Is.EqualTo(expected.Y).Within(tolerance));
            Assert.That(actual.Z / One, Is.EqualTo(expected.Z).Within(tolerance));
        }

        private static void AssertVector(Vec3l actual, (double X, double Y, double Z) expected)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(4));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(4));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(4));
        }

        /// <summary>倍精度の参照クォータニオン。</summary>
        private readonly struct Ref
        {
            public Ref(double w, double x, double y, double z)
            {
                W = w;
                X = x;
                Y = y;
                Z = z;
            }

            public double W { get; }

            public double X { get; }

            public double Y { get; }

            public double Z { get; }

            public static Ref Of(Quat q) => new(q.W / One, q.X / One, q.Y / One, q.Z / One);

            public Ref Mul(Ref b) => new(
                (W * b.W) - (X * b.X) - (Y * b.Y) - (Z * b.Z),
                (W * b.X) + (X * b.W) + (Y * b.Z) - (Z * b.Y),
                (W * b.Y) - (X * b.Z) + (Y * b.W) + (Z * b.X),
                (W * b.Z) + (X * b.Y) - (Y * b.X) + (Z * b.W));

            public (double X, double Y, double Z) Rotate(double x, double y, double z)
            {
                var r = Mul(new Ref(0, x, y, z)).Mul(new Ref(W, -X, -Y, -Z));
                return (r.X, r.Y, r.Z);
            }
        }
    }
}
