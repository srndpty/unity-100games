using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class SnapshotTests
    {
        [Test]
        public void MovingBall_IsInterpolatedBetweenTicks()
        {
            var from = new BreakoutSnapshot(new Vec2(0f, 0f), true, PaddleMotion.At(0f), 1f);
            var to = new BreakoutSnapshot(new Vec2(1f, 2f), true, PaddleMotion.At(0f), 1f);

            var middle = BreakoutSnapshot.Interpolate(from, to, 0.5f);

            Assert.That(middle.Ball, Is.EqualTo(new Vec2(0.5f, 1f)));
        }

        [Test]
        public void HeldBall_MovesWithPaddle()
        {
            var paddle = PaddleMotion.Clamped(0f, 0.2f, 5f);
            var from = new BreakoutSnapshot(new Vec2(0f, -6f), false, PaddleMotion.At(0f), 1f);
            var to = new BreakoutSnapshot(new Vec2(0.2f, -6f), false, paddle, 1f);

            var middle = BreakoutSnapshot.Interpolate(from, to, 0.5f);

            Assert.That(middle.PaddleX, Is.EqualTo(0.1f).Within(1e-6f));
            Assert.That(middle.Ball.X, Is.EqualTo(0.1f).Within(1e-6f));
        }

        [Test]
        public void BallReturnedToPaddle_IsNotInterpolatedFromWhereItFell()
        {
            var from = new BreakoutSnapshot(new Vec2(3f, -8.2f), true, PaddleMotion.At(0f), 1f);
            var to = new BreakoutSnapshot(new Vec2(0f, -6f), false, PaddleMotion.At(0f), 1f);

            var middle = BreakoutSnapshot.Interpolate(from, to, 0.5f);

            Assert.That(middle.Ball, Is.EqualTo(to.Ball));
        }

        [Test]
        public void State_SnapshotReflectsPaddleWidth()
        {
            var state = TestStages.NewState(TestStages.Config("...N........"));
            state.Effects.Activate(EffectKind.Expand);

            Assert.That(state.Snapshot.PaddleHalfWidth, Is.EqualTo(1f), "幅はカプセルを取ったときに変わる");
            Assert.That(state.Snapshot.BallInPlay, Is.False);
        }
    }
}
