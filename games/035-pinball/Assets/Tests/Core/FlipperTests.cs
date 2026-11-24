using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>フリッパーのアクチュエータ（押すと加速して上で止まり、離すと戻る）。</summary>
    public sealed class FlipperTests
    {
        private static readonly PinballConfig _config = new();

        [Test]
        public void PressAcceleratesFromTheFirstSubstep([Values(FlipperSide.Left, FlipperSide.Right)] FlipperSide side)
        {
            var spec = TestKit.Production().Flipper(side);
            var flipper = new Flipper(spec) { Held = true };
            flipper.Step(_config);
            Assert.That(flipper.Speed, Is.EqualTo(spec.UpDirection * _config.FlipAccel));
            Assert.That(flipper.Angle, Is.EqualTo(spec.RestAngle + (spec.UpDirection * _config.FlipAccel)));
        }

        [Test]
        public void RisesToTheStopAndHolds([Values(FlipperSide.Left, FlipperSide.Right)] FlipperSide side)
        {
            var spec = TestKit.Production().Flipper(side);
            var flipper = new Flipper(spec) { Held = true };
            var steps = 0;
            while (!flipper.IsUp)
            {
                flipper.Step(_config);
                Assert.That(System.Math.Abs(flipper.Speed), Is.LessThanOrEqualTo(_config.FlipMaxSpeed));
                Assert.That(++steps, Is.LessThan(200));
            }

            // 端に着いた区間で止まり（速さ 0）、押している間は動かない。
            Assert.That(flipper.Speed, Is.Zero);
            Assert.That(steps, Is.EqualTo(ExpectedSteps(spec.UpAngle - spec.RestAngle, _config.FlipAccel, _config.FlipMaxSpeed)));
            for (var i = 0; i < 50; i++)
            {
                flipper.Step(_config);
                Assert.That(flipper.Angle, Is.EqualTo(spec.UpAngle));
                Assert.That(flipper.LastDelta, Is.Zero);
            }
        }

        [Test]
        public void ReleaseReturnsToRest([Values(FlipperSide.Left, FlipperSide.Right)] FlipperSide side)
        {
            var spec = TestKit.Production().Flipper(side);
            var flipper = new Flipper(spec) { Held = true };
            for (var i = 0; i < 100; i++) { flipper.Step(_config); }
            flipper.Held = false;
            flipper.Step(_config);
            Assert.That(flipper.Speed, Is.EqualTo(-spec.UpDirection * _config.ReturnAccel), "離した区間から戻り始める");
            for (var i = 0; i < 200; i++) { flipper.Step(_config); }
            Assert.That(flipper.Angle, Is.EqualTo(spec.RestAngle));
            Assert.That(flipper.Speed, Is.Zero);
        }

        [Test]
        public void TapReversesMidway()
        {
            var spec = TestKit.Production().Left;
            var flipper = new Flipper(spec) { Held = true };
            for (var i = 0; i < 5; i++) { flipper.Step(_config); }
            var peak = flipper.Angle;
            Assert.That(peak, Is.GreaterThan(spec.RestAngle).And.LessThan(spec.UpAngle));
            flipper.Held = false;
            for (var i = 0; i < 200; i++)
            {
                flipper.Step(_config);
                Assert.That(flipper.Angle, Is.InRange(spec.RestAngle, spec.UpAngle));
            }

            Assert.That(flipper.Angle, Is.EqualTo(spec.RestAngle));
        }

        [Test]
        public void SurfaceVelocityIsPerpendicularAndProportionalToRadius()
        {
            var spec = TestKit.Production().Left;
            var flipper = new Flipper(spec);
            Assert.That(flipper.SurfaceVelocity(spec.TipAt(spec.RestAngle)), Is.EqualTo(Vec2i.Zero), "止まっていれば 0");

            flipper.Held = true;
            flipper.Step(_config);
            var tip = flipper.Tip;
            var half = spec.Pivot + new Vec2i((tip.X - spec.Pivot.X) / 2, (tip.Y - spec.Pivot.Y) / 2);
            var atTip = flipper.SurfaceVelocity(tip);
            var atHalf = flipper.SurfaceVelocity(half);
            Assert.That(Vec2i.Dot(atTip, tip - spec.Pivot), Is.InRange(-(long)atTip.LengthSquared / 100, (long)atTip.LengthSquared / 100), "腕に垂直");
            Assert.That(atTip.Y, Is.GreaterThan(0), "左のフリッパーが上がると先端は上へ動く");
            Assert.That(atHalf.LengthSquared * 4, Is.InRange(atTip.LengthSquared * 95 / 100, atTip.LengthSquared * 105 / 100), "半分の距離で半分の速さ");

            // ω × r：1 区間に回った角度 × 腕の長さ × 2π / 65536 単位（速度の単位は 1/256）。
            var expected = (double)flipper.LastDelta * spec.Length * 2 * System.Math.PI / Trig.FullTurn * Board.VelocityScale;
            Assert.That(System.Math.Sqrt(atTip.LengthSquared), Is.EqualTo(expected).Within(expected / 100));
        }

        [Test]
        public void ProductionFlipperTipStepIsSafe()
        {
            foreach (var spec in new[] { TestKit.Production().Left, TestKit.Production().Right })
            {
                Assert.That(Table.TipStepIsSafe(_config.FlipMaxSpeed, spec.Length, spec.Radius, _config.BallRadius), Is.True);

                // 実際に最大の速さで回したときの先端の移動も「球の半径 + フリッパーの半径」より小さい。
                var flipper = new Flipper(spec) { Held = true };
                var previous = flipper.Tip;
                while (!flipper.IsUp)
                {
                    flipper.Step(_config);
                    Assert.That((flipper.Tip - previous).LengthSquared, Is.LessThan((long)(_config.BallRadius + spec.Radius) * (_config.BallRadius + spec.Radius)));
                    previous = flipper.Tip;
                }
            }
        }

        [Test]
        public void TipStepBoundary()
        {
            // 先端の弧 = ω × L × 2π/65536 と「半径の和」の境界（2π ≒ 411775 / 65536）。
            const int Length = 3277;
            const int Reach = 512 + 307;
            var limit = (int)(((long)Reach << 32) / ((long)Length * Table.TwoPiQ16));
            Assert.That(Table.TipStepIsSafe(limit, Length, 307, 512), Is.True);
            Assert.That(Table.TipStepIsSafe(limit + 1, Length, 307, 512), Is.False);
        }

        /// <summary>加速して頭打ちになる動きで、角度差に着くまでの区間の数（参照の計算）。</summary>
        private static int ExpectedSteps(int distance, int accel, int max)
        {
            distance = System.Math.Abs(distance);
            int angle = 0, speed = 0, steps = 0;
            while (angle < distance)
            {
                speed = System.Math.Min(speed + accel, max);
                angle += speed;
                steps++;
            }

            return steps;
        }
    }
}
