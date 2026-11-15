using System.Collections.Generic;
using System.Linq;
using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class BreakoutFlowTests
    {
        private const double Frame = 1d / 60d;
        private const int MaxTicksPerGame = 60 * 60 * 30;

        private BreakoutConfig _config = null!;
        private BreakoutFlow _flow = null!;

        [SetUp]
        public void SetUp()
        {
            _config = new BreakoutConfig();
            _flow = new BreakoutFlow(_config, seed: 1);
        }

        [Test]
        public void StartsAtTitleWithoutGame()
        {
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(_flow.Game, Is.Null);
        }

        [Test]
        public void Title_IgnoresTimeUntilSubmit()
        {
            _flow.Advance(10d, 1f);

            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(_flow.FrameEvents, Is.Empty);
        }

        [Test]
        public void Submit_FromTitle_StartsGameWaitingForLaunch()
        {
            _flow.Submit();

            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_flow.Game!.Phase, Is.EqualTo(GamePhase.Serving));
            Assert.That(_flow.GameSeed, Is.EqualTo(1));
        }

        [Test]
        public void Submit_DuringPlay_LaunchesInsteadOfRestarting()
        {
            _flow.Submit();
            var game = _flow.Game;
            AdvanceFrames(10);

            _flow.Submit();
            _flow.Advance(Frame, 0f);

            Assert.That(_flow.Game, Is.SameAs(game));
            Assert.That(game!.Phase, Is.EqualTo(GamePhase.Playing));
            Assert.That(_flow.FrameEvents, Has.Member(new GameEvent(GameEventKind.Launched)));
        }

        [Test]
        public void LaunchPressedOnFrameWithoutTick_IsKeptForNextTick()
        {
            _flow.Submit();
            _flow.Submit();

            _flow.Advance(_config.TickDuration * 0.4d, 0f); // まだ Tick が進まない
            Assert.That(_flow.Game!.Phase, Is.EqualTo(GamePhase.Serving));

            _flow.Advance(_config.TickDuration * 0.7d, 0f);
            Assert.That(_flow.Game.Phase, Is.EqualTo(GamePhase.Playing));
        }

        [Test]
        public void LaunchRequest_IsConsumedByOneTick()
        {
            // 発射中に押した決定は、次のミスの後の発射待ちまで持ち越さない。
            _flow.Submit();
            _flow.Submit();
            _flow.Advance(Frame, 0f);
            _flow.Submit();
            _flow.Advance(Frame, 0f);

            var game = _flow.Game!;
            for (var i = 0; i < MaxTicksPerGame && game.Lives == _config.Lives; i++)
            {
                _flow.Advance(Frame, game.Ball.Position.X > 0f ? -1f : 1f);
            }

            AdvanceFrames(30);
            Assert.That(game.Phase, Is.EqualTo(GamePhase.Serving));
        }

        [Test]
        public void Advance_RunsFixedTicksRegardlessOfFrameSplit()
        {
            _flow.Submit();

            _flow.Advance(_config.TickDuration * 2.5d, 0f);

            Assert.That(_flow.Game!.Ticks, Is.EqualTo(2));
            Assert.That(_flow.Alpha, Is.EqualTo(0.5f).Within(1e-3f));
        }

        [Test]
        public void Advance_LongStall_IsCappedPerFrame()
        {
            _flow.Submit();

            _flow.Advance(5d, 0f);

            Assert.That(_flow.Game!.Ticks, Is.EqualTo(BreakoutFlow.MaxTicksPerFrame));
            Assert.That(_flow.Alpha, Is.LessThanOrEqualTo(1f));
        }

        [TestCase(-1d)]
        [TestCase(0d)]
        [TestCase(double.NaN)]
        [TestCase(double.NegativeInfinity)]
        public void Advance_InvalidDelta_DoesNothing(double delta)
        {
            _flow.Submit();

            _flow.Advance(delta, 0f);

            Assert.That(_flow.Game!.Ticks, Is.Zero);
        }

        [Test]
        public void GameEnd_MovesToResult_ThenSubmitStartsNewGameWithNextSeed()
        {
            _flow.Submit();
            PlayUntilResult(new Autopilot(3));
            var finished = _flow.Game!;
            Assert.That(finished.IsOver, Is.True);

            _flow.Submit();

            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Playing));
            Assert.That(_flow.Game, Is.Not.SameAs(finished));
            Assert.That(_flow.Game!.Score, Is.Zero);
            Assert.That(_flow.Game.Lives, Is.EqualTo(_config.Lives));
            Assert.That(_flow.GameSeed, Is.EqualTo(2));
        }

        [Test]
        public void Autopilot_ClearsAllStagesThroughFlow()
        {
            _flow.Submit();

            PlayUntilResult(new Autopilot(5));

            Assert.That(_flow.Game!.Phase, Is.EqualTo(GamePhase.Won));
        }

        [Test]
        public void Result_IgnoresTimeAndLaunch()
        {
            _flow.Submit();
            PlayUntilResult(null);
            var ticks = _flow.Game!.Ticks;

            _flow.Advance(10d, 1f);

            Assert.That(_flow.Game.Ticks, Is.EqualTo(ticks));
            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Result));
        }

        [Test]
        public void Back_ReturnsToTitleAndDiscardsGame()
        {
            _flow.Submit();
            AdvanceFrames(10);

            _flow.Back();

            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Title));
            Assert.That(_flow.Game, Is.Null);

            _flow.Submit();
            Assert.That(_flow.GameSeed, Is.EqualTo(2), "戻った後のゲームも次のシード");
        }

        [Test]
        public void NthGame_CanBeReproducedFromItsSeedAndInputsAlone()
        {
            // 3 ゲーム続けて遊び、各ゲームのシードと入力列、Tick ごとの状態と出来事を記録する。
            var played = new List<(int Seed, List<(float Move, bool Launch)> Inputs, List<BreakoutSnapshot> Snapshots, List<GameEvent[]> Events, BreakoutState Game)>();
            var random = new SeededRandom(7);
            for (var game = 0; game < 3; game++)
            {
                _flow.Submit();
                var inputs = new List<(float, bool)>();
                var snapshots = new List<BreakoutSnapshot>();
                var events = new List<GameEvent[]>();
                for (var tick = 0; tick < 60 * 60 * 5 && _flow.Screen == AppScreen.Playing; tick++)
                {
                    var move = random.Range(-1f, 1f);
                    var launch = tick % 120 == 0;
                    if (launch)
                    {
                        _flow.Submit();
                    }

                    _flow.Advance(_config.TickDuration, move); // ちょうど 1 Tick 進む
                    inputs.Add((move, launch));
                    snapshots.Add(_flow.Current);
                    events.Add(_flow.FrameEvents.ToArray());
                }

                played.Add((_flow.GameSeed, inputs, snapshots, events, _flow.Game!));
                _flow.Back();
            }

            // 3 ゲーム目を、前のゲームを一切流さずに単独で再現し、全 Tick の位置と出来事を比べる。
            var (seed, recorded, recordedSnapshots, recordedEvents, original) = played[2];
            var replay = new BreakoutState(_config, seed);
            for (var tick = 0; tick < recorded.Count; tick++)
            {
                replay.Tick(recorded[tick].Move, recorded[tick].Launch);
                var expected = recordedSnapshots[tick];
                var actual = replay.Snapshot;
                Assert.That(actual.Ball, Is.EqualTo(expected.Ball), $"Tick {tick + 1} のボール位置");
                Assert.That(actual.PaddleX, Is.EqualTo(expected.PaddleX), $"Tick {tick + 1} のパドル");
                Assert.That(replay.Events, Is.EqualTo(recordedEvents[tick]), $"Tick {tick + 1} の出来事");
            }

            Assert.That(replay.Score, Is.EqualTo(original.Score));
            Assert.That(replay.Lives, Is.EqualTo(original.Lives));
            Assert.That(replay.Ticks, Is.EqualTo(original.Ticks));
        }

        [Test]
        public void InvalidConfig_IsRejected()
        {
            Assert.Throws<System.ArgumentException>(() => new BreakoutFlow(new BreakoutConfig { Lives = 0 }, 1));
        }

        private void AdvanceFrames(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                _flow.Advance(Frame, 0f);
            }
        }

        // 操縦役がいなければ何もせず（ボールを落として）負ける。発射待ちなら毎回発射する。
        private void PlayUntilResult(Autopilot? autopilot)
        {
            for (var i = 0; i < MaxTicksPerGame && _flow.Screen == AppScreen.Playing; i++)
            {
                var game = _flow.Game!;
                if (game.Phase == GamePhase.Serving)
                {
                    _flow.Submit();
                }

                _flow.Advance(_config.TickDuration, autopilot?.Decide(game) ?? 0f);
            }

            Assert.That(_flow.Screen, Is.EqualTo(AppScreen.Result));
        }
    }
}
