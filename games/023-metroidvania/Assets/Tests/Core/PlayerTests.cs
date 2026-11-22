using System;
using System.Collections.Generic;
using NUnit.Framework;
using TileMotion;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>自機の移動・ジャンプ・二段ジャンプ・被弾。</summary>
    public sealed class PlayerTests
    {
        /// <summary>幅 20・高さ 12 の平らな部屋。床は最下段、自機は列 2。列 12〜19 は床がなく下の外へ抜ける。</summary>
        private static readonly Room _flat = Flat();

        private static Room Flat()
        {
            var rows = new List<string>();
            rows.Add(new string('#', 20));
            for (var i = 0; i < 9; i++) { rows.Add("#" + new string('.', 18) + "#"); }
            rows.Add("#.S" + new string('.', 16) + "#");
            rows.Add(new string('#', 12) + new string('.', 7) + "#");
            return Map(0, 0, rows.ToArray());
        }

        private static Terrain TerrainOf(Room room) => new(room.Map, new bool[room.Map.Width * room.Map.Height]);

        /// <summary>床に立ち、キーを離した状態の自機。</summary>
        private static Player Standing(ExplorerConfig? config = null)
        {
            var player = new Player(config ?? Config(), Room.PlaceOnTile(_flat.Start!.Value, Player.Width, Player.Height));
            var terrain = TerrainOf(_flat);
            player.Step(terrain, Idle, Ability.None, false);
            player.Step(terrain, Idle, Ability.None, false);
            return player;
        }

        private static PlayerActions Step(Player player, PlayerInput input, Ability abilities = Ability.None)
        {
            return player.Step(TerrainOf(_flat), input, abilities, true);
        }

        /// <summary>地上で跳び、頂点でキーを離して押し直す。最高の足の高さと、二段ジャンプが起きたかを返す。</summary>
        private static (int Peak, bool Doubled) JumpAndRepress(Ability abilities)
        {
            var player = Standing();
            var floor = player.Body.Bottom;
            var peak = 0;
            var doubled = false;
            var phase = 0;
            for (var tick = 0; tick < 300; tick++)
            {
                var jump = phase != 1;
                var actions = Step(player, In(jump: jump), abilities);
                doubled |= (actions & PlayerActions.DoubleJumped) != 0;
                peak = Math.Max(peak, player.Body.Bottom - floor);
                if (phase == 0 && tick > 0 && player.VelocityY <= 0) { phase = 1; }
                else if (phase == 1) { phase = 2; }
                if (tick > 0 && player.Grounded) { break; }
            }

            return (peak, doubled);
        }

        [Test]
        public void DoubleJump_OnlyWithTheAbility()
        {
            var without = JumpAndRepress(Ability.None);
            var with = JumpAndRepress(Ability.DoubleJump);
            Assert.That(without.Doubled, Is.False, "能力がなければ空中の押下は跳ばない");
            Assert.That(with.Doubled, Is.True);
            Assert.That(without.Peak, Is.EqualTo(JumpReach.Measure(Config(), false)), "設定の検証と同じ高さ");
            Assert.That(with.Peak, Is.EqualTo(JumpReach.Measure(Config(), true)));
            Assert.That(without.Peak, Is.LessThan(Rooms.GateRise * T));
            Assert.That(with.Peak, Is.GreaterThanOrEqualTo(Rooms.GateRise * T));
        }

        [Test]
        public void DoubleJump_OncePerAirtime_RestoredOnLanding()
        {
            var player = Standing();
            var doubles = 0;
            Assert.That(Step(player, Jump, Ability.DoubleJump), Is.EqualTo(PlayerActions.Jumped));
            for (var tick = 0; tick < 200 && !(tick > 2 && player.Grounded); tick++)
            {
                // 2 Tick ごとに押し直す（空中で何度押しても二段ジャンプは 1 回）。
                var actions = Step(player, In(jump: tick % 2 == 0), Ability.DoubleJump);
                if ((actions & PlayerActions.DoubleJumped) != 0) { doubles++; }
                if ((actions & PlayerActions.Jumped) != 0) { break; }
            }

            Assert.That(doubles, Is.EqualTo(1));
            Assert.That(player.AirJumpUsed, Is.False, "着地で戻る");
        }

        [Test]
        public void Buffer_WithoutDoubleJump_FiresOnLanding()
        {
            var player = Standing();
            Step(player, Jump);
            while (player.VelocityY > -40) { Step(player, Jump); }
            Step(player, Idle);

            // 落ちている途中で押す。二段ジャンプがないので、着地までバッファで待つ（猶予の Tick 数より前に押せば消える）。
            var ticksToLand = 0;
            var probe = player.Clone();
            while (!probe.Grounded) { probe.Step(TerrainOf(_flat), Idle, Ability.None, true); ticksToLand++; }
            for (var i = 0; i < ticksToLand - Config().JumpBufferTicks; i++) { Step(player, Idle); }
            var jumped = false;
            for (var i = 0; i < 12; i++)
            {
                jumped |= (Step(player, i == 0 ? Jump : Idle) & PlayerActions.Jumped) != 0;
            }

            Assert.That(jumped, Is.True, "着地の直後に跳ぶ");
        }

        [Test]
        public void Coyote_GroundJumpJustAfterLeavingTheLedge_KeepsTheDoubleJump()
        {
            var player = Standing();

            // 右へ歩いて床の端（列 12）から落ちる。
            while (player.Grounded || player.Body.Left < 11 * T) { Step(player, Right, Ability.DoubleJump); }
            Assert.That(player.Grounded, Is.False);
            var actions = Step(player, In(1, jump: true), Ability.DoubleJump);
            Assert.That(actions, Is.EqualTo(PlayerActions.Jumped), "猶予の間は地上のジャンプ");
            Assert.That(player.AirJumpUsed, Is.False, "二段ジャンプは残っている");
        }

        [Test]
        public void HeldKeys_AtStart_DoNotJumpOrShootUntilReleased()
        {
            var player = new Player(Config(), Room.PlaceOnTile(_flat.Start!.Value, Player.Width, Player.Height));
            for (var i = 0; i < 10; i++)
            {
                Assert.That(Step(player, In(jump: true, shoot: true)), Is.EqualTo(PlayerActions.None));
            }

            Step(player, Idle);
            Assert.That(Step(player, In(jump: true, shoot: true)), Is.EqualTo(PlayerActions.Jumped | PlayerActions.Shot));
        }

        [Test]
        public void Hurt_InvincibleForExactlyTheConfiguredTicks()
        {
            var config = Config();
            var player = Standing(config);
            Assert.That(player.Hurt(1, 1), Is.True);
            Assert.That(player.Health, Is.EqualTo(config.MaxHealth - 1));
            Assert.That(player.Hurt(1, 1), Is.False, "同じ Tick の 2 回目は無効");
            for (var k = 1; k <= config.InvincibleTicks; k++)
            {
                Step(player, Idle);
                Assert.That(player.Clone().Hurt(1, 1), Is.False, $"被弾の {k} Tick 後は無敵");
            }

            Step(player, Idle);
            Assert.That(player.Hurt(1, 1), Is.True, $"{config.InvincibleTicks + 1} Tick 後は当たる");
        }

        [Test]
        public void Hurt_KnocksBackAndIgnoresSteeringForAWhile()
        {
            var config = Config();
            var player = Standing(config);
            player.Hurt(1, 1);
            Assert.That(player.VelocityX, Is.EqualTo(config.KnockbackSpeedX));
            for (var i = 0; i < config.KnockbackTicks; i++)
            {
                Step(player, Left);
                Assert.That(player.VelocityX, Is.EqualTo(config.KnockbackSpeedX), "ノックバック中は左を押しても速さが変わらない");
            }

            Step(player, Left);
            Assert.That(player.VelocityX, Is.LessThan(config.KnockbackSpeedX), "終わると入力が効く");
        }

        [Test]
        public void Hurt_DownAtZeroAndRejectsBadDamage()
        {
            var config = Config();
            config.MaxHealth = 2;
            var player = Standing(config);
            player.Hurt(5, 1);
            Assert.That(player.Health, Is.Zero, "0 より下へは減らない");
            Assert.That(player.IsDown, Is.True);
            Assert.That(player.Hurt(1, 1), Is.False, "倒れた後は受けない");
            Assert.Throws<ArgumentOutOfRangeException>(() => player.Hurt(0, 1));
        }

        [Test]
        public void Heal_CapsAtMaximum()
        {
            var config = Config();
            var player = Standing(config);
            player.Hurt(3, 1);
            Assert.That(player.Heal(2), Is.EqualTo(2));
            Assert.That(player.Heal(5), Is.EqualTo(1), "最大まで");
            Assert.That(player.Health, Is.EqualTo(config.MaxHealth));
            Assert.Throws<ArgumentOutOfRangeException>(() => player.Heal(0));
        }

        [Test]
        public void Teleport_KeepsHealthAndHeldKeys_DropsVelocity()
        {
            var player = Standing();
            player.Hurt(1, 1);
            Step(player, In(1, jump: true));
            player.Teleport(new Box(5 * T, T, Player.Width, Player.Height));
            Assert.That(player.VelocityX, Is.Zero);
            Assert.That(player.VelocityY, Is.Zero);
            Assert.That(player.Health, Is.EqualTo(Config().MaxHealth - 1));
            Assert.That(Step(player, In(jump: true)), Is.EqualTo(PlayerActions.None), "押したままのジャンプは移った先で跳ばない");
            Assert.Throws<ArgumentException>(() => player.Teleport(new Box(0, 0, 1, 1)));
        }

        [Test]
        public void Input_RejectsUndefinedDirections()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PlayerInput(2, false, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PlayerInput(-2, false, false));
        }
    }
}
