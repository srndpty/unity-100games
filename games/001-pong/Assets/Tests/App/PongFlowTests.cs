using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class PongFlowTests
    {
        private const double Frame = 1d / 60d;

        private PongConfig _config = null!;
        private PongFlow _flow = null!;

        [SetUp]
        public void SetUp()
        {
            _config = new PongConfig();
            _flow = new PongFlow(_config, new CpuConfig(), seed: 1);
        }

        [Test]
        public void StartsAtTitleWithoutMatch()
        {
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(_flow.Match, Is.Null);
        }

        [Test]
        public void Title_IgnoresTimeUntilSubmit()
        {
            _flow.Advance(10d, 1f);

            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(_flow.FrameEvents, Is.Empty);
        }

        [Test]
        public void Submit_FromTitle_StartsMatch()
        {
            _flow.Submit();

            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_flow.Match, Is.Not.Null);
            Assert.That(_flow.Match!.Phase, Is.EqualTo(MatchPhase.Serving));
        }

        [Test]
        public void Submit_DuringPlay_IsIgnored()
        {
            _flow.Submit();
            var match = _flow.Match;
            AdvanceFrames(90);
            var ticks = match!.Ticks;

            _flow.Submit();

            Assert.That(_flow.Match, Is.SameAs(match));
            Assert.That(match.Ticks, Is.EqualTo(ticks));
        }

        [Test]
        public void Advance_RunsFixedTicksRegardlessOfFrameSplit()
        {
            _flow.Submit();

            _flow.Advance(_config.TickDuration * 2.5d, 0f);

            Assert.That(_flow.Match!.Ticks, Is.EqualTo(2));
            Assert.That(_flow.Alpha, Is.EqualTo(0.5f).Within(1e-3f));
        }

        [Test]
        public void Advance_LongStall_IsCappedPerFrame()
        {
            _flow.Submit();

            _flow.Advance(5d, 0f);

            Assert.That(_flow.Match!.Ticks, Is.EqualTo(PongFlow.MaxTicksPerFrame));
            Assert.That(_flow.Alpha, Is.LessThanOrEqualTo(1f));
        }

        [TestCase(-1d)]
        [TestCase(0d)]
        [TestCase(double.NaN)]
        public void Advance_InvalidDelta_DoesNothing(double delta)
        {
            _flow.Submit();

            _flow.Advance(delta, 0f);

            Assert.That(_flow.Match!.Ticks, Is.Zero);
        }

        [Test]
        public void MatchEnd_MovesToResult_ThenSubmitRematches()
        {
            _flow.Submit();
            PlayUntilResult();
            var match = _flow.Match!;
            Assert.That(match.Winner, Is.Not.Null);

            _flow.Submit();

            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_flow.Match, Is.SameAs(match));
            Assert.That(match.LeftScore + match.RightScore, Is.Zero);
        }

        [Test]
        public void Result_IgnoresTime()
        {
            _flow.Submit();
            PlayUntilResult();
            var ticks = _flow.Match!.Ticks;

            _flow.Advance(10d, 1f);

            Assert.That(_flow.Match.Ticks, Is.EqualTo(ticks));
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Result));
        }

        [Test]
        public void Back_ReturnsToTitleAndDiscardsMatch()
        {
            _flow.Submit();
            AdvanceFrames(10);

            _flow.Back();

            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(_flow.Match, Is.Null);
        }

        [Test]
        public void NewMatchAfterBack_UsesNextSeed()
        {
            _flow.Submit();
            var firstSeed = _flow.MatchSeed;
            _flow.Back();

            _flow.Submit();

            Assert.That(_flow.MatchSeed, Is.EqualTo(firstSeed + 1));
        }

        private void AdvanceFrames(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                _flow.Advance(Frame, 0f);
            }
        }

        private void PlayUntilResult()
        {
            for (var i = 0; i < 60 * 60 * 30 && _flow.Screen == AppScreen.Playing; i++)
            {
                _flow.Advance(Frame, 0f);
            }

            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Result));
        }
    }
}
