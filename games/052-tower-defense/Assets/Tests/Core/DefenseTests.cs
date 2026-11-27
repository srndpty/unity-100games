using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class DefenseTests
    {
        [TestCase(0, 0, 3)]
        [TestCase(3000, 3, 3)]
        [TestCase(5000, 3, 1)]
        [TestCase(10000, 8, 1)]
        [TestCase(14000, 8, 5)]
        [TestCase(15000, 9, 5)]
        [TestCase(17000, 11, 5)]
        public void RoutePassesThroughEveryWaypoint(int progress, int x, int y)
        {
            var point = Canyon.At(progress);
            Assert.That(point.X, Is.EqualTo(x * Canyon.Scale));
            Assert.That(point.Y, Is.EqualTo(y * Canyon.Scale));
        }

        [Test]
        public void ValidatesBuildUpgradeAndSellEconomy()
        {
            var battle = new Defense(new Rules());
            Assert.That(battle.Build(-1, TowerKind.Bow), Is.EqualTo(CommandResult.Invalid));
            Assert.That(battle.Build(0, (TowerKind)4), Is.EqualTo(CommandResult.Invalid));
            Assert.That(battle.Sell(0), Is.EqualTo(CommandResult.Empty));
            Assert.That(battle.Build(0, TowerKind.Bow), Is.EqualTo(CommandResult.Applied));
            Assert.That(battle.Build(0, TowerKind.Frost), Is.EqualTo(CommandResult.Occupied));
            Assert.That(battle.Upgrade(0), Is.EqualTo(CommandResult.Applied));
            Assert.That(battle.Upgrade(0), Is.EqualTo(CommandResult.Applied));
            Assert.That(battle.Upgrade(0), Is.EqualTo(CommandResult.Maximum));
            Assert.That(battle.Build(1, TowerKind.Arcane), Is.EqualTo(CommandResult.Funds));
            Assert.That(battle.Money, Is.EqualTo(47));
            Assert.That(battle.Sell(0), Is.EqualTo(CommandResult.Applied));
            Assert.That(battle.Money, Is.EqualTo(126));
        }

        [TestCase(TowerKind.Bow, 995)]
        [TestCase(TowerKind.Cannon, 990)]
        [TestCase(TowerKind.Frost, 997)]
        public void AttacksOnlyOnInclusiveRangeBoundary(TowerKind kind, int expectedHp)
        {
            var rules = new Rules { SpawnInterval = 1 };
            rules.Ranges[(int)kind] = 1000;
            rules.Speeds[0] = 100;
            rules.HitPoints[0] = 1000;
            var battle = new Defense(rules);
            battle.Build(0, kind);
            battle.StartWave();
            for (var tick = 0; tick < 9; tick++) { battle.Step(); }
            Assert.That(battle.Enemies[0].Hp, Is.EqualTo(1000));
            battle.Step();
            Assert.That(battle.Enemies[0].Hp, Is.EqualTo(expectedHp));
            Assert.That(battle.Enemies[0].MaxHp, Is.EqualTo(1000));
            Assert.That(battle.Enemies[0].HitTick, Is.EqualTo(battle.Tick));
            Assert.That(battle.Enemies[0].HitBy, Is.EqualTo(kind));
            Assert.That(battle.Enemies[1].Hp, Is.EqualTo(kind == TowerKind.Cannon ? expectedHp : 1000));
            if (kind == TowerKind.Frost)
            {
                Assert.That(battle.Enemies[0].SlowUntil, Is.EqualTo(40));
                battle.Step();
                Assert.That(battle.Enemies[0].Progress, Is.EqualTo(1050));
            }
        }

        [TestCase(TowerKind.Bow, 30)]
        [TestCase(TowerKind.Arcane, 20)]
        public void ArcaneBypassesArmor(TowerKind kind, int hp)
        {
            var rules = new Rules();
            rules.Enemies[0] = 0;
            rules.Enemies[2] = 1;
            var battle = new Defense(rules);
            battle.Build(0, kind);
            battle.StartWave();
            battle.Step();
            Assert.That(battle.Enemies[0].Hp, Is.EqualTo(hp));
        }

        [TestCase(10, Phase.Lost)]
        [TestCase(11, Phase.Won)]
        public void FinalLeakChecksLifeBeforeVictory(int lives, Phase expected)
        {
            var rules = new Rules { StartingLives = lives };
            for (var i = 0; i < rules.Enemies.Length; i++) { rules.Enemies[i] = i % 3 == 0 ? 1 : 0; }
            var battle = new Defense(rules);
            for (var wave = 1; wave <= 10; wave++)
            {
                Assert.That(battle.Wave, Is.EqualTo(wave));
                Assert.That(battle.StartWave(), Is.True);
                Assert.That(battle.StartWave(), Is.False);
                battle.Step();
                Assert.That(battle.Phase, Is.EqualTo(Phase.Running));
                Assert.That(battle.Enemies.Count, Is.EqualTo(1));
                for (var tick = 1; tick < 284; tick++) { battle.Step(); }
                Assert.That(battle.Enemies.Count, Is.Zero);
            }

            Assert.That(battle.Phase, Is.EqualTo(expected));
            Assert.That(battle.StartWave(), Is.False);
            Assert.That(battle.Build(0, TowerKind.Bow), Is.EqualTo(CommandResult.Closed));
        }

        [Test]
        public void SimultaneousEscapesEachCostOneLife()
        {
            var rules = new Rules { SpawnInterval = 1 };
            rules.Enemies[0] = 1;
            rules.Enemies[1] = 1;
            rules.Speeds[0] = Canyon.Length / 2;
            rules.Speeds[1] = Canyon.Length;
            var battle = new Defense(rules);
            battle.StartWave();
            battle.Step();
            Assert.That(battle.Lives, Is.EqualTo(10));
            battle.Step();
            Assert.That(battle.Lives, Is.EqualTo(8));
            Assert.That(battle.Enemies.Count, Is.Zero);
            Assert.That(battle.Wave, Is.EqualTo(2));
        }

        [Test]
        public void EmptyBoardWithScheduledSpawnsDoesNotAdvanceWave()
        {
            var rules = new Rules();
            rules.Power[0] = 100;
            var battle = new Defense(rules);
            battle.Build(0, TowerKind.Bow);
            battle.StartWave();
            battle.Step();
            Assert.That(battle.Enemies.Count, Is.Zero);
            Assert.That(battle.Phase, Is.EqualTo(Phase.Running));
            Assert.That(battle.Wave, Is.EqualTo(1));
            Assert.That(battle.Money, Is.EqualTo(123));
        }

        [Test]
        public void EqualProgressChoosesEarlierSpawn()
        {
            var rules = new Rules { SpawnInterval = 1 };
            rules.Enemies[0] = 1;
            rules.Enemies[1] = 1;
            rules.Speeds[0] = 50;
            rules.Speeds[1] = 100;
            rules.Ranges[0] = 1346;
            var battle = new Defense(rules);
            battle.Build(0, TowerKind.Bow);
            battle.StartWave();
            battle.Step();
            battle.Step();
            Assert.That(battle.Enemies[0].Progress, Is.EqualTo(battle.Enemies[1].Progress));
            Assert.That(battle.Enemies[0].Hp, Is.EqualTo(13));
            Assert.That(battle.Enemies[1].Hp, Is.EqualTo(12));
        }


        [Test]
        public void FinishedWaveClearsTheLastShotBeforeNextWave()
        {
            var rules = new Rules();
            rules.Enemies[0] = 1;
            rules.Power[0] = 100;
            var battle = new Defense(rules);
            battle.Build(0, TowerKind.Bow);
            battle.StartWave();
            battle.Step();
            Assert.That(battle.Phase, Is.EqualTo(Phase.Preparation));
            Assert.That(battle.Towers[0]!.ShotTick, Is.LessThan(battle.Tick - 11));
            battle.StartWave();
            Assert.That(battle.Towers[0]!.ShotTick, Is.LessThan(battle.Tick - 11));
        }

        [Test]
        public void ReplacingATowerPreservesCooldownAndFiredShots()
        {
            var rules = new Rules();
            rules.HitPoints[0] = 1000;
            var battle = new Defense(rules);
            battle.Build(0, TowerKind.Cannon);
            battle.StartWave();
            battle.Step();
            Assert.That(battle.Shots.Count, Is.EqualTo(1));
            Assert.That(battle.Sell(0), Is.EqualTo(CommandResult.Applied));
            Assert.That(battle.Build(0, TowerKind.Bow), Is.EqualTo(CommandResult.Applied));
            battle.Step();
            Assert.That(battle.Shots[0].Kind, Is.EqualTo(TowerKind.Cannon));
            for (var tick = 2; tick < 36; tick++) { battle.Step(); }
            Assert.That(battle.Towers[0]!.ShotTick, Is.LessThan(0));
            battle.Step();
            Assert.That(battle.Towers[0]!.ShotTick, Is.EqualTo(37));
        }

        [Test]
        public void FastTowerKeepsOverlappingShotsAndHits()
        {
            var rules = new Rules();
            rules.HitPoints[0] = 1000;
            rules.Cooldowns[0] = 1;
            var battle = new Defense(rules);
            battle.Build(0, TowerKind.Bow);
            battle.StartWave();
            for (var i = 0; i < 3; i++) { battle.Step(); }
            Assert.That(battle.Shots.Count, Is.EqualTo(3));
            Assert.That(battle.Hits.Count, Is.EqualTo(3));
            Assert.That(battle.Shots[0].Tick, Is.EqualTo(1));
            Assert.That(battle.Shots[2].Tick, Is.EqualTo(3));
        }
    }
}
