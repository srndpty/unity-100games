using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>
    /// 題材固有：無敵中の二重被弾なし。被弾は同じ Tick に何発触れても 1 回、無敵は被弾した Tick を含めて InvincibleTicks の間。
    /// 動かない自機へ、真上のボスから大玉を毎 Tick 撃ち続ける（弾は約 74 Tick で届き、以後は毎 Tick どれかが触れている）。
    /// </summary>
    public sealed class InvincibilityTests
    {
        private const string Stream = "emit aimed every=1 count=1 speed=4 shape=large";

        [Test]
        public void SeveralBulletsInOneTick_CostOneLife()
        {
            // 同じ向き・同じ位置の 3 発が同じ Tick に触れる。
            var run = RunOf("emit aimed every=3600 count=3 spread=0 speed=4 shape=large");
            StepUntil(run, r => r.Hits > 0);
            Assert.That(run.Lives, Is.EqualTo(8));
            Assert.That(run.Hits, Is.EqualTo(1));
            Assert.That(run.Bullets.Count, Is.EqualTo(0), "被弾で敵弾を全消去");
            Assert.That(run.IsInvincible, Is.True);
        }

        [Test]
        public void TouchesDuringInvincibility_AreIgnoredUntilTheBoundaryTick()
        {
            var config = QuickConfig();
            config.InvincibleTicks = 100;
            var run = RunOf(Stream, config);
            StepUntil(run, r => r.Hits > 0);
            var hurtTick = run.Elapsed;
            Assert.That(run.InvincibleRemaining, Is.EqualTo(100), "被弾した Tick を含めて数える");

            var touchedWhileInvincible = 0;
            while (run.Elapsed < hurtTick + 99)
            {
                run.Step(PlayerCommand.None);
                if (AnyBulletTouchesPlayer(run)) { touchedWhileInvincible++; }
                Assert.That(run.Lives, Is.EqualTo(8), $"Tick {run.Elapsed}");
            }

            Assert.That(touchedWhileInvincible, Is.GreaterThan(10), "無敵の間に実際に弾が触れている（素通りの確認になっている）");
            Assert.That(run.IsInvincible, Is.True, "無敵の最後の Tick");

            run.Step(PlayerCommand.None);
            Assert.That(run.Elapsed, Is.EqualTo(hurtTick + 100));
            Assert.That(run.Lives, Is.EqualTo(7), "無敵が切れた Tick に、触れている弾で被弾する");
            Assert.That(run.Hits, Is.EqualTo(2));
        }

        [Test]
        public void Bomb_MakesInvincibleAndClearsBulletsEveryTick()
        {
            var config = QuickConfig();
            config.BombTicks = 200;
            var run = RunOf(Stream, config);
            StepUntil(run, r => r.Bullets.Count > 50);
            run.Step(new PlayerCommand(0, 0, false, false, bomb: true));
            Assert.That(run.Bombs, Is.EqualTo(config.Bombs - 1));
            for (var t = 0; t < 199; t++)
            {
                Assert.That(run.Bullets.Count, Is.EqualTo(0));
                run.Step(PlayerCommand.None);
            }

            Assert.That(run.Hits, Is.EqualTo(0));
            Assert.That(run.BombRemaining, Is.EqualTo(1), "ボムを使った Tick を含めて 200 Tick 目が最後");
            run.Step(PlayerCommand.None);
            Assert.That(run.BombRemaining, Is.EqualTo(0));
            Assert.That(run.IsInvincible, Is.False);
        }

        [Test]
        public void BombOnTheTickOfAHit_PreventsTheHit()
        {
            var probe = RunOf(Stream);
            StepUntil(probe, r => r.Hits > 0);
            var hitTick = probe.Elapsed;

            var run = RunOf(Stream);
            while (run.Elapsed < hitTick - 1) { run.Step(PlayerCommand.None); }
            run.Step(new PlayerCommand(0, 0, false, false, bomb: true));
            Assert.That(run.Hits, Is.EqualTo(0), "ボムは同じ Tick の当たりより先");
            Assert.That(run.Lives, Is.EqualTo(9));
        }

        [Test]
        public void HitAfterBomb_RestoresBombs()
        {
            var config = QuickConfig();
            config.BombTicks = 10;
            var run = RunOf(Stream, config);
            run.Step(new PlayerCommand(0, 0, false, false, bomb: true));
            run.Step(PlayerCommand.None);
            run.Step(new PlayerCommand(0, 0, false, false, bomb: true));
            Assert.That(run.Bombs, Is.EqualTo(config.Bombs - 1), "ボム中はもう 1 個使えない");

            StepUntil(run, r => r.Hits > 0);
            Assert.That(run.Bombs, Is.EqualTo(config.Bombs), "被弾でボムの数を戻す");
        }

        [Test]
        public void NoBombsLeft_BombDoesNothing()
        {
            var config = QuickConfig();
            config.Bombs = 0;
            var run = RunOf(Stream, config);
            run.Step(new PlayerCommand(0, 0, false, false, bomb: true));
            Assert.That(run.BombRemaining, Is.EqualTo(0));
            Assert.That(run.IsInvincible, Is.False);
            Assert.That(run.TakeEvents() & RunEvents.Bomb, Is.EqualTo(RunEvents.None));
        }

        [Test]
        public void LastLife_LostOnHitAndLaterTicksChangeNothing()
        {
            var config = QuickConfig();
            config.Lives = 1;
            var run = RunOf(Stream, config);
            StepUntil(run, r => r.IsOver);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Lost));
            Assert.That(run.Lives, Is.EqualTo(0));
            Assert.That(run.TakeEvents() & RunEvents.Lost, Is.EqualTo(RunEvents.Lost));

            var hash = run.Hash();
            var elapsed = run.Elapsed;
            for (var t = 0; t < 10; t++) { run.Step(new PlayerCommand(1, 1, true, true, true)); }
            Assert.That(run.Hash(), Is.EqualTo(hash));
            Assert.That(run.Elapsed, Is.EqualTo(elapsed));
        }

        private static bool AnyBulletTouchesPlayer(VeilRun run)
        {
            var radius = run.Config.PlayerRadius;
            for (var i = 0; i < run.Bullets.Count; i++)
            {
                if (VeilRun.Touches(run.Bullets.StartOf(i), run.Bullets.PositionOf(i), run.PlayerStart, run.Player, run.Bullets.RadiusOf(i) + radius)) { return true; }
            }

            return false;
        }
    }
}
