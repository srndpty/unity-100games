using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>1 面の挑戦：踏みつけ・ミス・ゴール・時間。</summary>
    public sealed class LevelRunTests
    {
        private readonly PlatformerConfig _config = new();

        /// <summary>真上から跳ね虫の上に落ちる面。</summary>
        private static Level DropOnto(char enemy)
        {
            return Map(
                "G.......",
                "........",
                "....S...",
                "........",
                "........",
                "........",
                $"....{enemy}...",
                "########");
        }

        private static RunEvents TickUntilEvent(LevelRun run, PlayerInput input, int limit = 600)
        {
            for (var i = 0; i < limit; i++)
            {
                run.Tick(input);
                var events = run.TakeEvents();
                if ((events.Kinds & ~RunEventKind.Jumped) != RunEventKind.None) { return events; }
            }

            return default;
        }

        [Test]
        public void Stomp_DefeatsEnemyAndBounces()
        {
            var run = Run(DropOnto('h'));
            var events = TickUntilEvent(run, Idle);
            Assert.That(events.Has(RunEventKind.Stomped), Is.True);
            Assert.That(events.Stomps, Is.EqualTo(1));
            Assert.That(run.Enemies[0].State, Is.EqualTo(EnemyState.Stomped));
            Assert.That(run.Player.VelocityY, Is.EqualTo(_config.StompBounce));
            Assert.That(run.Status, Is.EqualTo(RunStatus.Playing));
            Assert.That(run.Stomps, Is.EqualTo(1));
        }

        [Test]
        public void Stomp_WithJumpHeldBouncesHigher()
        {
            var run = Run(DropOnto('h'));
            TickUntilEvent(run, Jump);
            Assert.That(run.Player.VelocityY, Is.EqualTo(_config.JumpSpeed));
        }

        [Test]
        public void Stomp_BounceIsNotAnAirJumpAfterwards()
        {
            var run = Run(DropOnto('h'));
            TickUntilEvent(run, Idle);
            for (var i = 0; i < 5; i++)
            {
                run.Tick(Jump);
                run.Tick(Idle);
                Assert.That(run.TakeEvents().Has(RunEventKind.Jumped), Is.False);
            }
        }

        [Test]
        public void FallingOntoThorn_IsMiss()
        {
            var run = Run(DropOnto('t'));
            var events = TickUntilEvent(run, Idle);
            Assert.That(events.Has(RunEventKind.Missed), Is.True);
            Assert.That(run.MissReason, Is.EqualTo(MissReason.Enemy));
            Assert.That(run.Enemies[0].IsAlive, Is.True);
        }

        [Test]
        public void WalkerWalkingIntoPlayer_IsMiss()
        {
            var run = Run(Map(
                "G.......",
                "S...w...",
                "########"));
            var events = TickUntilEvent(run, Idle);
            Assert.That(events.Has(RunEventKind.Missed), Is.True);
            Assert.That(run.MissReason, Is.EqualTo(MissReason.Enemy));
            Assert.That(run.Enemies[0].IsAlive, Is.True, "横から触れても敵は倒れない");
        }

        [Test]
        public void WalkingIntoWalker_IsMiss()
        {
            var run = Run(Map(
                "G..........",
                "S.......w..",
                "###########"), new PlatformerConfig { ActivationTiles = 1 });
            var events = TickUntilEvent(run, Right);
            Assert.That(events.Has(RunEventKind.Missed), Is.True);
        }

        [Test]
        public void JumpingOverWalkerAndLandingOnIt_IsStomp()
        {
            var run = Run(Map(
                "G..........",
                "...........",
                "...........",
                "...........",
                "S......w...",
                "###########"), new PlatformerConfig { ActivationTiles = 1 });
            Hold(run, Right, 30);
            run.TakeEvents();
            run.Tick(RightJump);
            Assert.That(run.TakeEvents().Has(RunEventKind.Jumped), Is.True);
            var events = TickUntilEvent(run, RightJump);
            Assert.That(events.Has(RunEventKind.Stomped), Is.True, $"{events.Kinds}");
        }

        [Test]
        public void TwoEnemiesTouchedAtSameTime_BothStomped()
        {
            var level = Map(
                "G...........",
                "......S.....",
                "............",
                "............",
                "............",
                "............",
                "......hh....",
                "############");
            var stompedTogether = TryLandAcross(level, 2);
            Assert.That(stompedTogether, Is.True);
        }

        [Test]
        public void LandingAcrossThornAndHopper_ThornTouchedFirst_IsMiss()
        {
            // 茨玉は跳ね虫より背が高いので、両方にまたがって落ちると先に茨玉に触れる。
            var level = Map(
                "G...........",
                "......S.....",
                "............",
                "............",
                "............",
                "............",
                "......ht....",
                "############");
            var run = Run(level, new PlatformerConfig { ActivationTiles = 1 });
            var missed = false;
            for (var hold = 0; hold < 120 && !missed; hold++)
            {
                run = Run(level, new PlatformerConfig { ActivationTiles = 1 });
                Hold(run, Right, hold);
                var events = TickUntilEvent(run, Idle);
                missed = events.Has(RunEventKind.Missed) && run.Player.Body.Right > run.Enemies[1].Body.Left && run.Player.Body.Left < run.Enemies[0].Body.Right;
            }

            Assert.That(missed, Is.True, "両方にまたがって落ちたらミス");
            Assert.That(run.Enemies[0].IsAlive, Is.True, "先に触れた茨玉でミスになり、跳ね虫は倒れない");
        }

        [Test]
        public void Goal_ClearsOnceAndIgnoresLaterTicks()
        {
            var run = Run(Map(
                ".....G..",
                "S....G..",
                "########"));
            var events = TickUntilEvent(run, Right);
            Assert.That(events.Has(RunEventKind.Cleared), Is.True);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Cleared));

            var body = run.Player.Body;
            var ticks = run.Ticks;
            var hash = run.Hash();
            for (var i = 0; i < 30; i++)
            {
                Assert.That(run.Tick(i % 2 == 0 ? Right : RightJump), Is.False);
            }

            Assert.That(run.TakeEvents().Kinds, Is.EqualTo(RunEventKind.None), "2 回目のクリアは起きない");
            Assert.That(run.Player.Body, Is.EqualTo(body));
            Assert.That(run.Ticks, Is.EqualTo(ticks));
            Assert.That(run.Hash(), Is.EqualTo(hash));
        }

        [Test]
        public void Goal_TakesPriorityOverEnemyInSameTick()
        {
            // 1 Tick 目で自機はゴールに触れ、同じ Tick に動けば歩き虫が自機に届く。ゴールの判定が先なので敵は動かない。
            var config = new PlatformerConfig { WalkSpeed = 100, RunSpeed = 100, GroundAccel = 100, WalkerSpeed = 255 };
            var run = Run(Map(
                "G.....",
                "SGw...",
                "######"), config);
            run.Tick(Right);
            var events = run.TakeEvents();
            Assert.That(events.Has(RunEventKind.Cleared), Is.True);
            Assert.That(events.Has(RunEventKind.Missed), Is.False);
        }

        [Test]
        public void Pit_IsMiss()
        {
            var run = Run(Map(
                "G.....",
                "S.....",
                "##..##"));
            var events = TickUntilEvent(run, Right);
            Assert.That(events.Has(RunEventKind.Missed), Is.True);
            Assert.That(run.MissReason, Is.EqualTo(MissReason.Pit));
            Assert.That(run.Tick(Right), Is.False, "ミスの後は進まない");
        }

        [Test]
        public void TimeUp_IsMissAtLimit()
        {
            var config = new PlatformerConfig { TimeLimitSeconds = PlatformerConfig.MinTimeLimitSeconds };
            var run = Run(Flat(), config);
            Hold(run, Idle, config.TimeLimitTicks - 1);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Playing));
            Assert.That(run.RemainingTicks, Is.EqualTo(1));
            Assert.That(run.RemainingSeconds, Is.EqualTo(1));
            run.Tick(Idle);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Missed));
            Assert.That(run.MissReason, Is.EqualTo(MissReason.TimeUp));
            Assert.That(run.RemainingTicks, Is.Zero);
        }

        [Test]
        public void Jump_EventOnlyOncePerPress()
        {
            var run = Run(Flat());
            run.Tick(Idle);
            var jumps = 0;
            for (var i = 0; i < 200; i++)
            {
                run.Tick(Jump);
                if (run.TakeEvents().Has(RunEventKind.Jumped)) { jumps++; }
            }

            Assert.That(jumps, Is.EqualTo(1));
        }

        [Test]
        public void SameInputs_SameHash()
        {
            var a = Run(Levels.Meadow);
            var b = Run(Levels.Meadow);
            Script(a);
            Script(b);
            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
        }

        /// <summary>整数だけで動くので、Mono（Unity）と .NET で同じ値になる。</summary>
        [Test]
        public void ScriptedRun_HashMatchesFixedValue()
        {
            var run = Run(Levels.Meadow);
            Script(run);
            Assert.That(run.Hash(), Is.EqualTo(ExpectedScriptHash));
        }

        [Test]
        public void Clone_HasSameStateAndAdvancesIndependently()
        {
            var run = Run(Levels.Meadow);
            Script(run);
            var clone = run.Clone();
            Assert.That(clone.Hash(), Is.EqualTo(run.Hash()));

            Hold(clone, RunRight, 60);
            Assert.That(clone.Hash(), Is.Not.EqualTo(run.Hash()));
            Assert.That(run.Ticks, Is.EqualTo(600), "元は進まない");

            // 複製を進めた後でも、元を同じ入力で進めれば同じ状態になる。
            Hold(run, RunRight, 60);
            Assert.That(run.Hash(), Is.EqualTo(clone.Hash()));
        }

        [Test]
        public void Constructor_RejectsInvalidConfig()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = Run(Flat(), new PlatformerConfig { Gravity = 0 }));
            Assert.Throws<ArgumentNullException>(() => _ = new LevelRun(_config, null!));
        }

        [Test]
        public void Constructor_CopiesConfig()
        {
            var config = new PlatformerConfig();
            var run = Run(Flat(), config);
            config.WalkSpeed = 1;
            Hold(run, Right, 60);
            Assert.That(run.Player.VelocityX, Is.EqualTo(new PlatformerConfig().WalkSpeed));
        }

        private const uint ExpectedScriptHash = 3356844846u;

        /// <summary>走りながら一定の間隔で跳ぶ 600 Tick。</summary>
        private static void Script(LevelRun run)
        {
            for (var i = 0; i < 600; i++)
            {
                run.Tick(new PlayerInput(i % 200 < 170 ? 1 : -1, i % 3 != 0, i % 50 < 20));
            }
        }

        /// <summary>右へ押す Tick 数を変えながら落とし、2 体を同じ Tick に踏む落ち方があるか。</summary>
        private static bool TryLandAcross(Level level, int expected)
        {
            for (var hold = 0; hold < 120; hold++)
            {
                var run = Run(level, new PlatformerConfig { ActivationTiles = 1 });
                Hold(run, Right, hold);
                var events = TickUntilEvent(run, Idle);
                if (events.Has(RunEventKind.Stomped) && events.Stomps == expected)
                {
                    return run.Enemies[0].State == EnemyState.Stomped && run.Enemies[1].State == EnemyState.Stomped;
                }
            }

            return false;
        }
    }
}
