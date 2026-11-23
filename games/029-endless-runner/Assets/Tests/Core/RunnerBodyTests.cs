using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>自機の 1 Tick：レーン移動・ジャンプ・スライドと先行入力。</summary>
    public sealed class RunnerBodyTests
    {
        private static readonly RunConfig _config = new();

        private static RunnerBody Body(int lane = 1) => RunnerBody.Rest(_config, lane);

        [Test]
        public void LaneChange_TakesLaneChangeTicks_MovingLinearly()
        {
            var body = Body();
            var events = body.Step(TestKit.Do(Steer.Right), _config);
            Assert.That(events, Is.EqualTo(RunnerEvents.Steered));
            for (var tick = 1; tick <= _config.LaneChangeTicks; tick++)
            {
                if (tick > 1) { body.Step(Command.None, _config); }
                Assert.That(body.Lane.X, Is.EqualTo(_config.LaneWidth * tick / _config.LaneChangeTicks), $"Tick {tick}");
                Assert.That(body.Lane.IsMoving, Is.EqualTo(tick < _config.LaneChangeTicks));
            }

            Assert.That(body.Lane.To, Is.EqualTo(2));
            body.Step(Command.None, _config);
            Assert.That(body.IsAtRest(_config), Is.True);
        }

        [Test]
        public void SteeringPastTheEdge_IsDropped_NotBuffered()
        {
            var body = Body(0);
            Assert.That(body.Step(TestKit.Do(Steer.Left), _config), Is.EqualTo(RunnerEvents.None));
            Assert.That(body.SteerBuffer, Is.EqualTo(Steer.None), "端より外は覚えておかない");
            Assert.That(body.Lane, Is.EqualTo(LaneState.Rest(_config, 0)));
        }

        [Test]
        public void SteeringWhileMoving_IsBuffered_AndStartsRightAfterArriving()
        {
            var body = Body(0);
            body.Step(TestKit.Do(Steer.Right), _config);
            body.Step(TestKit.Do(Steer.Right), _config);
            Assert.That(body.SteerBuffer, Is.EqualTo(Steer.Right));
            for (var tick = 3; tick <= _config.LaneChangeTicks; tick++) { body.Step(Command.None, _config); }
            Assert.That(body.Lane, Is.EqualTo(new LaneState(1, 1, 0, 0, body.Lane.PrevX)), "1 つ目の移動が終わった");
            var events = body.Step(Command.None, _config);
            Assert.That(events, Is.EqualTo(RunnerEvents.Steered), "着いた次の Tick に先行入力の移動が始まる");
            Assert.That(body.Lane.To, Is.EqualTo(2));
        }

        [Test]
        public void JumpInTheAir_IsNotADoubleJump()
        {
            var body = Body();
            body.Step(TestKit.Do(leap: Leap.Jump), _config);
            var y = body.Vertical.Y;
            body.Step(TestKit.Do(leap: Leap.Jump), _config);
            Assert.That(body.Vertical.Velocity, Is.LessThan(_config.JumpVelocity - _config.Gravity), "空中で跳び直さない");
            Assert.That(body.Vertical.Y, Is.GreaterThan(y));
        }

        [Test]
        public void JumpBuffer_FiresOnLanding_OnlyWithinTheWindow()
        {
            var landing = LandingTick();

            // 着地した次の Tick（接地して最初に跳べる Tick）に、押した Tick を含めて窓の最後が届けば跳ぶ。
            var ground = landing + 1;
            Assert.That(JumpsAgain(ground - _config.InputBufferTicks + 1, ground), Is.True, "窓の最後の Tick で効く");
            Assert.That(JumpsAgain(ground - _config.InputBufferTicks, ground), Is.False, "1 Tick 早いと消えている");
        }

        [Test]
        public void Slide_LastsExactlySlideTicks_AndIsNotExtended()
        {
            var body = Body();
            Assert.That(body.Step(TestKit.Do(leap: Leap.Slide), _config), Is.EqualTo(RunnerEvents.Slid));
            for (var tick = 2; tick <= _config.SlideTicks; tick++)
            {
                // スライド中のスライドは捨てる（延長しない）。
                body.Step(TestKit.Do(leap: tick == 10 ? Leap.Slide : Leap.None), _config);
                Assert.That(body.Vertical.IsSliding, Is.True, $"Tick {tick}");
                Assert.That(body.LeapBuffer, Is.EqualTo(Leap.None));
            }

            Assert.That(body.Vertical.Height(_config), Is.EqualTo(_config.SlideHeight));
            body.Step(Command.None, _config);
            Assert.That(body.Vertical.IsSliding, Is.False, $"{_config.SlideTicks} Tick で終わる");
            Assert.That(body.Vertical.Height(_config), Is.EqualTo(_config.StandHeight));
        }

        [Test]
        public void JumpWhileSliding_EndsTheSlide()
        {
            var body = Body();
            body.Step(TestKit.Do(leap: Leap.Slide), _config);
            body.Step(Command.None, _config);
            Assert.That(body.Step(TestKit.Do(leap: Leap.Jump), _config), Is.EqualTo(RunnerEvents.Jumped));
            Assert.That(body.Vertical.IsSliding, Is.False);
            Assert.That(body.Vertical.Y, Is.EqualTo(_config.JumpVelocity));
        }

        [Test]
        public void SlideInTheAir_IsBuffered_AndStartsOnTheGround()
        {
            var landing = LandingTick();
            var body = Body();
            body.Step(TestKit.Do(leap: Leap.Jump), _config);
            for (var tick = 2; tick < landing; tick++) { body.Step(Command.None, _config); }
            Assert.That(body.Step(TestKit.Do(leap: Leap.Slide), _config) & RunnerEvents.Landed, Is.EqualTo(RunnerEvents.Landed));
            Assert.That(body.Vertical.IsSliding, Is.False, "着地した Tick はまだ空中の扱い（跳べない・滑れない）");
            Assert.That(body.Step(Command.None, _config), Is.EqualTo(RunnerEvents.Slid));
            Assert.That(body.Vertical.IsSliding, Is.True);
        }

        [Test]
        public void LaneAndVertical_AreIndependent()
        {
            var body = Body();
            var events = body.Step(TestKit.Do(Steer.Left, Leap.Jump), _config);
            Assert.That(events, Is.EqualTo(RunnerEvents.Steered | RunnerEvents.Jumped));
            Assert.That(body.Lane.To, Is.EqualTo(0));
            Assert.That(body.Vertical.Y, Is.GreaterThan(0));
        }

        [Test]
        public void UndefinedCommand_IsRejected()
        {
            Assert.That(() => new Command((Steer)7, Leap.None), Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(() => new Command(Steer.None, (Leap)(-1)), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }

        /// <summary>Tick 1 に跳んで着地する Tick。</summary>
        private static int LandingTick()
        {
            var body = Body();
            body.Step(TestKit.Do(leap: Leap.Jump), _config);
            var tick = 1;
            while (!body.Vertical.IsGrounded)
            {
                body.Step(Command.None, _config);
                tick++;
            }

            return tick;
        }

        /// <summary>Tick 1 に跳び、Tick <paramref name="press"/> に跳ぶキーを押したとき、Tick <paramref name="ground"/> に跳び直すか。</summary>
        private static bool JumpsAgain(int press, int ground)
        {
            var body = Body();
            body.Step(TestKit.Do(leap: Leap.Jump), _config);
            for (var tick = 2; tick <= ground; tick++)
            {
                var events = body.Step(TestKit.Do(leap: tick == press ? Leap.Jump : Leap.None), _config);
                if (tick < ground) { Assert.That(events & RunnerEvents.Jumped, Is.EqualTo(RunnerEvents.None), $"Tick {tick} は空中"); }
                else { return (events & RunnerEvents.Jumped) != 0; }
            }

            return false;
        }
    }
}
