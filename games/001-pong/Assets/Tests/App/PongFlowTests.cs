using System.Collections.Generic;
using System.Linq;
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
        public void MatchEnd_MovesToResult_ThenSubmitStartsNewMatchWithNextSeed()
        {
            _flow.Submit();
            var firstSeed = _flow.MatchSeed;
            PlayUntilResult();
            var finished = _flow.Match!;
            Assert.That(finished.Winner, Is.Not.Null);

            _flow.Submit();

            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_flow.Match, Is.Not.SameAs(finished));
            Assert.That(_flow.Match!.LeftScore + _flow.Match.RightScore, Is.Zero);
            Assert.That(_flow.MatchSeed, Is.EqualTo(firstSeed + 1));
        }

        [Test]
        public void NthRematch_CanBeReproducedFromItsSeedAndInputsAlone()
        {
            // 3 試合続けて遊び、各試合のシードとプレイヤー入力列、Tick ごとの状態と出来事を記録する。
            var played = new List<(int Seed, List<float> Inputs, List<MatchSnapshot> Snapshots, List<MatchEvent[]> Events, MatchState Match)>();
            var random = new SeededRandom(7);
            for (var match = 0; match < 3; match++)
            {
                _flow.Submit();
                var inputs = new List<float>();
                var snapshots = new List<MatchSnapshot>();
                var events = new List<MatchEvent[]>();
                for (var frame = 0; frame < 60 * 60 * 30 && _flow.Screen == AppScreen.Playing; frame++)
                {
                    var input = random.Range(-1f, 1f);
                    _flow.Advance(_config.TickDuration, input); // ちょうど 1 Tick 進む
                    inputs.Add(input);
                    snapshots.Add(_flow.Current);
                    events.Add(_flow.FrameEvents.ToArray());
                }

                Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Result));
                played.Add((_flow.MatchSeed, inputs, snapshots, events, _flow.Match!));
            }

            // 3 試合目を、前の試合を一切流さずに単独で再現し、全 Tick の位置と出来事を比べる。
            var (seed, recorded, recordedSnapshots, recordedEvents, original) = played[2];
            var replay = new SeededMatch(_config, new CpuConfig(), seed);
            for (var tick = 0; tick < recorded.Count; tick++)
            {
                replay.Tick(recorded[tick]);
                var expected = recordedSnapshots[tick];
                var actual = replay.State.Snapshot;
                Assert.That(actual.Ball, Is.EqualTo(expected.Ball), $"Tick {tick + 1} のボール位置");
                Assert.That(actual.BallInPlay, Is.EqualTo(expected.BallInPlay), $"Tick {tick + 1} のボール状態");
                Assert.That(actual.LeftY, Is.EqualTo(expected.LeftY), $"Tick {tick + 1} の左パドル");
                Assert.That(actual.RightY, Is.EqualTo(expected.RightY), $"Tick {tick + 1} の右パドル");
                Assert.That(replay.State.Events, Is.EqualTo(recordedEvents[tick]), $"Tick {tick + 1} の出来事");
            }

            Assert.That(replay.State.Winner, Is.EqualTo(original.Winner));
            Assert.That(replay.State.LeftScore, Is.EqualTo(original.LeftScore));
            Assert.That(replay.State.RightScore, Is.EqualTo(original.RightScore));
            Assert.That(replay.State.Ticks, Is.EqualTo(original.Ticks));
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
