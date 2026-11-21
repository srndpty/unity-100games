using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>接地猶予（コヨーテタイム）と入力バッファ。題材の中心課題なので、境界の Tick を全て確かめる。</summary>
    public sealed class GraceTests
    {
        private static readonly int[] _graceValues = { 0, 1, 6, PrecisionConfig.MaxGraceTicks };

        private static PrecisionConfig Coyote(int ticks) => new() { CoyoteTicks = ticks };

        private static PrecisionConfig Buffer(int ticks) => new() { JumpBufferTicks = ticks };

        /// <summary>右へ歩いて床の端から離れた直後（この Tick の終わりで初めて接地していない）の自機。</summary>
        private static Player WalkOff(PrecisionConfig config, Room room)
        {
            var player = Standing(room, config);
            for (var i = 0; i < 600; i++)
            {
                player.Step(room.Map, Right);
                if (!player.Grounded)
                {
                    return player;
                }
            }

            Assert.Fail("床の端から離れませんでした。");
            return player;
        }

        [Test]
        public void Coyote_JumpAllowedUpToCoyoteTicksAfterLeavingTheLedge_AndNotAfter([ValueSource(nameof(_graceValues))] int coyote)
        {
            var config = Coyote(coyote);
            var room = Ledge(10);
            var leftGround = WalkOff(config, room);
            for (var k = 1; k <= coyote + 2; k++)
            {
                var player = leftGround.Clone();
                Assert.That(Hold(player, room.Map, Right, k - 1), Is.EqualTo(PlayerActions.None), $"k={k}：押す前に跳んではいけない");
                var actions = player.Step(room.Map, In(1, jump: true));
                var expected = k <= coyote;
                Assert.That((actions & PlayerActions.Jumped) != 0, Is.EqualTo(expected), $"空中 {k} Tick 目のジャンプ（猶予 {coyote}）");
                if (expected)
                {
                    Assert.That(player.VelocityY, Is.EqualTo(config.JumpSpeed - config.Gravity));
                }
            }
        }

        [Test]
        public void Coyote_IsSpentByJumping_NoSecondJumpInTheAir()
        {
            var room = Ledge();
            var player = Standing(room);
            Assert.That(player.Step(room.Map, Jump), Is.EqualTo(PlayerActions.Jumped));
            player.Step(room.Map, Idle);
            Assert.That(player.Grounded, Is.False);
            Assert.That(Hold(player, room.Map, Jump, 1), Is.EqualTo(PlayerActions.None), "猶予の間でも、跳んだ後は空中で跳べない");
            Assert.That(player.CoyoteRemaining, Is.Zero);
        }

        [Test]
        public void Coyote_IsSpentByDashing()
        {
            var config = new PrecisionConfig { DashTicks = 1, CoyoteTicks = 6 };
            var room = Ledge(10);
            var leftGround = WalkOff(config, room);

            var withoutDash = leftGround.Clone();
            withoutDash.Step(room.Map, Right);
            Assert.That(withoutDash.Step(room.Map, In(1, jump: true)), Is.EqualTo(PlayerActions.Jumped), "対照：ダッシュしなければ跳べる");

            var dashed = leftGround.Clone();
            Assert.That(dashed.Step(room.Map, In(1, dash: true)), Is.EqualTo(PlayerActions.Dashed));
            Assert.That(dashed.Step(room.Map, In(1, jump: true)), Is.EqualTo(PlayerActions.None), "ダッシュで猶予を失う");
        }

        [Test]
        public void Coyote_TicksDownOnlyInTheAir()
        {
            var config = Coyote(6);
            var room = Ledge(10);
            var player = Standing(room, config);
            Hold(player, room.Map, Idle, 30);
            Assert.That(player.CoyoteRemaining, Is.EqualTo(6), "接地中は猶予を張り直し続ける");
            var leftGround = WalkOff(config, room);
            leftGround.Step(room.Map, Right);
            Assert.That(leftGround.CoyoteRemaining, Is.EqualTo(5));
        }

        /// <summary>空中から落ちて、何 Tick 目の終わりに接地するか（1 始まり）。</summary>
        private static int LandingTick(Player falling)
        {
            var probe = falling.Clone();
            for (var i = 1; i <= 600; i++)
            {
                probe.Step(Open, Idle);
                if (probe.Grounded)
                {
                    return i;
                }
            }

            Assert.Fail("着地しませんでした。");
            return -1;
        }

        [Test]
        public void Buffer_PressJustBeforeLandingFiresOnTheFirstGroundedTick([ValueSource(nameof(_graceValues))] int buffer, [Values(true, false)] bool hold)
        {
            var config = Buffer(buffer);
            var start = Floating(5 * T, 10 * T, config);
            var landing = LandingTick(start);
            Assert.That(landing, Is.GreaterThan(PrecisionConfig.MaxGraceTicks + 2), "境界を全て試せる高さから落とす");
            var firstGrounded = landing + 1;

            for (var early = 0; early <= buffer + 2; early++)
            {
                var player = start.Clone();
                var press = firstGrounded - early;
                var jumpedAt = 0;
                for (var tick = 1; tick <= firstGrounded; tick++)
                {
                    var jump = hold ? tick >= press : tick == press;
                    if ((player.Step(Open, In(jump: jump)) & PlayerActions.Jumped) != 0)
                    {
                        jumpedAt = tick;
                        break;
                    }
                }

                var expected = early <= buffer;
                Assert.That(jumpedAt, Is.EqualTo(expected ? firstGrounded : 0), $"着地の {early} Tick 前に押した（バッファ {buffer}・{(hold ? "押し続け" : "1 Tick だけ")}）");
                if (!expected)
                {
                    Assert.That(Hold(player, Open, In(jump: hold), 60), Is.EqualTo(PlayerActions.None), "押し続けても、新しく押すまで跳ばない");
                }
            }
        }

        [Test]
        public void Buffer_TappedJumpFiresAsShortHop_HeldJumpFiresFull()
        {
            var config = Buffer(6);
            var start = Floating(5 * T, 10 * T, config);
            var firstGrounded = LandingTick(start) + 1;

            var tapped = start.Clone();
            Hold(tapped, Open, Idle, firstGrounded - 4);
            tapped.Step(Open, Jump);
            Hold(tapped, Open, Idle, 2);
            Assert.That(tapped.Step(Open, Idle), Is.EqualTo(PlayerActions.Jumped));
            Assert.That(tapped.VelocityY, Is.EqualTo(config.JumpCutSpeed - config.Gravity), "離した後に発火したら低いジャンプ");

            var held = start.Clone();
            Hold(held, Open, Idle, firstGrounded - 4);
            Assert.That(Hold(held, Open, Jump, 4), Is.EqualTo(PlayerActions.Jumped));
            Assert.That(held.VelocityY, Is.EqualTo(config.JumpSpeed - config.Gravity));
        }

        [Test]
        public void Buffer_FiresOnlyOnce_HoldingThroughLandingDoesNotRejump()
        {
            var room = Ledge();
            var player = Standing(room);
            Assert.That(player.Step(room.Map, Jump), Is.EqualTo(PlayerActions.Jumped));
            Assert.That(player.BufferRemaining, Is.Zero);
            Assert.That(Hold(player, room.Map, Jump, 300), Is.EqualTo(PlayerActions.None), "押したまま着地しても跳び直さない");
        }

        [Test]
        public void Buffer_PressDuringDashWaitsInTheBuffer()
        {
            var config = new PrecisionConfig { DashTicks = 4, JumpBufferTicks = 6 };
            var room = Ledge();
            var player = Standing(room, config);
            Assert.That(player.Step(room.Map, In(1, dash: true)), Is.EqualTo(PlayerActions.Dashed));
            Assert.That(player.Step(room.Map, In(1, jump: true)), Is.EqualTo(PlayerActions.None), "ダッシュ中は跳ばない");
            Assert.That(Hold(player, room.Map, In(1, jump: true), 2), Is.EqualTo(PlayerActions.None));
            Assert.That(player.IsDashing, Is.False);
            Assert.That(player.Step(room.Map, In(1, jump: true)), Is.EqualTo(PlayerActions.Jumped), "ダッシュの後、バッファの中のジャンプが発火する");
        }

        [Test]
        public void Buffer_ExpiresDuringALongDash()
        {
            var config = new PrecisionConfig { DashTicks = 10, JumpBufferTicks = 3 };
            var room = Ledge();
            var player = Standing(room, config);
            player.Step(room.Map, In(1, dash: true));
            player.Step(room.Map, In(1, jump: true));
            Assert.That(Hold(player, room.Map, In(1, jump: true), 20), Is.EqualTo(PlayerActions.None), "バッファより長いダッシュの後は発火しない");
        }
    }
}
