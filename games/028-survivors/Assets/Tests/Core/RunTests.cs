using System;
using IntGeometry;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>共通：状態遷移・ボスと勝敗・無敵・負テスト・再現性・掃引。</summary>
    public sealed class RunTests
    {
        private static TideConfig Short(Action<TideConfig>? edit = null) => Quiet(c =>
        {
            c.DurationTicks = 120;
            c.BossTimeLimitTicks = 1500;
            edit?.Invoke(c);
        });

        [Test]
        public void BossAppearsAtTheDuration_AndDefeatingItWins()
        {
            var config = Short(c => c.Enemies[Warden].Health = 5);
            var run = Run(config);
            var events = Wait(run, 119);
            Assert.That(run.BossSpawned, Is.False);
            Assert.That(events & RunEvents.BossSpawned, Is.EqualTo(RunEvents.None));
            events = Wait(run, 1);
            Assert.That(run.BossSpawned, Is.True);
            Assert.That(events & RunEvents.BossSpawned, Is.EqualTo(RunEvents.BossSpawned));
            Assert.That(run.BossIndex(), Is.GreaterThanOrEqualTo(0));
            Assert.That(run.TicksToBoss, Is.Zero);

            run.SetWeaponLevel(Pulse, config.Weapons[Pulse].MaxLevel);
            for (var i = 0; i < 2000 && !run.IsOver; i++) { run.Step(new PlayerCommand(1, 0)); }
            Assert.That(run.Status, Is.EqualTo(RunStatus.Won).Or.EqualTo(RunStatus.Lost));
            if (run.Status == RunStatus.Lost) { Assert.That(run.End, Is.EqualTo(RunEnd.TimeUp)); }

            // 自機の方へ置いたボスなら確実に倒せる。
            var direct = Run(config);
            Wait(direct, 120);
            direct.SetWeaponLevel(Pulse, config.Weapons[Pulse].MaxLevel);
            for (var i = 0; i < 2000 && !direct.IsOver; i++) { direct.Step(PlayerCommand.Idle); }
            Assert.That(direct.Status, Is.EqualTo(RunStatus.Won));
            Assert.That(direct.End, Is.EqualTo(RunEnd.BossDefeated));
        }

        [Test]
        public void BossNotDefeatedInTime_LosesByTimeUp()
        {
            var config = Short(c => c.Enemies[Warden].ContactDamage = 0);
            var run = Run(config);
            Wait(run, config.MaxRunTicks - 1);
            Assert.That(run.IsOver, Is.False);
            var events = Wait(run, 1);
            Assert.That(run.Status, Is.EqualTo(RunStatus.Lost));
            Assert.That(run.End, Is.EqualTo(RunEnd.TimeUp));
            Assert.That(events & RunEvents.Lost, Is.EqualTo(RunEvents.Lost));
        }

        [Test]
        public void HealthZero_Loses_AndLaterStepsChangeNothing()
        {
            var config = Quiet(c => c.PlayerHealth = 5);
            var run = Run(config);
            run.Place(Mite, At(100, 0));
            var events = Wait(run, 1);
            Assert.That(run.Health, Is.Zero);
            Assert.That(run.End, Is.EqualTo(RunEnd.Died));
            Assert.That(events & RunEvents.Lost, Is.EqualTo(RunEvents.Lost));

            var hash = run.Hash();
            Wait(run, 30);
            Assert.That(run.Hash(), Is.EqualTo(hash));
            Assert.That(run.IsChoosing, Is.False);
            Assert.Throws<InvalidOperationException>(() => run.Choose(0));
            Assert.Throws<InvalidOperationException>(() => run.Place(Mite, At(0, 0)));
        }

        [Test]
        public void Invincibility_LastsTheConfiguredTicks_IncludingTheHitTick()
        {
            var config = Quiet(c => c.InvincibleTicks = 10);
            var run = Run(config);
            run.Place(Mite, At(60, 0));
            run.Place(Mite, At(-60, 0));
            run.Step(PlayerCommand.Idle);
            var full = config.PlayerHealth - config.Enemies[Mite].ContactDamage;
            Assert.That(run.Health, Is.EqualTo(full), "同じ Tick に 2 体に触れても 1 回");

            Wait(run, 9);
            Assert.That(run.Health, Is.EqualTo(full), "無敵の最後の Tick まで減らない");
            Wait(run, 1);
            Assert.That(run.Health, Is.EqualTo(full - config.Enemies[Mite].ContactDamage), "切れた次の Tick に減る");
        }

        [Test]
        public void FastShot_HitsAnEnemyBetweenItsEndpoints()
        {
            var config = Quiet();
            Sturdy(config, Gnat);
            config.Weapons = new[] { config.Weapons[Bolt] };
            config.Weapons[0].Levels[0].Speed = 255;
            config.Weapons[0].Levels[0].Size = 8;
            var run = new TideRun(config, 1);
            var index = run.Place(Gnat, At(600, 0));
            var id = run.Enemies.Id(index);
            Wait(run, 5);
            Assert.That(HealthOf(run, id), Is.EqualTo(config.Enemies[Gnat].Health - config.Weapons[0].Levels[0].Damage));
        }

        [Test]
        public void SameSeedAndInputs_GiveTheSameHash_AndClonesDoNotShare()
        {
            TideRun Play(int seed)
            {
                var run = new TideRun(new TideConfig(), seed);
                var random = new SeededRandom(seed);
                for (var i = 0; i < 2000 && !run.IsOver; i++)
                {
                    while (run.IsChoosing) { run.Choose(random.Next(run.ChoiceCount)); }
                    run.Step(new PlayerCommand(random.Range(-1, 1), random.Range(-1, 1)));
                }

                return run;
            }

            var a = Play(3);
            Assert.That(Play(3).Hash(), Is.EqualTo(a.Hash()));
            Assert.That(Play(4).Hash(), Is.Not.EqualTo(a.Hash()));

            var clone = a.Clone();
            var hash = a.Hash();
            while (clone.IsChoosing) { clone.Choose(0); }
            Wait(clone, 50);
            Assert.That(a.Hash(), Is.EqualTo(hash), "複製を進めても元は変わらない");
        }

        [Test]
        public void ConfigIsCopied_LaterEditsDoNotAffectTheRun()
        {
            var config = Quiet();
            var run = new TideRun(config, 1);
            config.Weapons[Bolt].Levels[0].Damage = 999;
            config.Enemies[Mite].Health = 999;
            Assert.That(run.WeaponStatsOf(Bolt)!.Damage, Is.EqualTo(new TideConfig().Weapons[Bolt].Levels[0].Damage));
            Assert.That(run.EnemyDefOf(Mite).Health, Is.EqualTo(new TideConfig().Enemies[Mite].Health));
        }

        [Test]
        public void DefaultConfig_IsValid()
        {
            Assert.DoesNotThrow(() => new TideConfig().Validate());
        }

        private static readonly (string Name, Action<TideConfig> Edit)[] _invalid =
        {
            ("自機の速さ 0", c => c.PlayerSpeed = 0),
            ("自機の速さが上限超え", c => c.PlayerSpeed = TideConfig.MaxSpeed + 1),
            ("ボスが 2 体", c => c.Enemies[Mite].Behavior = EnemyBehavior.Boss),
            ("ボスがいない", c => c.Enemies[Warden].Behavior = EnemyBehavior.Chase),
            ("未定義の振る舞い", c => c.Enemies[Mite].Behavior = (EnemyBehavior)99),
            ("敵の名前が空", c => c.Enemies[Mite].Id = ""),
            ("敵の定義が空", c => c.Enemies[Mite] = null!),
            ("武器のレベルが空", c => c.Weapons[Bolt].Levels = Array.Empty<WeaponLevel>()),
            ("未定義の武器", c => c.Weapons[Bolt].Kind = (WeaponKind)99),
            ("ダメージ 0", c => c.Weapons[Pulse].Levels[2].Damage = 0),
            ("間隔 0", c => c.Weapons[Arc].Levels[0].IntervalTicks = 0),
            ("貫通 0", c => c.Weapons[Bolt].Levels[0].Pierce = 0),
            ("Lash の向きが 3", c => c.Weapons[Lash].Levels[0].Count = 3),
            ("使わない値が負", c => c.Weapons[Pulse].Levels[0].Speed = -1),
            ("弾が上限を超えうる", c => c.Weapons[Bolt].Levels[4].LifeTicks = 3000),
            ("最初の武器が範囲外", c => c.StartingWeapon = 5),
            ("出現の重みの数が違う", c => c.Stages[0].Weights = new[] { 1, 1 }),
            ("ボスの重みが正", c => c.Stages[0].Weights[Warden] = 1),
            ("重みの合計 0", c => c.Stages[0].Weights = new[] { 0, 0, 0, 0 }),
            ("目標が上限超え", c => c.Stages[0].TargetAlive = c.MaxEnemies + 1),
            ("区間が時間を覆わない", c => c.Stages = new[] { c.Stages[0] }),
            ("出現の輪が視野の内側", c => c.SpawnInner = 10 * T),
            ("輪の外側が内側より小さい", c => c.SpawnOuter = c.SpawnInner - 1),
            ("置き直しが輪より近い", c => c.DespawnDistance = c.SpawnOuter),
            ("経験値の表が空", c => c.ExperienceTable = Array.Empty<int>()),
            ("経験値 0", c => c.ExperienceTable[0] = 0),
            ("敵の上限が大きすぎる", c => c.MaxEnemies = TideConfig.MaxEnemiesLimit + 1),
            ("試合が長すぎて座標が桁あふれ", c =>
            {
                c.DurationTicks = TideConfig.MaxDurationTicks;
                c.BossTimeLimitTicks = TideConfig.MaxDurationTicks;
                c.PlayerSpeed = TideConfig.MaxSpeed;
            }),
        };

        [TestCaseSource(nameof(_invalid))]
        public void InvalidConfig_IsRejected((string Name, Action<TideConfig> Edit) invalid)
        {
            var config = new TideConfig();
            invalid.Edit(config);
            Assert.Throws<ArgumentOutOfRangeException>(() => new TideRun(config, 1), invalid.Name);
        }

        [Test]
        public void InvalidCommandsAndPlacements_AreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PlayerCommand(2, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new PlayerCommand(0, -2));
            Assert.Throws<ArgumentNullException>(() => _ = new TideRun(null!, 1));
            var run = Run(Quiet(c => c.MaxEnemies = 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => run.Place(99, At(0, 0)));
            run.Place(Mite, At(10 * T, 0));
            run.Place(Mite, At(-10 * T, 0));
            Assert.Throws<InvalidOperationException>(() => run.Place(Mite, At(0, 10 * T)), "上限を超えて置けない");
            run.Place(Warden, At(0, -10 * T));
            Assert.Throws<InvalidOperationException>(() => run.Place(Warden, At(0, -12 * T)), "ボスは 1 体");
            Assert.Throws<ArgumentOutOfRangeException>(() => run.SetWeaponLevel(Bolt, 6));
            Assert.Throws<ArgumentOutOfRangeException>(() => run.WeaponLevelOf(9));
        }

        [Test]
        public void Movement_UsesDiagonalSpeed_AndRemembersFacing()
        {
            var config = Quiet();
            var run = Run(config);
            run.Step(new PlayerCommand(1, 1));
            var diagonal = TideConfig.DiagonalOf(config.PlayerSpeed);
            Assert.That(run.Player, Is.EqualTo(At(diagonal, diagonal)));
            run.Step(new PlayerCommand(-1, 0));
            Assert.That(run.FacingX, Is.EqualTo(-1));
            run.Step(new PlayerCommand(0, 1));
            Assert.That(run.FacingX, Is.EqualTo(-1), "上下だけの移動では左右の向きを保つ");
            Assert.That(run.Facing, Is.EqualTo(At(0, 1)));
            Assert.That(TideConfig.DiagonalOf(1), Is.EqualTo(1));
        }
    }
}
