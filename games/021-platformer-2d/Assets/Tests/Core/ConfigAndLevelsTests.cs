using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>設定の検証と、3 面の地図の性質。</summary>
    public sealed class ConfigAndLevelsTests
    {
        [Test]
        public void Default_IsValid()
        {
            Assert.DoesNotThrow(() => new PlatformerConfig().Validate());
        }

        [TestCase(nameof(PlatformerConfig.WalkSpeed), 0)]
        [TestCase(nameof(PlatformerConfig.RunSpeed), 23)]
        [TestCase(nameof(PlatformerConfig.RunSpeed), PlatformerConfig.MaxSpeed + 1)]
        [TestCase(nameof(PlatformerConfig.GroundAccel), 0)]
        [TestCase(nameof(PlatformerConfig.AirAccel), 0)]
        [TestCase(nameof(PlatformerConfig.Friction), 0)]
        [TestCase(nameof(PlatformerConfig.SkidDecel), 0)]
        [TestCase(nameof(PlatformerConfig.Gravity), 0)]
        [TestCase(nameof(PlatformerConfig.JumpSpeed), 3)]
        [TestCase(nameof(PlatformerConfig.JumpSpeed), PlatformerConfig.MaxSpeed + 1)]
        [TestCase(nameof(PlatformerConfig.JumpCutSpeed), -1)]
        [TestCase(nameof(PlatformerConfig.JumpCutSpeed), 96)]
        [TestCase(nameof(PlatformerConfig.MaxFallSpeed), 3)]
        [TestCase(nameof(PlatformerConfig.MaxFallSpeed), PlatformerConfig.MaxSpeed + 1)]
        [TestCase(nameof(PlatformerConfig.StompBounce), 0)]
        [TestCase(nameof(PlatformerConfig.StompBounce), 97)]
        [TestCase(nameof(PlatformerConfig.WalkerSpeed), 0)]
        [TestCase(nameof(PlatformerConfig.ThornSpeed), 0)]
        [TestCase(nameof(PlatformerConfig.HopperInterval), 0)]
        [TestCase(nameof(PlatformerConfig.HopperJumpSpeed), 3)]
        [TestCase(nameof(PlatformerConfig.ActivationTiles), 0)]
        [TestCase(nameof(PlatformerConfig.Lives), 0)]
        [TestCase(nameof(PlatformerConfig.Lives), PlatformerConfig.MaxLives + 1)]
        [TestCase(nameof(PlatformerConfig.TimeLimitSeconds), PlatformerConfig.MinTimeLimitSeconds - 1)]
        [TestCase(nameof(PlatformerConfig.TimeLimitSeconds), PlatformerConfig.MaxTimeLimitSeconds + 1)]
        [TestCase(nameof(PlatformerConfig.StompScore), -1)]
        [TestCase(nameof(PlatformerConfig.TimeBonusPerSecond), PlatformerConfig.MaxScorePerEvent + 1)]
        public void Validate_RejectsOutOfRange(string field, int value)
        {
            var config = new PlatformerConfig();
            typeof(PlatformerConfig).GetField(field)!.SetValue(config, value);
            var error = Assert.Throws<ArgumentOutOfRangeException>(() => config.Validate());
            Assert.That(error!.ParamName, Is.EqualTo(field));
        }

        [Test]
        public void Validate_AcceptsBoundaries()
        {
            var config = new PlatformerConfig
            {
                WalkSpeed = PlatformerConfig.MaxSpeed,
                RunSpeed = PlatformerConfig.MaxSpeed,
                Gravity = 1,
                JumpSpeed = PlatformerConfig.MaxSpeed,
                JumpCutSpeed = 0,
                MaxFallSpeed = PlatformerConfig.MaxSpeed,
                StompBounce = PlatformerConfig.MaxSpeed,
                Lives = PlatformerConfig.MaxLives,
                TimeLimitSeconds = PlatformerConfig.MaxTimeLimitSeconds,
                StompScore = 0,
                TimeBonusPerSecond = PlatformerConfig.MaxScorePerEvent,
            };
            Assert.DoesNotThrow(() => config.Validate());
        }

        [Test]
        public void MaxSpeed_IsBelowOneTileAndSmallestBody()
        {
            Assert.That(PlatformerConfig.MaxSpeed, Is.LessThan(Units.Tile));
        }

        [Test]
        public void Levels_AreThreeWithFifteenRows()
        {
            var levels = Levels.All();
            Assert.That(levels, Has.Length.EqualTo(3));
            Assert.That(levels, Has.All.Matches<Level>(l => l.Map.Height == 15));
            Assert.That(Levels.All(), Is.Not.SameAs(levels), "毎回新しい配列を返す");
        }

        [Test]
        public void Levels_EveryKindOfEnemyAppears()
        {
            foreach (var kind in new[] { EnemyKind.Walker, EnemyKind.Hopper, EnemyKind.Thorn })
            {
                Assert.That(Levels.Meadow.Enemies, Has.Some.Matches<EnemySpawn>(e => e.Kind == kind), $"1 面に {kind}");
            }
        }

        [Test]
        public void Levels_StartIsOnGroundAndFarFromEnemies()
        {
            foreach (var level in Levels.All())
            {
                Assert.That(level.Map.IsSolid(level.StartColumn, level.StartRow - 1), Is.True, level.Name);
                foreach (var enemy in level.Enemies)
                {
                    Assert.That(enemy.Column - level.StartColumn, Is.GreaterThanOrEqualTo(6), $"{level.Name}：開始直後に敵に触れない");
                }
            }
        }

        [Test]
        public void Levels_GoalIsColumnReachableFromGround()
        {
            foreach (var level in Levels.All())
            {
                var found = false;
                for (var column = 0; column < level.Map.Width && !found; column++)
                {
                    found = level.Map[column, 2] == TileKind.Goal && level.Map.IsSolid(column, 1);
                }

                Assert.That(found, Is.True, $"{level.Name}：地面の上に旗の柱がある");
            }
        }

        [Test]
        public void Levels_StartRunDoesNotThrow()
        {
            foreach (var level in Levels.All())
            {
                var run = Run(level);
                Hold(run, RunRight, 10);
                Assert.That(run.Status, Is.EqualTo(RunStatus.Playing), level.Name);
            }
        }
    }
}
