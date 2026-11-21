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
