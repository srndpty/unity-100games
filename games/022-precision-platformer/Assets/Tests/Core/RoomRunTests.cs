using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>1 部屋の挑戦：死亡と即時リトライ・出口・終了後の入力。</summary>
    public sealed class RoomRunTests
    {
        /// <summary>床の右側がトゲの部屋。右へ跳ぶと必ずトゲに落ちる。出口は左上の隅（届かない）。</summary>
        private static Room SpikeFloor() => Map(
            "E...........",
            "............",
            "............",
            "............",
            "............",
            "............",
            "............",
            ".S..........",
            "##^^^^^^^^^^");

        private static Room Pit() => Map(
            "E.....",
            "S.....",
            "#.....");

        private static Room Corridor() => Map(
            "......",
            "S...E.",
            "######");

        /// <summary>空中でダッシュを使い切ってから、トゲで死亡するまで進める。</summary>
        private static RoomRun DieAfterDashing(PrecisionConfig config)
        {
            var run = Run(SpikeFloor(), config);
            run.Tick(Idle);
            Hold(run, In(1, jump: true), 4);
            for (var i = 0; i < config.DashCount; i++)
            {
                run.Tick(In(1, dash: true));
                Hold(run, In(1), config.DashTicks);
            }

            Assert.That(run.Player.DashesLeft, Is.Zero, "死亡の前にダッシュを使い切っている");
            for (var i = 0; i < 600 && run.Status == RunStatus.Playing; i++)
            {
                run.Tick(In(1, jump: true, dash: true));
            }

            Assert.That(run.Status, Is.EqualTo(RunStatus.Dying));
            return run;
        }

        [Test]
        public void Death_RestoresDashCountOnRespawn([Values(1, 2, 3)] int dashCount)
        {
            var config = new PrecisionConfig { DashCount = dashCount };
            var run = DieAfterDashing(config);
            Assert.That(run.LastDeath, Is.EqualTo(DeathCause.Spike));
            Assert.That(run.Deaths, Is.EqualTo(1));
            Hold(run, Idle, config.DeathTicks);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Playing));
            Assert.That(run.Player.DashesLeft, Is.EqualTo(dashCount), "死亡後はダッシュ回数が最大に戻る");
            Assert.That(run.Player.Body, Is.EqualTo(run.Room.StartBox));
            Assert.That(run.Player.VelocityX, Is.Zero);
            Assert.That(run.Player.VelocityY, Is.Zero);
            Assert.That(run.Player.BufferRemaining, Is.Zero);
        }

        [Test]
        public void Death_RespawnsAfterExactlyDeathTicks_IgnoringInput([Values(1, 18, PrecisionConfig.MaxDeathTicks)] int deathTicks)
        {
            var config = new PrecisionConfig { DeathTicks = deathTicks };
            var run = DieAfterDashing(config);
            Assert.That(run.TakeEvents() & RunEventKind.Died, Is.EqualTo(RunEventKind.Died));
            var body = run.Player.Body;
            var dashes = run.Player.DashesLeft;
            Hold(run, In(-1, 1, true, true), deathTicks - 1);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Dying), "演出の間は戻らない");
            Assert.That(run.Player.Body, Is.EqualTo(body), "演出中の入力で状態は変わらない");
            Assert.That(run.Player.DashesLeft, Is.EqualTo(dashes));
            Assert.That(run.Deaths, Is.EqualTo(1), "演出中に重ねて死亡しない");
            run.Tick(In(-1, 1, true, true));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Playing));
            Assert.That(run.TakeEvents(), Is.EqualTo(RunEventKind.Respawned));
        }

        [Test]
        public void Respawn_KeysHeldFromBeforeDeathMustBeReleased()
        {
            var config = new PrecisionConfig();
            var run = DieAfterDashing(config);
            Hold(run, In(1, jump: true, dash: true), config.DeathTicks);
            run.TakeEvents();
            Hold(run, In(0, jump: true, dash: true), 10);
            Assert.That(run.TakeEvents(), Is.EqualTo(RunEventKind.None), "押したままのジャンプ・ダッシュでは動かない");
            run.Tick(Idle);
            run.Tick(In(jump: true));
            Assert.That(run.TakeEvents(), Is.EqualTo(RunEventKind.Jumped), "離して押し直すと跳ぶ");
        }

        [Test]
        public void Death_FallingBelowTheRoom()
        {
            var run = Run(Pit());
            for (var i = 0; i < 600 && run.Status == RunStatus.Playing; i++) { run.Tick(Right); }
            Assert.That(run.Status, Is.EqualTo(RunStatus.Dying));
            Assert.That(run.LastDeath, Is.EqualTo(DeathCause.Fall));
            Assert.That(run.Player.Body.Top, Is.LessThanOrEqualTo(0));
        }

        [Test]
        public void Death_CanRepeatAnyNumberOfTimes()
        {
            var config = new PrecisionConfig { DeathTicks = 1 };
            var run = Run(Pit(), config);
            for (var i = 0; i < 3000 && run.Deaths < 25; i++) { run.Tick(Right); }
            Assert.That(run.Deaths, Is.EqualTo(25), "残機はなく、何度でもやり直せる");
        }

        [Test]
        public void Exit_ClearsAndLaterTicksAreIgnored()
        {
            var run = Run(Corridor());
            for (var i = 0; i < 600 && !run.IsCleared; i++) { run.Tick(Right); }
            Assert.That(run.IsCleared, Is.True);
            Assert.That(run.TakeEvents() & RunEventKind.Cleared, Is.EqualTo(RunEventKind.Cleared));
            var hash = run.Hash();
            Assert.That(run.Tick(In(-1, 1, true, true)), Is.False);
            Assert.That(run.Hash(), Is.EqualTo(hash), "クリアの後は状態を変えない");
            Assert.That(run.TakeEvents(), Is.EqualTo(RunEventKind.None), "2 回目のクリアは起きない");
        }

        [Test]
        public void SameTick_SpikeAndExit_DeathWins()
        {
            // 跳んだ直後の自機は、列 2 のトゲ（行 1 の下半分）と出口（行 2）に同時に触れうる。
            var room = Map(
                "....",
                "....",
                "..E.",
                "S.^.",
                "####");
            var found = false;
            for (var walk = 0; walk < 60 && !found; walk++)
            {
                var run = Run(room);
                run.Tick(Idle);
                Hold(run, Right, walk);
                if (run.Status != RunStatus.Playing) { break; }
                run.Tick(In(1, jump: true));
                var body = run.Player.Body;
                if (run.Map.TouchesSpike(body) && run.Map.Touches(body, TileKind.Exit))
                {
                    found = true;
                    Assert.That(run.Status, Is.EqualTo(RunStatus.Dying), "同じ Tick なら死亡が優先");
                    Assert.That(run.IsCleared, Is.False);
                }
            }

            Assert.That(found, Is.True, "トゲと出口に同時に触れる Tick を作れなかった");
        }

        [Test]
        public void Spike_CornerClippedDuringTheTick_KillsEvenIfTheEndIsSafe()
        {
            // レビュー：Tick の終わりの位置だけでは、跳び上がりながらトゲの角をかすめた接触を見逃していた。
            // 横に動いた時点（縦に動く前）でトゲの当たりに重なり、縦に動いた後は上へ抜ける Tick を探す。
            var room = Map(
                "E.........",
                "..........",
                "..........",
                "..........",
                "..........",
                "S....^....",
                "##########");
            var found = 0;
            for (var walk = 0; walk < 40; walk++)
            {
                var run = Run(room);
                run.Tick(Idle);
                Hold(run, Right, walk);
                if (run.Status != RunStatus.Playing) { break; }
                for (var air = 0; air < 6 && run.Status == RunStatus.Playing; air++)
                {
                    run.Tick(In(1, jump: true));
                    var motion = run.Player.LastMotion;
                    if (!run.Map.TouchesSpike(motion.End) && run.Map.TouchesSpike(motion.Corner))
                    {
                        found++;
                        Assert.That(run.Status, Is.EqualTo(RunStatus.Dying), $"歩き {walk} Tick・空中 {air + 1} Tick 目：終わりの位置は安全でも、途中で触れたら死亡");
                        Assert.That(run.LastDeath, Is.EqualTo(DeathCause.Spike));
                    }
                }
            }

            Assert.That(found, Is.GreaterThan(0), "角をかすめる Tick を作れなかった");
        }

        [Test]
        public void Spike_TheReviewExample_DiagonalDashPastTheCornerOfRoom1()
        {
            // レビューの例：1 部屋目の列 28・行 4 のトゲの近く (6983, 1110) から右上へダッシュする。
            // 終わり (7033, 1160) はトゲの上だが、横に動いた (7033, 1110) で当たりに重なる。
            var map = Rooms.All()[0].Map;
            var player = new Player(new PrecisionConfig(), new Box(6983, 1110, Player.Width, Player.Height));
            player.Step(map, Idle);
            var before = player.Body;
            player.Step(map, In(1, 1, dash: true));
            var motion = player.LastMotion;
            Assert.That(motion.Start, Is.EqualTo(before));
            Assert.That(map.TouchesSpike(motion.End), Is.False, "終わりの位置だけなら安全");
            Assert.That(map.TouchesSpike(motion.HorizontalSweep), Is.True, "横に動く区間でトゲに触れる");
        }

        [Test]
        public void Motion_IsHorizontalThenVertical_StoppingAtTerrain()
        {
            var player = Floating((40 * T) - Player.Width - 20, 6 * T);
            var start = player.Body;
            player.Step(Open, In(1, 1, dash: true));
            var motion = player.LastMotion;
            Assert.That(motion.Start, Is.EqualTo(start));
            Assert.That(motion.Corner, Is.EqualTo(start.At((40 * T) - Player.Width, start.Y)), "横は壁で止まる");
            Assert.That(motion.End, Is.EqualTo(motion.Corner.Offset(0, PrecisionConfig.DiagonalOf(new PrecisionConfig().DashSpeed))));
            Assert.That(motion.HorizontalSweep, Is.EqualTo(new Box(start.X, start.Y, motion.Corner.Right - start.X, Player.Height)));
            Assert.That(motion.VerticalSweep, Is.EqualTo(new Box(motion.Corner.X, start.Y, Player.Width, motion.End.Top - start.Y)));
        }

        [Test]
        public void Exit_TouchedOnTheWayClears_SpikeOnTheSameSegmentWins()
        {
            var run = Run(Corridor());
            for (var i = 0; i < 600 && !run.IsCleared; i++) { run.Tick(Right); }
            Assert.That(run.IsCleared, Is.True);
            Assert.That(run.Map.Touches(run.Player.LastMotion.HorizontalSweep, TileKind.Exit), Is.True);
        }

        [Test]
        public void Events_ReportActionsOnce()
        {
            var run = Run(Corridor());
            run.Tick(Idle);
            run.Tick(Jump);
            Assert.That(run.TakeEvents(), Is.EqualTo(RunEventKind.Jumped));
            Assert.That(run.TakeEvents(), Is.EqualTo(RunEventKind.None));
        }

        [Test]
        public void Clone_IsIndependent()
        {
            var run = Run(Corridor());
            Hold(run, Right, 3);
            var copy = run.Clone();
            Hold(run, Right, 10);
            Assert.That(copy.Ticks, Is.EqualTo(3));
            Hold(copy, Right, 10);
            Assert.That(copy.Hash(), Is.EqualTo(run.Hash()), "同じ入力なら同じ状態");
        }

        [Test]
        public void Hash_DistinguishesABufferedJumpThatHasNotFiredYet()
        {
            // レビュー P3-2：着地前にジャンプを押してバッファに入れただけの状態は、位置・速度が同じでも以後の動きが違う。
            var room = Ledge();
            var run = Run(room);
            run.Tick(Idle);
            Hold(run, Jump, 25);
            run.Tick(Idle);
            Assert.That(run.Player.VelocityY, Is.LessThan(0), "落下中");
            Assert.That(run.Player.Grounded, Is.False);

            // 着地の 3 Tick 前（バッファの範囲内）まで落とす。
            for (var i = 0; i < 120 && TicksToLand(run) > 3; i++) { run.Tick(Idle); }
            Assert.That(TicksToLand(run), Is.EqualTo(3));
            var buffered = run.Clone();
            var plain = run.Clone();
            buffered.Tick(Jump);
            plain.Tick(Idle);
            Assert.That(buffered.Player.Body, Is.EqualTo(plain.Player.Body));
            Assert.That(buffered.Player.VelocityY, Is.EqualTo(plain.Player.VelocityY));
            Assert.That(buffered.Player.BufferRemaining, Is.GreaterThan(0));
            Assert.That(buffered.Hash(), Is.Not.EqualTo(plain.Hash()), "バッファの残りもハッシュに含める");

            for (var i = 0; i < 60 && buffered.Player.Body == plain.Player.Body; i++)
            {
                buffered.Tick(Idle);
                plain.Tick(Idle);
            }

            Assert.That(buffered.Player.Body, Is.Not.EqualTo(plain.Player.Body), "着地すると片方だけ跳ぶ");
        }

        private static int TicksToLand(RoomRun run)
        {
            var probe = run.Clone();
            for (var i = 1; i <= 120; i++)
            {
                probe.Tick(Idle);
                if (probe.Player.Grounded) { return i; }
            }

            return int.MaxValue;
        }

        [Test]
        public void Hash_DistinguishesAHeldDashKey()
        {
            var room = Ledge();
            var run = Run(room);
            run.Tick(Idle);
            Hold(run, Jump, 3);
            run.Tick(Dash);
            Hold(run, Idle, new PrecisionConfig().DashTicks);
            Assert.That(run.Player.DashesLeft, Is.Zero);
            var held = run.Clone();
            var released = run.Clone();
            held.Tick(Dash);
            released.Tick(Idle);
            Assert.That(held.Player.Body, Is.EqualTo(released.Player.Body), "回数がないのでどちらも動きは同じ");
            Assert.That(held.Hash(), Is.Not.EqualTo(released.Hash()), "押したままかどうか（着地後に押し直しが要るか）もハッシュに含める");
        }

        [Test]
        public void Hash_DistinguishesTheWallJumpLockAndDashTicks()
        {
            var config = new PrecisionConfig();
            var locked = new Player(config, new Box(0, 6 * T, Player.Width, Player.Height));
            locked.Step(Open, Idle);
            var copy = locked.Clone();
            locked.Step(Open, Jump);
            Assert.That(locked.Hash(), Is.Not.EqualTo(copy.Hash()));

            var dashing = Floating(10 * T, 6 * T, config);
            dashing.Step(Open, Dash);
            var a = dashing.Clone();
            var b = dashing.Clone();
            a.Step(Open, Idle);
            b.Step(Open, Idle);
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()), "同じ状態・同じ入力なら同じ");
        }

        [Test]
        public void Constructor_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentNullException>(() => _ = new RoomRun(null!, Corridor()));
            Assert.Throws<ArgumentNullException>(() => _ = new RoomRun(new PrecisionConfig(), null!));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new RoomRun(new PrecisionConfig { DashTicks = 0 }, Corridor()));
        }

        [Test]
        public void Run_CopiesConfig_LaterChangesDoNotAffectIt()
        {
            var config = new PrecisionConfig();
            var run = Run(Corridor(), config);
            config.MoveSpeed = 200;
            Hold(run, Right, 30);
            Assert.That(run.Player.VelocityX, Is.EqualTo(new PrecisionConfig().MoveSpeed));
        }
    }
}
