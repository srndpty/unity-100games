using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>題材固有：経験値の重複獲得なし。宝石は触れた Tick に 1 回だけ加わってその場で消え、落とした量と取った量の合計が合う。</summary>
    public sealed class ExperienceTests
    {
        [Test]
        public void SeveralGemsTouchedInOneTick_EachCountsOnce()
        {
            var run = Run();
            run.PlaceGem(At(10, 0), 2);
            run.PlaceGem(At(-10, 0), 3);
            run.PlaceGem(At(0, 10), 1);
            run.Step(PlayerCommand.Idle);

            Assert.That(run.TotalExperience, Is.EqualTo(6));
            Assert.That(run.GemsCollected, Is.EqualTo(3));
            Assert.That(run.Gems.Count, Is.Zero);
            Assert.That(run.TakeEvents() & RunEvents.GemCollected, Is.EqualTo(RunEvents.GemCollected));

            // 取った後に同じ場所に留まっても、もう加わらない。
            Wait(run, 30);
            Assert.That(run.TotalExperience, Is.EqualTo(6));
            Assert.That(run.GemsCollected, Is.EqualTo(3));
        }

        [Test]
        public void GemOutsidePickup_StaysUntilApproached_ThenIsCollectedOnce()
        {
            var config = Quiet();
            var run = Run(config);
            run.PlaceGem(At(config.PickupRadius, 0), 4);
            Wait(run, 20);
            Assert.That(run.Gems.Count, Is.EqualTo(1));
            Assert.That(run.Gems.IsAttracted(0), Is.False, "ちょうど吸い寄せの半径では吸い寄せない");

            run.Step(new PlayerCommand(1, 0));
            Assert.That(run.Gems.IsAttracted(0), Is.True);
            for (var i = 0; i < 60 && run.Gems.Count > 0; i++) { run.Step(PlayerCommand.Idle); }
            Assert.That(run.Gems.Count, Is.Zero);
            Assert.That(run.TotalExperience, Is.EqualTo(4));
        }

        [Test]
        public void GemDroppedByAKillNextToThePlayer_IsCollectedOnce()
        {
            var config = Quiet();
            var run = Run(config);
            run.SetWeaponLevel(Pulse, 1);
            run.Place(Mite, At(200, 0));
            run.Place(Mite, At(-200, 0));
            for (var i = 0; i < 400 && run.Kills < 2; i++) { run.Step(PlayerCommand.Idle); }
            Wait(run, 60);

            Assert.That(run.Kills, Is.EqualTo(2));
            Assert.That(run.ExperienceDropped, Is.EqualTo(2 * config.Enemies[Mite].Experience));
            Assert.That(run.TotalExperience, Is.EqualTo(run.ExperienceDropped));
            Assert.That(run.Gems.Count, Is.Zero);
        }

        [Test]
        public void FullGemPool_MergesIntoTheNearestGem_KeepingTheTotal()
        {
            var config = Quiet(c => c.MaxGems = 2);
            var run = Run(config);
            run.PlaceGem(At(40 * T, 0), 2);
            run.PlaceGem(At(-40 * T, 0), 3);
            run.PlaceGem(At(39 * T, 0), 5);

            Assert.That(run.Gems.Count, Is.EqualTo(2));
            Assert.That(run.Gems.TotalValue, Is.EqualTo(10));
            Assert.That(run.ExperienceDropped, Is.EqualTo(10));
            var right = run.Gems.Position(0).X > 0 ? 0 : 1;
            Assert.That(run.Gems.ValueOf(right), Is.EqualTo(7), "いちばん近い宝石に足す");
        }

        /// <summary>
        /// レビュー（028 P2-3）：満杯の宝石へ足し続けて int を超えても値を捨てない。int.MaxValue の直前・ちょうど・超過で、
        /// 落ちている合計が落とした合計と等しく、取れば全部が経験値になる。
        /// </summary>
        [Test]
        public void MergedGemBeyondIntMax_KeepsEveryPoint()
        {
            var run = Run(Quiet(c => c.MaxGems = 1));
            var far = At(40 * T, 0);
            const int Big = TideConfig.MaxExperience;
            for (var i = 0; i < int.MaxValue / Big; i++) { run.PlaceGem(far, Big); }
            run.PlaceGem(far, (int)(int.MaxValue % Big) - 1);
            Assert.That(run.Gems.ValueOf(0), Is.EqualTo(int.MaxValue - 1L), "直前");
            run.PlaceGem(far, 1);
            Assert.That(run.Gems.ValueOf(0), Is.EqualTo((long)int.MaxValue), "ちょうど");
            run.PlaceGem(far, Big);
            Assert.That(run.Gems.ValueOf(0), Is.EqualTo(int.MaxValue + (long)Big), "超過しても切り捨てない");
            Assert.That(run.Gems.Count, Is.EqualTo(1));
            Assert.That(run.Gems.TotalValue, Is.EqualTo(run.ExperienceDropped));

            // 取ると全部が経験値になる（今のレベルの経験値も long で、レベルの表で順に上がる）。
            run.PlacePlayer(far);
            run.Step(PlayerCommand.Idle);
            Assert.That(run.Gems.Count, Is.Zero);
            Assert.That(run.TotalExperience, Is.EqualTo(run.ExperienceDropped));
            Assert.That(run.Experience, Is.LessThan(run.ExperienceToNext));
            Assert.That(run.PendingLevelUps, Is.EqualTo(run.Level - 1));
        }

        [Test]
        public void OneBigGem_GivesOneChoicePerLevel_AndStepWaitsWhileChoosing()
        {
            var config = Quiet();
            var run = Run(config, unarmed: false);
            var needed = config.ExperienceTable[0] + config.ExperienceTable[1] + config.ExperienceTable[2];
            run.PlaceGem(At(0, 0), needed + 1);
            run.Step(PlayerCommand.Idle);

            Assert.That(run.Level, Is.EqualTo(4));
            Assert.That(run.Experience, Is.EqualTo(1));
            Assert.That(run.PendingLevelUps, Is.EqualTo(3));
            Assert.That(run.IsChoosing, Is.True);

            // 3 択の間は進まない（経験値・時間・状態が変わらない）。
            var hash = run.Hash();
            var elapsed = run.Elapsed;
            for (var i = 0; i < 10; i++) { run.Step(new PlayerCommand(1, 1)); }
            Assert.That(run.Hash(), Is.EqualTo(hash));
            Assert.That(run.Elapsed, Is.EqualTo(elapsed));

            var shown = 0;
            while (run.IsChoosing)
            {
                shown++;
                run.Choose(0);
            }

            Assert.That(shown, Is.EqualTo(3), "上がったレベルの数だけ 3 択が出る");
            Assert.That(run.PendingLevelUps, Is.Zero);
            run.Step(PlayerCommand.Idle);
            Assert.That(run.Elapsed, Is.EqualTo(elapsed + 1));
        }

        [Test]
        public void LevelAndExperience_AlwaysMatchTheCollectedTotal()
        {
            // 敵の出る既定の設定で武器を全部持ち、取った経験値・レベル・落ちている宝石の合計がいつも合うことを見る。
            var config = new TideConfig { PlayerHealth = TideConfig.MaxHealth };
            var run = new TideRun(config, 5);
            for (var w = 0; w < config.Weapons.Length; w++) { run.SetWeaponLevel(w, config.Weapons[w].MaxLevel); }
            var random = new IntGeometry.SeededRandom(9);
            for (var tick = 0; tick < 6000 && !run.IsOver; tick++)
            {
                while (run.IsChoosing) { run.Choose(random.Next(run.ChoiceCount)); }
                run.Step(new PlayerCommand(random.Range(-1, 1), random.Range(-1, 1)));
                Assert.That(run.TotalExperience + run.Gems.TotalValue, Is.EqualTo(run.ExperienceDropped));

                long spent = 0;
                for (var level = 1; level < run.Level; level++) { spent += config.ExperienceToNext(level); }
                Assert.That(spent + run.Experience, Is.EqualTo(run.TotalExperience));
            }

            Assert.That(run.Kills, Is.GreaterThan(100));
            Assert.That(run.GemsCollected, Is.GreaterThan(0));
        }

        [Test]
        public void ChooseOutsideChoices_IsRejected()
        {
            var run = Run();
            Assert.Throws<InvalidOperationException>(() => run.Choose(0));
            run.PlaceGem(At(0, 0), 5);
            run.Step(PlayerCommand.Idle);
            Assert.That(run.IsChoosing, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Choose(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Choose(run.ChoiceCount));
            Assert.Throws<ArgumentOutOfRangeException>(() => run.PlaceGem(At(0, 0), 0));
        }
    }
}
