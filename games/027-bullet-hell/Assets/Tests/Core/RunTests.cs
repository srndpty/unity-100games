using System;
using System.Linq;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>試合の進行：自機の移動・ショット・弾幕の切り替え・勝敗・再挑戦・再現性・設定の検証。</summary>
    public sealed class RunTests
    {
        private static readonly PlayerCommand _shoot = new(0, 0, false, true, false);

        [Test]
        public void Moves_AtNormalSlowAndDiagonalSpeeds()
        {
            var config = new VeilConfig();
            var start = config.PlayerStart;
            Assert.That(VeilRun.Moved(start, 1, 0, false, config), Is.EqualTo(start + new Vec2i(config.PlayerSpeed, 0)));
            Assert.That(VeilRun.Moved(start, 0, 1, true, config), Is.EqualTo(start + new Vec2i(0, config.PlayerSlowSpeed)));
            var diagonal = config.PlayerSpeed * 181 / 256;
            Assert.That(VeilRun.Moved(start, -1, 1, false, config), Is.EqualTo(start + new Vec2i(-diagonal, diagonal)));

            config.PlayerSlowSpeed = 1;
            Assert.That(VeilRun.Moved(start, 1, 1, true, config), Is.EqualTo(start + new Vec2i(1, 1)), "斜めの成分は最低 1（022 の知見）");
        }

        [Test]
        public void Moves_StopAtMargin()
        {
            var config = new VeilConfig();
            var corner = VeilRun.Moved(new Vec2i(config.PlayerMargin + 1, config.PlayerMargin + 1), -1, -1, false, config);
            Assert.That(corner, Is.EqualTo(new Vec2i(config.PlayerMargin, config.PlayerMargin)));
            var top = VeilRun.Moved(new Vec2i(Field.Width / 2, Field.Height - config.PlayerMargin), 0, 1, false, config);
            Assert.That(top.Y, Is.EqualTo(Field.Height - config.PlayerMargin));
        }

        [Test]
        public void Interval_ThenPatternStartsAndFiresNextTick()
        {
            var config = QuickConfig();
            config.IntervalTicks = 5;
            var run = RunOf("emit ring every=1 count=4 speed=1", config);
            for (var t = 1; t <= 4; t++)
            {
                run.Step(PlayerCommand.None);
                Assert.That(run.Phase, Is.EqualTo(RunPhase.Interval));
                Assert.That(run.IntervalRemaining, Is.EqualTo(5 - t));
            }

            run.Step(PlayerCommand.None);
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Pattern), "5 Tick 目に始まる");
            Assert.That(run.TakeEvents() & RunEvents.PatternStarted, Is.EqualTo(RunEvents.PatternStarted));
            Assert.That(run.Bullets.Count, Is.EqualTo(0), "始まった Tick にはまだ撃たない");
            run.Step(PlayerCommand.None);
            Assert.That(run.Bullets.Count, Is.EqualTo(4));
        }

        [Test]
        public void ShotsDuringInterval_DoNotDamage()
        {
            var config = QuickConfig();
            config.IntervalTicks = 60;
            var run = RunOf(Quiet, config, hp: 50);
            Assert.That(run.BossHp, Is.EqualTo(50), "弾幕の間は次の弾幕の体力を見せる");
            Assert.That(run.PatternTicksLeft, Is.EqualTo(36000));
            for (var t = 0; t < 59; t++) { run.Step(_shoot); }
            Assert.That(run.BossHp, Is.EqualTo(50));
            Assert.That(run.TakeEvents() & RunEvents.BossHit, Is.EqualTo(RunEvents.None));
        }

        [Test]
        public void Shots_TravelUpAndHitBossUnderIt()
        {
            var config = QuickConfig();
            var run = RunOf(Quiet, config, hp: 1000);
            run.Step(PlayerCommand.None); // 弾幕が始まる

            // 自機 y = 64 px からボス y = 360 px（半径 24 px）へ、12 px/Tick。触れるのは 296 - 24 - 4 = 268 px 進んだ 23 Tick 目。
            run.Step(_shoot);
            Assert.That(run.Shots.Count, Is.EqualTo(3));
            var hitTick = StepUntil(run, r => r.BossHp < 1000) + 1;
            Assert.That(hitTick, Is.EqualTo(23));
            Assert.That(run.BossHp, Is.EqualTo(1000 - 3), "3 発とも当たる");
            Assert.That(run.Score, Is.EqualTo(3 * config.ShotHitScore));
        }

        [Test]
        public void Shots_FireAtIntervalWhileHeldAndNarrowWhenSlow()
        {
            var config = QuickConfig();
            var run = RunOf(Quiet, config);
            run.Step(_shoot);
            var first = run.Shots.Select(s => s.Position.X - run.Player.X).ToArray();
            Assert.That(first, Is.EqualTo(new[] { -config.ShotSpread, 0, config.ShotSpread }));
            for (var t = 1; t < config.ShotIntervalTicks; t++)
            {
                run.Step(_shoot);
                Assert.That(run.Shots.Count, Is.EqualTo(3), "間隔の間は撃たない");
            }

            run.Step(new PlayerCommand(0, 0, true, true, false));
            Assert.That(run.Shots.Count, Is.EqualTo(6));
            Assert.That(run.Shots.Skip(3).Select(s => s.Position.X - run.Player.X), Is.EqualTo(new[] { -config.ShotSlowSpread, 0, config.ShotSlowSpread }));
            Assert.That(run.Slow, Is.True);
        }

        [Test]
        public void Shots_LeavingTheTop_AreRemoved()
        {
            var run = RunOf(Quiet);
            run.Step(_shoot);
            StepUntil(run, r => r.Shots.Count == 0, limit: 60);
        }

        [Test]
        public void DefeatingPattern_ClearsBulletsAndAwardsBonusWhenUnspoiled()
        {
            var config = QuickConfig();
            var run = RunOf("emit ring every=30 count=8 speed=0.5", config, hp: 6);
            StepUntil(run, r => r.Phase == RunPhase.Pattern && r.Bullets.Count > 0);
            StepUntil(run, r => r.PatternIndex == 1, _shoot);
            var events = run.TakeEvents();
            Assert.That(events & (RunEvents.PatternDefeated | RunEvents.PatternBonus), Is.EqualTo(RunEvents.PatternDefeated | RunEvents.PatternBonus));
            Assert.That(run.Bullets.Count, Is.EqualTo(0));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Interval));
            Assert.That(run.Score, Is.EqualTo((6 * config.ShotHitScore) + config.PatternBonus), "1 つ目の弾幕のボーナスは 1 倍");
            Assert.That(run.BossHp, Is.EqualTo(6), "次の弾幕の体力");
        }

        [Test]
        public void TimeOut_EndsPatternWithoutBonus()
        {
            var run = RunOf(Quiet, hp: 100, time: 30);
            StepUntil(run, r => r.PatternIndex == 1);
            Assert.That(run.TakeEvents() & (RunEvents.PatternTimedOut | RunEvents.PatternBonus), Is.EqualTo(RunEvents.PatternTimedOut));
            Assert.That(run.Elapsed, Is.EqualTo(1 + 30), "弾幕の間 1 Tick ＋ 制限時間 30 Tick");
            Assert.That(run.Score, Is.EqualTo(0));
        }

        [Test]
        public void BombedPattern_GetsNoBonus()
        {
            var config = QuickConfig();
            config.BombDamagePerTick = 0;
            var run = RunOf(Quiet, config, hp: 3);
            run.Step(new PlayerCommand(0, 0, false, false, bomb: true));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Pattern), "ボムは弾幕の間の最後の Tick に使った");
            Assert.That(run.PatternSpoiled, Is.True, "続いているボムは始まった弾幕に数える");
            StepUntil(run, r => r.PatternIndex == 1, _shoot);
            Assert.That(run.TakeEvents() & RunEvents.PatternBonus, Is.EqualTo(RunEvents.None));
        }

        [Test]
        public void BombDamage_IsAppliedEachTickOfThePattern()
        {
            var config = QuickConfig();
            config.BombTicks = 10;
            config.BombDamagePerTick = 7;
            var run = RunOf(Quiet, config, hp: 1000);
            run.Step(PlayerCommand.None);
            run.Step(new PlayerCommand(0, 0, false, false, bomb: true));
            for (var t = 0; t < 20; t++) { run.Step(PlayerCommand.None); }
            Assert.That(run.BossHp, Is.EqualTo(1000 - 70));
        }

        [Test]
        public void SixPatterns_ThenWinWithBonusesAndNothingAfter()
        {
            // 1 回に 3 発が当たり、撃ち出した弾が次の弾幕にも届くので、当たりの点は 0 にして数えやすくする。
            var config = QuickConfig();
            config.Lives = 2;
            config.ShotHitScore = 0;
            var run = RunOf(Quiet, config, hp: 1, time: 600);
            StepUntil(run, r => r.IsOver, _shoot);

            Assert.That(run.Status, Is.EqualTo(RunStatus.Won));
            Assert.That(run.TakeEvents() & (RunEvents.Won | RunEvents.BossDefeated), Is.EqualTo(RunEvents.Won | RunEvents.BossDefeated));
            Assert.That(run.BossIndex, Is.EqualTo(1));
            Assert.That(run.PatternIndex, Is.EqualTo(2));
            var bonus = Enumerable.Range(1, 6).Sum(n => (long)n * config.PatternBonus);
            Assert.That(run.Score, Is.EqualTo(bonus + (2 * config.LifeBonus) + (config.Bombs * config.BombBonus)));

            var hash = run.Hash();
            run.Step(_shoot);
            Assert.That(run.Hash(), Is.EqualTo(hash));
        }

        [Test]
        public void SecondBoss_StartsAtHomeAndRemembersScoreForRetry()
        {
            var run = RunOf("move to at=40,420 over=5\n" + Quiet, hp: 1, time: 600);
            StepUntil(run, r => r.BossIndex == 1, _shoot);
            Assert.That(run.Boss, Is.EqualTo(run.Config.BossHome));
            Assert.That(run.BossStartScore, Is.EqualTo(run.Score));
            Assert.That(run.PatternIndex, Is.EqualTo(0));
        }

        [Test]
        public void RetryFromBoss_StartsThereWithFullLivesAndGivenScore()
        {
            var config = QuickConfig();
            var run = new VeilRun(config, Bosses(Quiet), seed: 9, startBoss: 1, startScore: 1234);
            Assert.That(run.BossIndex, Is.EqualTo(1));
            Assert.That(run.PatternIndex, Is.EqualTo(0));
            Assert.That(run.Lives, Is.EqualTo(config.Lives));
            Assert.That(run.Bombs, Is.EqualTo(config.Bombs));
            Assert.That(run.Score, Is.EqualTo(1234));
            Assert.That(run.BossStartScore, Is.EqualTo(1234));
            Assert.That(run.Phase, Is.EqualTo(RunPhase.Interval));
        }

        [Test]
        public void SameSeedAndInputs_GiveSameHash_AndCloneContinuesIdentically()
        {
            var bosses = LoadScripts();
            static PlayerCommand Input(int t) => new((t / 37 % 3) - 1, (t / 53 % 3) - 1, t % 90 < 30, t % 7 != 0, t == 400);

            var a = new VeilRun(new VeilConfig(), bosses, 5);
            var b = new VeilRun(new VeilConfig(), bosses, 5);
            for (var t = 0; t < 600; t++)
            {
                a.Step(Input(t));
                b.Step(Input(t));
            }

            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
            var copy = a.Clone();
            for (var t = 600; t < 1200; t++)
            {
                a.Step(Input(t));
                copy.Step(Input(t));
            }

            Assert.That(copy.Hash(), Is.EqualTo(a.Hash()));
            Assert.That(b.Hash(), Is.Not.EqualTo(a.Hash()), "複製を進めても元は変わらない");
            Assert.That(new VeilRun(new VeilConfig(), bosses, 6).Hash(), Is.Not.EqualTo(new VeilRun(new VeilConfig(), bosses, 5).Hash()));
        }

        [Test]
        public void BulletCap_IsNeverExceededInARealRun()
        {
            var config = new VeilConfig { MaxBullets = 64, Lives = 9 };
            var run = new VeilRun(config, LoadScripts(), 3);
            for (var t = 0; t < 3000 && !run.IsOver; t++)
            {
                run.Step(PlayerCommand.None);
                Assert.That(run.Bullets.Count, Is.LessThanOrEqualTo(64));
            }

            Assert.That(run.Bullets.Dropped, Is.GreaterThan(0), "容量を超える発射があった");
        }

        [Test]
        public void ProductionScripts_AreTwoBossesWithThreePatternsEach()
        {
            var bosses = LoadScripts();
            Assert.DoesNotThrow(() => VeilConfig.ValidateBosses(bosses));
            Assert.That(bosses.SelectMany(b => b.Patterns).Select(p => p.Name).Distinct().Count(), Is.EqualTo(6));
        }

        [Test]
        public void InvalidSetup_IsRejected()
        {
            var bosses = Bosses(Quiet);
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new VeilRun(new VeilConfig { Lives = 0 }, bosses, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new VeilRun(new VeilConfig { PlayerSlowSpeed = 5 * Field.Unit }, bosses, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new VeilRun(new VeilConfig { MaxBullets = 0 }, bosses, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new VeilRun(new VeilConfig { PlayerMargin = 1 }, bosses, 1));
            Assert.Throws<ArgumentNullException>(() => _ = new VeilRun(null!, bosses, 1));
            Assert.Throws<ArgumentException>(() => _ = new VeilRun(new VeilConfig(), new[] { bosses[0] }, 1));
            var twoPatterns = ScriptParser.ParseOrThrow("boss \"B\" radius=8\npattern \"P\" hp=1 time=1\n" + Quiet + "\npattern \"Q\" hp=1 time=1\n" + Quiet, "t");
            Assert.Throws<ArgumentException>(() => _ = new VeilRun(new VeilConfig(), new[] { twoPatterns, twoPatterns }, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new VeilRun(new VeilConfig(), bosses, 1, startBoss: 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new VeilRun(new VeilConfig(), bosses, 1, startScore: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PlayerCommand(2, 0, false, false, false));
        }

        [Test]
        public void ConfigIsCopied_LaterChangesDoNotAffectRun()
        {
            var config = QuickConfig();
            var run = new VeilRun(config, Bosses(Quiet), 1);
            config.PlayerSpeed = 1;
            run.Step(new PlayerCommand(1, 0, false, false, false));
            Assert.That(run.Player.X - run.Config.PlayerStart.X, Is.EqualTo(new VeilConfig().PlayerSpeed));
        }

        [Test]
        public void Touches_HugeMovement_DoesNotOverflow()
        {
            // ボスが 1 Tick で領域を横切るような移動でも例外にならず、通り道の上の点には触れる。
            Assert.That(VeilRun.Touches(Px(0, 200), Px(384, 200), Px(192, 200), Px(192, 200), 4 * Field.Unit), Is.True);
            Assert.That(VeilRun.Touches(Px(0, 200), Px(384, 200), Px(192, 220), Px(192, 220), 4 * Field.Unit), Is.False);
            Assert.That(VeilRun.Touches(Px(0, 0), Px(384, 448), Px(384, 0), Px(0, 448), 2 * Field.Unit), Is.True, "交差する 2 つの大きな移動");
            Assert.That(VeilRun.Touches(Px(0, 0), Px(1, 0), Px(300, 0), Px(300, 0), 4 * Field.Unit), Is.False);
        }
    }
}
