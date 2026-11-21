using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>ダッシュ・壁蹴り・壁ずり。</summary>
    public sealed class DashAndWallTests
    {
        private readonly PrecisionConfig _config = new();

        [Test]
        public void Dash_NeutralGoesFacingDirection_StraightWithoutGravity()
        {
            var player = Floating(5 * T, 6 * T);
            var start = player.Body;
            Assert.That(player.Step(Open, Dash), Is.EqualTo(PlayerActions.Dashed));
            Hold(player, Open, Idle, _config.DashTicks - 1);
            Assert.That(player.IsDashing, Is.False);
            Assert.That(player.Body.X - start.X, Is.EqualTo(_config.DashSpeed * _config.DashTicks));
            Assert.That(player.Body.Y, Is.EqualTo(start.Y), "ダッシュ中は重力が効かない");
            Assert.That(player.VelocityX, Is.EqualTo(_config.DashEndSpeed), "終わりの速さは DashEndSpeed まで落とす");
            Assert.That(player.VelocityY, Is.Zero);
        }

        [Test]
        public void Dash_EightDirections([Values(-1, 0, 1)] int h, [Values(-1, 0, 1)] int v)
        {
            if (h == 0 && v == 0) { Assert.Pass("向きなしは別のテスト"); }
            var player = Floating(20 * T, 6 * T);
            Assert.That(player.Step(Open, In(h, v, dash: true)), Is.EqualTo(PlayerActions.Dashed));
            var speed = h != 0 && v != 0 ? PrecisionConfig.DiagonalOf(_config.DashSpeed) : _config.DashSpeed;
            Assert.That(player.VelocityX, Is.EqualTo(h * speed));
            Assert.That(player.VelocityY, Is.EqualTo(v * speed));
        }

        [Test]
        public void Dash_EightDirectionsMoveEvenAtTheSlowestSpeeds([Values(1, 2, 72)] int dashSpeed)
        {
            // レビュー P3-1：DashSpeed 1 の斜め成分が切り捨てで 0 になり、回数だけ使って動かなかった。
            var config = new PrecisionConfig { DashSpeed = dashSpeed, DashEndSpeed = 0 };
            for (var h = -1; h <= 1; h++)
            {
                for (var v = -1; v <= 1; v++)
                {
                    if (h == 0 && v == 0) { continue; }
                    var player = Floating(20 * T, 6 * T, config);
                    var start = player.Body;
                    Assert.That(player.Step(Open, In(h, v, dash: true)), Is.EqualTo(PlayerActions.Dashed));
                    Assert.That(Math.Sign(player.Body.X - start.X), Is.EqualTo(h), $"({h}, {v}) の横");
                    Assert.That(Math.Sign(player.Body.Y - start.Y), Is.EqualTo(v), $"({h}, {v}) の縦");
                }
            }
        }

        [Test]
        public void Dash_InputDuringDashDoesNotSteer()
        {
            var player = Floating(10 * T, 6 * T);
            player.Step(Open, In(1, dash: true));
            player.Step(Open, In(-1, 1, jump: true));
            Assert.That(player.VelocityX, Is.EqualTo(_config.DashSpeed));
            Assert.That(player.VelocityY, Is.Zero);
        }

        [Test]
        public void Dash_OncePerAirtime_SecondPressIgnored()
        {
            var player = Floating(5 * T, 10 * T);
            player.Step(Open, Dash);
            Assert.That(player.DashesLeft, Is.Zero);
            Hold(player, Open, Idle, _config.DashTicks);
            Assert.That(player.Step(Open, Dash), Is.EqualTo(PlayerActions.None), "空中で 2 回目はできない");
        }

        [Test]
        public void Dash_CountFromConfig()
        {
            var config = new PrecisionConfig { DashCount = 2 };
            var player = Floating(5 * T, 10 * T, config);
            Assert.That(player.DashesLeft, Is.EqualTo(2));
            Assert.That(player.Step(Open, Dash), Is.EqualTo(PlayerActions.Dashed));
            Hold(player, Open, Idle, config.DashTicks);
            Assert.That(player.Step(Open, Dash), Is.EqualTo(PlayerActions.Dashed));
            Hold(player, Open, Idle, config.DashTicks);
            Assert.That(player.Step(Open, Dash), Is.EqualTo(PlayerActions.None));
        }

        [Test]
        public void Dash_CannotRestartWhileDashing()
        {
            var config = new PrecisionConfig { DashCount = 2 };
            var player = Floating(5 * T, 10 * T, config);
            player.Step(Open, Dash);
            player.Step(Open, Idle);
            Assert.That(player.Step(Open, Dash), Is.EqualTo(PlayerActions.None), "ダッシュ中の押下は無視する");
            Assert.That(player.DashesLeft, Is.EqualTo(1));
        }

        [Test]
        public void Dash_HeldKeyAtSpawnDoesNotDash()
        {
            var room = Ledge();
            var player = new Player(_config, room.StartBox);
            Assert.That(Hold(player, room.Map, Dash, 5), Is.EqualTo(PlayerActions.None));
        }

        [Test]
        public void Dash_RefillsOnLanding()
        {
            var player = Floating(5 * T, 6 * T);
            player.Step(Open, Dash);
            Assert.That(player.DashesLeft, Is.Zero);
            for (var i = 0; i < 200 && !player.Grounded; i++) { player.Step(Open, Idle); }
            Assert.That(player.Grounded, Is.True);
            Assert.That(player.DashesLeft, Is.EqualTo(_config.DashCount));
        }

        [Test]
        public void Dash_OnGroundRefillsOnlyAfterTheDashEnds()
        {
            var room = Ledge();
            var player = Standing(room);
            player.Step(room.Map, In(1, dash: true));
            Assert.That(player.Grounded, Is.True);
            Assert.That(player.DashesLeft, Is.Zero, "ダッシュ中は接地していても回復しない");
            Hold(player, room.Map, Idle, _config.DashTicks - 1);
            Assert.That(player.IsDashing, Is.False);
            Assert.That(player.DashesLeft, Is.EqualTo(_config.DashCount));
        }

        [Test]
        public void Dash_StopsAtWall()
        {
            var player = Floating((40 * T) - Player.Width - 50, 6 * T);
            player.Step(Open, In(1, dash: true));
            Assert.That(player.Body.Right, Is.EqualTo(40 * T));
            Assert.That(player.VelocityX, Is.Zero);
        }

        [Test]
        public void WallJump_KicksAwayFromLeftAndRightWalls([Values(-1, 1)] int side)
        {
            var x = side < 0 ? 0 : (40 * T) - Player.Width;
            var player = Floating(x, 6 * T);
            Assert.That(player.Step(Open, Jump), Is.EqualTo(PlayerActions.WallJumped));
            Assert.That(player.VelocityX, Is.EqualTo(-side * _config.WallJumpSpeedX));
            Assert.That(player.VelocityY, Is.EqualTo(_config.WallJumpSpeedY - _config.Gravity));
            Assert.That(player.Facing, Is.EqualTo(-side));
        }

        [Test]
        public void WallJump_ReachBoundary()
        {
            var within = Floating(_config.WallReach - 1, 6 * T);
            Assert.That(within.Step(Open, Jump), Is.EqualTo(PlayerActions.WallJumped), "隙間が WallReach 未満なら届く");
            var beyond = Floating(_config.WallReach, 6 * T);
            Assert.That(beyond.Step(Open, Jump), Is.EqualTo(PlayerActions.None), "隙間が WallReach なら届かない");
        }

        [Test]
        public void WallJump_GroundJumpTakesPriority()
        {
            var room = Ledge();
            var player = new Player(_config, new Box(0, T, Player.Width, Player.Height));
            Hold(player, room.Map, Idle, 2);
            Assert.That(player.Step(room.Map, Jump), Is.EqualTo(PlayerActions.Jumped));
            Assert.That(player.VelocityX, Is.Zero, "壁際でも接地中は真上に跳ぶ");
        }

        [Test]
        public void WallJump_LocksHorizontalInputThenSteers()
        {
            var player = Floating(0, 6 * T);
            player.Step(Open, In(-1, jump: true));
            Hold(player, Open, In(-1, jump: true), _config.WallJumpLockTicks - 1);
            Assert.That(player.VelocityX, Is.EqualTo(_config.WallJumpSpeedX), "固定の間は壁へ戻れない");
            player.Step(Open, In(-1, jump: true));
            Assert.That(player.VelocityX, Is.EqualTo(_config.WallJumpSpeedX - _config.AirAccel), "固定が解けたら入力で減速する");
        }

        [Test]
        public void WallJump_WithoutLockSteersImmediately()
        {
            var config = new PrecisionConfig { WallJumpLockTicks = 0 };
            var player = Floating(0, 6 * T, config);
            player.Step(Open, In(-1, jump: true));
            Assert.That(player.VelocityX, Is.EqualTo(config.WallJumpSpeedX - config.AirAccel));
        }

        [Test]
        public void WallSlide_CapsFallWhilePushingIntoWall()
        {
            var sliding = Floating(0, 10 * T);
            Hold(sliding, Open, Left, 40);
            Assert.That(sliding.WallSliding, Is.True);
            Assert.That(sliding.VelocityY, Is.EqualTo(-_config.WallSlideMaxFall));

            var away = Floating(0, 10 * T);
            Hold(away, Open, Idle, 26);
            Assert.That(away.WallSliding, Is.False);
            Assert.That(away.VelocityY, Is.EqualTo(-_config.MaxFallSpeed), "壁へ押していなければ普通に落ちる");
        }
    }
}
