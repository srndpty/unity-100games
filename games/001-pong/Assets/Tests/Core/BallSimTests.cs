using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class BallSimTests
    {
        private const float Tolerance = 1e-4f;

        private PongConfig _config = null!;
        private Paddle _left = null!;
        private Paddle _right = null!;
        private BallSim _ball = null!;
        private List<MatchEvent> _events = null!;

        [SetUp]
        public void SetUp()
        {
            _config = new PongConfig();
            _left = new Paddle(_config, Side.Left);
            _right = new Paddle(_config, Side.Right);
            _ball = new BallSim(_config);
            _events = new List<MatchEvent>();
        }

        [Test]
        public void CenterHit_ReflectsStraightBack()
        {
            var velocity = HitLeftPaddleAt(offset: 0f);

            Assert.That(velocity.X, Is.GreaterThan(0f));
            Assert.That(velocity.Y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(_events, Is.EqualTo(new[] { new MatchEvent(MatchEventKind.PaddleHit, Side.Left) }));
        }

        [Test]
        public void EdgeHit_ReflectsAtSteeperAngleThanCenterHit()
        {
            var center = HitLeftPaddleAt(offset: 0f);
            var nearEdge = HitLeftPaddleAt(offset: 0.7f);
            var upperEdge = HitLeftPaddleAt(offset: 1f);
            var lowerEdge = HitLeftPaddleAt(offset: -1f);

            Assert.That(Angle(nearEdge), Is.GreaterThan(Angle(center)));
            Assert.That(Angle(upperEdge), Is.GreaterThan(Angle(nearEdge)));
            Assert.That(Angle(upperEdge), Is.EqualTo(_config.MaxBounceAngleDegrees).Within(0.01f));
            Assert.That(upperEdge.Y, Is.GreaterThan(0f), "上端で打てば上へ");
            Assert.That(lowerEdge.Y, Is.LessThan(0f), "下端で打てば下へ");
        }

        [Test]
        public void PaddleHit_SpeedsUpUntilMaximum()
        {
            var once = HitLeftPaddleAt(offset: 0f, speed: _config.ServeSpeed);
            var capped = HitLeftPaddleAt(offset: 0f, speed: _config.MaxBallSpeed);

            Assert.That(once.Length, Is.EqualTo(_config.ServeSpeed + _config.SpeedIncreasePerHit).Within(Tolerance));
            Assert.That(capped.Length, Is.EqualTo(_config.MaxBallSpeed).Within(Tolerance));
        }

        [Test]
        public void BallMissingPaddleVertically_PassesThrough()
        {
            var y = _left.HalfHeight + _ball.HalfSize + 0.01f;
            _ball.Launch(new Vec2(_left.FaceX + 0.2f, y), new Vec2(-_config.ServeSpeed, 0f));

            StepTicks(10);

            Assert.That(_ball.Velocity.X, Is.LessThan(0f));
            Assert.That(_events, Is.Empty);
        }

        [Test]
        public void BallMovingAwayFromPaddle_DoesNotBounceEvenWhenOverlapping()
        {
            // パドルの裏から内側へ抜けていくボール（進行方向がパドルから離れる）
            _ball.Launch(new Vec2(_left.X, 0f), new Vec2(_config.ServeSpeed, 0f));

            StepTicks(1);

            Assert.That(_ball.Velocity.X, Is.GreaterThan(0f));
            Assert.That(_events, Is.Empty);
        }

        [Test]
        public void BallBehindPaddle_IsNotReturned()
        {
            // 面を越えてしまったボールはそのままゴールへ向かう
            _ball.Launch(new Vec2(_left.X - 0.2f, 0f), new Vec2(-_config.ServeSpeed, 0f));

            StepTicks(1);

            Assert.That(_ball.Velocity.X, Is.LessThan(0f));
            Assert.That(_events, Is.Empty);
        }

        [Test]
        public void VeryFastBall_DoesNotTunnelThroughPaddle()
        {
            // 1 Tick でパドル幅の何十倍も進む速度でも、面を越える瞬間を検出する
            var speed = _config.PaddleWidth * 40f * _config.TicksPerSecond;
            _ball.Launch(new Vec2(_left.FaceX + 1f, 0f), new Vec2(-speed, 0f));

            StepTicks(1);

            Assert.That(_ball.Velocity.X, Is.GreaterThan(0f));
            Assert.That(_ball.Position.X, Is.GreaterThan(_left.FaceX));
        }

        [Test]
        public void Wall_ReflectsVerticalVelocityAndKeepsBallInside()
        {
            var wallY = (_config.FieldHeight / 2f) - _ball.HalfSize;
            _ball.Launch(new Vec2(0f, wallY - 0.01f), new Vec2(1f, 6f));

            StepTicks(1);

            Assert.That(_ball.Velocity.Y, Is.EqualTo(-6f));
            Assert.That(_ball.Position.Y, Is.LessThanOrEqualTo(wallY));
            Assert.That(_events, Is.EqualTo(new[] { new MatchEvent(MatchEventKind.WallHit, default) }));
        }

        [Test]
        public void GoalReached_OnlyAfterBallFullyCrossesLine()
        {
            var goalX = _config.FieldWidth / 2f;

            _ball.Launch(new Vec2(-goalX, 0f), Vec2.Zero);
            Assert.That(_ball.GoalReached(), Is.Null, "ライン上はまだ得点ではない");

            _ball.Launch(new Vec2(-goalX - _ball.HalfSize - 0.01f, 0f), Vec2.Zero);
            Assert.That(_ball.GoalReached(), Is.EqualTo(Side.Left));

            _ball.Launch(new Vec2(goalX + _ball.HalfSize + 0.01f, 0f), Vec2.Zero);
            Assert.That(_ball.GoalReached(), Is.EqualTo(Side.Right));
        }

        [Test]
        public void BallNotInPlay_DoesNotMove()
        {
            _ball.ResetToCenter();

            StepTicks(10);

            Assert.That(_ball.Position, Is.EqualTo(Vec2.Zero));
            Assert.That(_events, Is.Empty);
        }

        // 以下、動くパドル。1 Tick で 0.15 動き、届く範囲はパドル中心から ±0.925。
        // Tick 終了時の位置だけで判定すると結果が変わる高さを選んでいる。

        [Test]
        public void RisingPaddle_IsJudgedAtItsPositionWhenBallCrosses()
        {
            // 上昇中（0 → 0.15）。y = 0.95 のボールは、Tick の序盤に面を越えると届かず、終盤なら当たる。
            Assert.That(CrossLeftFaceWhileMoving(fromY: 0f, input: 1f, ballY: 0.95f, crossFraction: 0.1f), Is.False);
            Assert.That(CrossLeftFaceWhileMoving(fromY: 0f, input: 1f, ballY: 0.95f, crossFraction: 0.9f), Is.True);
        }

        [Test]
        public void FallingPaddle_IsJudgedAtItsPositionWhenBallCrosses()
        {
            // 下降中（0.15 → 0）。y = 1.05 のボールは、序盤なら当たり、終盤には届かない。
            Assert.That(CrossLeftFaceWhileMoving(fromY: 0.15f, input: -1f, ballY: 1.05f, crossFraction: 0.1f), Is.True);
            Assert.That(CrossLeftFaceWhileMoving(fromY: 0.15f, input: -1f, ballY: 1.05f, crossFraction: 0.9f), Is.False);
        }

        [Test]
        public void PaddleRightAfterReversing_StartsFromWhereThePreviousTickEnded()
        {
            _left.SetY(0f);
            _left.Move(1f, _config.TickDuration);
            _left.Move(-1f, _config.TickDuration);

            Assert.That(_left.PreviousY, Is.EqualTo(0.15f).Within(Tolerance));
            Assert.That(_left.Y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(LaunchAcrossLeftFace(ballY: 1.05f, crossFraction: 0.1f), Is.True, "Tick 序盤はまだ上（0.135）にいる");
        }

        [Test]
        public void MovingPaddle_BounceAngleUsesPaddlePositionAtContact()
        {
            // 上昇中のパドルの、接触時刻（Tick の中間）での中心（0.075）に当てると水平に返る。
            _left.SetY(0f);
            _left.Move(1f, _config.TickDuration);

            Assert.That(LaunchAcrossLeftFace(ballY: 0.075f, crossFraction: 0.5f), Is.True);
            Assert.That(_ball.Velocity.Y, Is.EqualTo(0f).Within(Tolerance));
        }

        // 左パドルを fromY から input の向きに 1 Tick 動かしながら、ボールを Tick 中の crossFraction の時刻に面へ届かせる。当たったら true。
        private bool CrossLeftFaceWhileMoving(float fromY, float input, float ballY, float crossFraction)
        {
            _left.SetY(fromY);
            _left.Move(input, _config.TickDuration);
            return LaunchAcrossLeftFace(ballY, crossFraction);
        }

        // 水平に飛ぶボールが、1 Tick のうち crossFraction の時刻に左パドルの面（の接触位置）を越えるように打ち出し、1 Tick 進める。
        private bool LaunchAcrossLeftFace(float ballY, float crossFraction)
        {
            _events.Clear();
            const float Speed = 10f;
            var contactX = _left.FaceX + _ball.HalfSize;
            var perTick = Speed * _config.TickDuration;
            _ball.Launch(new Vec2(contactX + (crossFraction * perTick), ballY), new Vec2(-Speed, 0f));
            _ball.Step(_config.TickDuration, _left, _right, _events);
            return _events.Contains(new MatchEvent(MatchEventKind.PaddleHit, Side.Left));
        }

        // 左パドルの中心から offset（-1 = 下端、+1 = 上端）の高さに水平に当て、反射後の速度を返す。
        private Vec2 HitLeftPaddleAt(float offset, float? speed = null)
        {
            _events.Clear();
            var y = offset * (_left.HalfHeight + _ball.HalfSize);
            _ball.Launch(new Vec2(_left.FaceX + _ball.HalfSize + 0.05f, y), new Vec2(-(speed ?? _config.ServeSpeed), 0f));
            StepTicks(1);
            return _ball.Velocity;
        }

        private void StepTicks(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _ball.Step(_config.TickDuration, _left, _right, _events);
            }
        }

        private static float Angle(Vec2 velocity) => MathF.Abs(MathF.Atan2(velocity.Y, MathF.Abs(velocity.X))) * 180f / MathF.PI;
    }
}
