using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>自機の移動・ジャンプ。</summary>
    public sealed class PlayerTests
    {
        private readonly PrecisionConfig _config = new();

        [Test]
        public void Walk_AcceleratesUpToMoveSpeedAndStopsWithFriction()
        {
            var room = Ledge();
            var player = Standing(room);
            player.Step(room.Map, Right);
            Assert.That(player.VelocityX, Is.EqualTo(_config.GroundAccel));
            Hold(player, room.Map, Right, 30);
            Assert.That(player.VelocityX, Is.EqualTo(_config.MoveSpeed));
            player.Step(room.Map, Idle);
            Assert.That(player.VelocityX, Is.EqualTo(_config.MoveSpeed - _config.GroundDecel));
            Hold(player, room.Map, Idle, 30);
            Assert.That(player.VelocityX, Is.Zero);
        }

        [Test]
        public void Facing_FollowsHorizontalInput()
        {
            var room = Ledge();
            var player = Standing(room);
            Assert.That(player.Facing, Is.EqualTo(1));
            player.Step(room.Map, Left);
            Assert.That(player.Facing, Is.EqualTo(-1));
            player.Step(room.Map, Idle);
            Assert.That(player.Facing, Is.EqualTo(-1), "入力がなければ向きを保つ");
        }

        [Test]
        public void Jump_OnlyOnPressFromGround_HeldKeyAtSpawnDoesNotJump()
        {
            var room = Ledge();
            var player = new Player(_config, room.StartBox);
            Assert.That(Hold(player, room.Map, Jump, 10), Is.EqualTo(PlayerActions.None), "開始時に押したままのキーでは跳ばない");
            player.Step(room.Map, Idle);
            Assert.That(player.Step(room.Map, Jump), Is.EqualTo(PlayerActions.Jumped));
            Assert.That(player.Grounded, Is.False);
        }

        [Test]
        public void Jump_HeightDependsOnHold()
        {
            int Apex(int holdTicks)
            {
                var room = Ledge();
                var player = Standing(room);
                var start = player.Body.Bottom;
                var best = start;
                for (var i = 0; i < 200; i++)
                {
                    player.Step(room.Map, In(jump: i < holdTicks));
                    best = Math.Max(best, player.Body.Bottom);
                }

                return best - start;
            }

            var full = Apex(200);
            var shortHop = Apex(1);
            Assert.That(full, Is.GreaterThan(3 * T).And.LessThan(4 * T), "押し続けると 3〜4 タイル");
            Assert.That(shortHop, Is.LessThan(T), "すぐ離すと 1 タイル未満");
        }

        [Test]
        public void Fall_IsCappedAtMaxFallSpeed()
        {
            var player = Floating(5 * T, 10 * T);
            Hold(player, Open, Idle, 30);
            Assert.That(player.VelocityY, Is.EqualTo(-_config.MaxFallSpeed));
        }

        [Test]
        public void Step_RejectsNullMap()
        {
            var player = Floating(5 * T, 10 * T);
            Assert.Throws<ArgumentNullException>(() => player.Step(null!, Idle));
        }

        [Test]
        public void Input_RejectsOutOfRangeAxes()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PlayerInput(2, 0, false, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PlayerInput(0, -2, false, false));
        }

        [Test]
        public void Player_RejectsWrongSize()
        {
            Assert.Throws<ArgumentException>(() => _ = new Player(_config, new Box(0, 0, T, T)));
        }

        [Test]
        public void Clone_IsIndependentIncludingTimers()
        {
            var room = Ledge();
            var player = Standing(room);
            var copy = player.Clone();
            player.Step(room.Map, Jump);
            Assert.That(copy.Grounded, Is.True);
            Assert.That(copy.CoyoteRemaining, Is.EqualTo(_config.CoyoteTicks));
            Assert.That(copy.Step(room.Map, Jump), Is.EqualTo(PlayerActions.Jumped), "複製は元と独立に跳べる");
        }
    }
}
