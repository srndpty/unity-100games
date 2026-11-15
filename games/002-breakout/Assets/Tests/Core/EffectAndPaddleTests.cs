using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class EffectAndPaddleTests
    {
        private const int Duration = 5;

        private BreakoutConfig _config = null!;
        private EffectTimers _effects = null!;
        private List<EffectKind> _expired = null!;

        [SetUp]
        public void SetUp()
        {
            _config = new BreakoutConfig { PierceDurationTicks = Duration, ExpandDurationTicks = Duration * 2 };
            _effects = new EffectTimers(_config);
            _expired = new List<EffectKind>();
        }

        [Test]
        public void Effect_IsActiveForExactlyDurationTicksAfterActivation()
        {
            // 発動した Tick（減らさない）
            _effects.Activate(EffectKind.Pierce);
            _effects.Advance(_expired);

            var activeTicks = 0;
            while (_effects.IsActive(EffectKind.Pierce))
            {
                activeTicks++;
                _effects.Advance(_expired);
            }

            Assert.That(activeTicks, Is.EqualTo(Duration));
            Assert.That(_expired, Is.EqualTo(new[] { EffectKind.Pierce }), "時間切れは 1 回だけ");
        }

        [Test]
        public void Reactivation_ResetsRemainingWithoutAdding()
        {
            _effects.Activate(EffectKind.Pierce);
            _effects.Advance(_expired);
            _effects.Advance(_expired);
            _effects.Advance(_expired);
            Assert.That(_effects.Remaining(EffectKind.Pierce), Is.EqualTo(Duration - 2));

            _effects.Activate(EffectKind.Pierce);
            _effects.Advance(_expired);

            Assert.That(_effects.Remaining(EffectKind.Pierce), Is.EqualTo(Duration));
            Assert.That(_expired, Is.Empty);
        }

        [Test]
        public void Effects_RunIndependently()
        {
            _effects.Activate(EffectKind.Pierce);
            _effects.Activate(EffectKind.Expand);
            for (var i = 0; i <= Duration; i++)
            {
                _effects.Advance(_expired);
            }

            Assert.That(_effects.IsActive(EffectKind.Pierce), Is.False);
            Assert.That(_effects.IsActive(EffectKind.Expand), Is.True);
            Assert.That(_effects.Remaining(EffectKind.Expand), Is.EqualTo(Duration));
        }

        [Test]
        public void Clear_DeactivatesAllWithoutExpiredEvents()
        {
            _effects.Activate(EffectKind.Pierce);
            _effects.Activate(EffectKind.Expand);

            _effects.Clear();
            _effects.Advance(_expired);

            Assert.That(_effects.IsActive(EffectKind.Pierce), Is.False);
            Assert.That(_effects.IsActive(EffectKind.Expand), Is.False);
            Assert.That(_expired, Is.Empty);
        }

        [Test]
        public void Paddle_StopsAtWalls()
        {
            var paddle = new Paddle(_config);
            for (var i = 0; i < 120; i++)
            {
                paddle.Move(1f);
            }

            Assert.That(paddle.X, Is.EqualTo(5f), "右の壁 6 から半幅 1 だけ内側");
        }

        [Test]
        public void Expand_AtWall_PushesPaddleInside()
        {
            var paddle = new Paddle(_config);
            for (var i = 0; i < 120; i++)
            {
                paddle.Move(1f);
            }

            paddle.SetExpanded(true);
            Assert.That(paddle.HalfWidth, Is.EqualTo(1.5f));
            Assert.That(paddle.X, Is.EqualTo(4.5f));
            Assert.That(paddle.Motion.From, Is.EqualTo(paddle.Motion.To), "押し戻しは Tick 中の移動として扱わない");

            paddle.SetExpanded(false);
            Assert.That(paddle.HalfWidth, Is.EqualTo(1f));
            Assert.That(paddle.X, Is.EqualTo(4.5f), "縮むときは位置を変えない");
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Paddle_IgnoresNonFiniteInput(float input)
        {
            var paddle = new Paddle(_config);

            paddle.Move(input);

            Assert.That(paddle.X, Is.EqualTo(0f));
        }

        [Test]
        public void Paddle_ClampsInputMagnitude()
        {
            var paddle = new Paddle(_config);

            paddle.Move(-10f);

            Assert.That(paddle.X, Is.EqualTo(-_config.PaddleSpeed * _config.TickDuration).Within(1e-6f));
        }

        [Test]
        public void PaddleMotion_StopsAtLimitPartwayThroughTick()
        {
            var motion = PaddleMotion.Clamped(from: 4.9f, requestedDelta: 0.2f, limit: 5f);

            Assert.That(motion.To, Is.EqualTo(5f));
            Assert.That(motion.StopAt, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(motion.XAt(0.25f), Is.EqualTo(4.95f).Within(1e-4f));
            Assert.That(motion.XAt(0.75f), Is.EqualTo(5f));
        }
    }
}
