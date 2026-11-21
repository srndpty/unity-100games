using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>自機の速度ベースの制御とジャンプ。</summary>
    public sealed class PlayerTests
    {
        private readonly PlatformerConfig _config = new();

        [Test]
        public void Walk_AcceleratesUpToWalkSpeedAndStays()
        {
            var level = Flat();
            var player = StandingPlayer(level);
            player.Step(level.Map, Right);
            Assert.That(player.VelocityX, Is.EqualTo(_config.GroundAccel));
            Hold(player, level.Map, Right, 60);
            Assert.That(player.VelocityX, Is.EqualTo(_config.WalkSpeed));
        }

        [Test]
        public void Run_ReachesRunSpeedAndSlowsGraduallyWhenReleased()
        {
            var level = Flat(200);
            var player = StandingPlayer(level);
            Hold(player, level.Map, RunRight, 60);
            Assert.That(player.VelocityX, Is.EqualTo(_config.RunSpeed));

            player.Step(level.Map, Right);
            Assert.That(player.VelocityX, Is.EqualTo(_config.RunSpeed - _config.Friction), "走りをやめても急には落とさない");
            Hold(player, level.Map, Right, 60);
            Assert.That(player.VelocityX, Is.EqualTo(_config.WalkSpeed));
        }

        [Test]
        public void Release_FrictionStopsOnGround()
        {
            var level = Flat();
            var player = StandingPlayer(level);
            Hold(player, level.Map, Right, 60);
            player.Step(level.Map, Idle);
            Assert.That(player.VelocityX, Is.EqualTo(_config.WalkSpeed - _config.Friction));
            Hold(player, level.Map, Idle, 60);
            Assert.That(player.VelocityX, Is.Zero);
        }

        [Test]
        public void OppositeInput_SkidsThenTurns()
        {
            var level = Flat();
            var player = StandingPlayer(level);
            Hold(player, level.Map, Right, 60);
            player.Step(level.Map, Left);
            Assert.That(player.VelocityX, Is.EqualTo(_config.WalkSpeed - _config.SkidDecel));
            Assert.That(player.Facing, Is.EqualTo(-1));
            Hold(player, level.Map, Left, 60);
            Assert.That(player.VelocityX, Is.EqualTo(-_config.WalkSpeed));
        }

        [Test]
        public void Air_KeepsMomentumWithoutInput()
        {
            var level = Flat();
            var player = StandingPlayer(level);
            Hold(player, level.Map, Right, 60);
            player.Step(level.Map, Idle);
            player.Step(level.Map, RightJump);
            var vx = player.VelocityX;
            player.Step(level.Map, Jump);
            Assert.That(player.Grounded, Is.False);
            Assert.That(player.VelocityX, Is.EqualTo(vx), "空中では摩擦で減速しない");
        }

        [Test]
        public void Jump_OnlyFromGroundOnPress()
        {
            var level = Flat();
            var player = StandingPlayer(level);
            Assert.That(player.Grounded, Is.True);
            Assert.That(player.Step(level.Map, Jump), Is.True);
            Assert.That(player.VelocityY, Is.EqualTo(_config.JumpSpeed - _config.Gravity));
        }

        [Test]
        public void Jump_NotInAir_EvenWhenPressedAgain()
        {
            var level = Flat();
            var player = StandingPlayer(level);
            player.Step(level.Map, Jump);
            var presses = 0;
            for (var i = 0; i < 10; i++)
            {
                player.Step(level.Map, Idle);
                if (player.Grounded) { break; }
                Assert.That(player.Step(level.Map, Jump), Is.False, $"空中で押し直した {i} 回目");
                presses++;
            }

            Assert.That(presses, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void Jump_HeldThroughLandingDoesNotJumpAgain()
        {
            var level = Flat();
            var player = StandingPlayer(level);
            var jumps = 0;
            for (var i = 0; i < 300; i++)
            {
                if (player.Step(level.Map, Jump)) { jumps++; }
            }

            Assert.That(jumps, Is.EqualTo(1));
            Assert.That(player.Grounded, Is.True);

            player.Step(level.Map, Idle);
            Assert.That(player.Step(level.Map, Jump), Is.True, "離して押し直せば跳ぶ");
        }

        [Test]
        public void Jump_HeldFromCreationDoesNotJumpUntilReleased()
        {
            var level = Flat();
            var player = new Player(_config, Level.PlaceOnTile(level.StartColumn, level.StartRow, Player.Width, Player.Height));
            for (var i = 0; i < 5; i++)
            {
                Assert.That(player.Step(level.Map, Jump), Is.False);
            }

            player.Step(level.Map, Idle);
            Assert.That(player.Step(level.Map, Jump), Is.True);
        }

        [Test]
        public void Jump_WalkingOffLedgeCannotJumpInAir()
        {
            var level = Map(
                "G.........",
                "..........",
                "S.........",
                "###.......",
                "##########");
            var player = StandingPlayer(level);
            var left = false;
            for (var i = 0; i < 120 && !left; i++)
            {
                player.Step(level.Map, Right);
                left = !player.Grounded;
            }

            Assert.That(left, Is.True, "足場から落ちた");
            Assert.That(player.Step(level.Map, RightJump), Is.False);
            Assert.That(player.VelocityY, Is.LessThan(0));
        }

        [Test]
        public void VariableJump_TapIsLowerThanHold()
        {
            var level = Flat();
            var full = Apex(level, 200);
            var tap = Apex(level, 1);
            Assert.That(tap, Is.LessThan(full / 2));
            Assert.That(full, Is.InRange(4 * T, 5 * T), "既定で 4〜5 タイル跳べる");
        }

        [Test]
        public void VariableJump_ReleaseCutsSpeedOnlyWhileRising()
        {
            var level = Flat();
            var player = StandingPlayer(level);
            player.Step(level.Map, Jump);
            player.Step(level.Map, Idle);
            Assert.That(player.VelocityY, Is.EqualTo(_config.JumpCutSpeed - _config.Gravity));
        }

        [Test]
        public void Ceiling_StopsRise()
        {
            var level = Map(
                "G....",
                "####.",
                ".....",
                "S....",
                "#####");
            var player = StandingPlayer(level);
            Hold(player, level.Map, Jump, 10);
            Assert.That(player.Body.Top, Is.LessThanOrEqualTo(3 * T));
            Hold(player, level.Map, Jump, 60);
            Assert.That(player.Grounded, Is.True);
            Assert.That(player.Body.Bottom, Is.EqualTo(T));
        }

        [Test]
        public void Wall_StopsHorizontalSpeed()
        {
            var level = Map(
                "G....",
                "S..#.",
                "#####");
            var player = StandingPlayer(level);
            Hold(player, level.Map, Right, 120);
            Assert.That(player.Body.Right, Is.EqualTo(3 * T));
            Assert.That(player.VelocityX, Is.Zero);
        }

        [Test]
        public void Fall_IsCappedAtMaxFallSpeed()
        {
            var rows = new string[30];
            rows[0] = "G..";
            rows[1] = ".S.";
            for (var i = 2; i < rows.Length - 1; i++) { rows[i] = "..."; }
            rows[rows.Length - 1] = "###";
            var level = Map(rows);
            var player = new Player(_config, Level.PlaceOnTile(1, level.StartRow, Player.Width, Player.Height));
            Hold(player, level.Map, Idle, 60);
            Assert.That(player.VelocityY, Is.EqualTo(-_config.MaxFallSpeed));
        }

        /// <summary>落下速度の上限が最大でも、1 タイルの薄い床をすり抜けない（どの高さから落ちても）。</summary>
        [Test]
        public void Fall_AtMaximumSpeedNeverTunnelsThroughThinFloor()
        {
            var config = new PlatformerConfig
            {
                Gravity = PlatformerConfig.MaxSpeed,
                JumpSpeed = PlatformerConfig.MaxSpeed,
                MaxFallSpeed = PlatformerConfig.MaxSpeed,
                HopperJumpSpeed = PlatformerConfig.MaxSpeed,
            };
            config.Validate();
            var level = Map(
                "G..",
                "...",
                "...",
                "...",
                "...",
                ".S.",
                "...",
                "###",
                "...",
                "...");
            for (var offset = 0; offset < T; offset += 7)
            {
                var player = new Player(config, Level.PlaceOnTile(1, 4, Player.Width, Player.Height).Offset(0, offset));
                Hold(player, level.Map, Idle, 30);
                Assert.That(player.Body.Bottom, Is.EqualTo(3 * T), $"高さのずれ {offset}");
                Assert.That(player.Grounded, Is.True);
            }
        }

        [Test]
        public void Input_RejectsOutOfRangeHorizontal()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PlayerInput(2, false, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PlayerInput(-2, false, false));
        }

        [Test]
        public void Constructor_RejectsWrongBodySize()
        {
            Assert.Throws<ArgumentException>(() => _ = new Player(_config, new Box(0, 0, T, T)));
        }
    }
}
