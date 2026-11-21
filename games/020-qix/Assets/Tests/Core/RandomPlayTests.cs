using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>乱数の入力で多数の試合を進め、毎 Tick の不変条件を確かめる。</summary>
    public sealed class RandomPlayTests
    {
        private static QixConfig Arena() => new()
        {
            Width = 30,
            Height = 20,
            PlayerStepTicks = 2,
            Lives = QixConfig.MaxLives,
            TargetPercent = QixConfig.MaxTargetPercent,
        };

        [Test]
        public void InvariantsHoldEveryTick([Range(0, 39)] int seed)
        {
            var game = new QixGame(Arena(), seed);
            var input = new SeededRandom(seed + 1000);
            var overlapped = new List<int>();
            var claimed = 0;
            var closes = 0;
            for (var tick = 0; tick < 4000 && !game.IsOver; tick++)
            {
                Drive(game, input);
                game.Tick();
                if (game.TakeEvents().Has(GameEventKind.Closed)) { closes++; }

                Assert.That(game.ClaimedCells, Is.GreaterThanOrEqualTo(claimed), $"占有は減らない（Tick {tick}）");
                claimed = game.ClaimedCells;
                Assert.That(game.Score, Is.EqualTo(game.ClaimedCells));

                var grid = Snapshot(game);
                game.Enemy.OverlappedCells(grid, overlapped);
                foreach (var cell in overlapped)
                {
                    Assert.That(grid[cell], Is.EqualTo(Cell.Open), $"敵は海だけに重なる（Tick {tick}）");
                }

                var here = game.CellAt(game.PlayerX, game.PlayerY);
                if (game.IsDrawing)
                {
                    Assert.That(here, Is.EqualTo(Cell.Trail));
                }
                else
                {
                    Assert.That(game.IsBoundary(game.PlayerX, game.PlayerY), Is.True, $"線がないときは縁にいる（Tick {tick}）");
                    Assert.That(game.Trail.Count, Is.Zero);
                }
            }

            Assert.That(closes, Is.GreaterThan(0), "乱数の入力でも何度かは閉じる");
        }

        [Test]
        public void SameSeedAndInputsGiveSameGame()
        {
            Assert.That(Play(7), Is.EqualTo(Play(7)));
            Assert.That(Play(7), Is.Not.EqualTo(Play(8)));
        }

        /// <summary>整数だけで進むので、.NET と Mono（Unity）で同じ値になる。</summary>
        [Test]
        public void FinalHashIsFixedAcrossRuntimes()
        {
            Assert.That(Play(20), Is.EqualTo(2084818581u));
        }

        [Test]
        public void EnemySpawnsInsideTheSeaAndVariesBySeed()
        {
            var config = new QixConfig();
            var positions = new HashSet<(int, int)>();
            var overlapped = new List<int>();
            for (var seed = 0; seed < 200; seed++)
            {
                var game = new QixGame(config, seed);
                game.Enemy.OverlappedCells(Snapshot(game), overlapped);
                Assert.That(overlapped, Is.Not.Empty);
                foreach (var cell in overlapped)
                {
                    Assert.That(game.CellAt(cell), Is.EqualTo(Cell.Open), $"シード {seed}");
                }

                positions.Add((game.Enemy.X, game.Enemy.Y));
                Assert.That(game.Enemy.VelocityX, Is.Not.Zero);
                Assert.That(game.Enemy.VelocityY, Is.Not.Zero);
            }

            Assert.That(positions.Count, Is.GreaterThan(190));
        }

        private static uint Play(int seed)
        {
            var game = new QixGame(Arena(), seed);
            var input = new SeededRandom(seed + 1000);
            for (var tick = 0; tick < 3000 && !game.IsOver; tick++)
            {
                Drive(game, input);
                game.Tick();
            }

            return game.Hash();
        }

        /// <summary>ときどき向きを変え、たいてい描画キーを押している入力。</summary>
        private static void Drive(QixGame game, SeededRandom input)
        {
            if (input.Next(8) == 0)
            {
                game.SetInput((Direction)input.Next(5), input.Next(4) != 0);
            }
        }

        /// <summary>試合の盤を写した CellGrid（敵の重なりを数えるため）。</summary>
        private static CellGrid Snapshot(QixGame game)
        {
            var grid = new CellGrid(game.Width, game.Height);
            for (var i = 0; i < grid.CellCount; i++)
            {
                grid.Set(i, game.CellAt(i));
            }

            return grid;
        }
    }

    public sealed class QixConfigTests
    {
        [Test]
        public void DefaultIsValid()
        {
            Assert.DoesNotThrow(() => new QixConfig().Validate());
        }

        [TestCase(nameof(QixConfig.Width), CellGrid.MinSize - 1)]
        [TestCase(nameof(QixConfig.Height), CellGrid.MaxSize + 1)]
        [TestCase(nameof(QixConfig.PlayerStepTicks), 0)]
        [TestCase(nameof(QixConfig.PlayerStepTicks), QixConfig.MaxStepTicks + 1)]
        [TestCase(nameof(QixConfig.EnemyRadius), QixConfig.MinEnemyRadius - 1)]
        [TestCase(nameof(QixConfig.EnemyRadius), 27 * QixConfig.SubCell)]
        [TestCase(nameof(QixConfig.EnemySpeed), 0)]
        [TestCase(nameof(QixConfig.EnemySpeed), QixConfig.MaxEnemySpeed + 1)]
        [TestCase(nameof(QixConfig.EnemyTurnMinTicks), 0)]
        [TestCase(nameof(QixConfig.EnemyTurnMaxTicks), 39)]
        [TestCase(nameof(QixConfig.Lives), 0)]
        [TestCase(nameof(QixConfig.Lives), QixConfig.MaxLives + 1)]
        [TestCase(nameof(QixConfig.TargetPercent), 0)]
        [TestCase(nameof(QixConfig.TargetPercent), 100)]
        public void RejectsOutOfRange(string field, int value)
        {
            var config = new QixConfig();
            typeof(QixConfig).GetField(field)!.SetValue(config, value);

            Assert.Throws<System.ArgumentOutOfRangeException>(config.Validate);
        }

        [Test]
        public void EnemyMustFitInsideTheSea()
        {
            var config = new QixConfig { Width = 10, Height = 6 };
            config.EnemyRadius = (4 * QixConfig.SubCell - 1) / 2;
            Assert.DoesNotThrow(config.Validate);

            config.EnemyRadius++;
            Assert.Throws<System.ArgumentOutOfRangeException>(config.Validate);
        }

        [Test]
        public void SmallestBoardWithLargestEnemyStillSpawns([Range(0, 49)] int seed)
        {
            var config = new QixConfig { Width = CellGrid.MinSize, Height = CellGrid.MinSize };
            config.EnemyRadius = (3 * QixConfig.SubCell - 1) / 2;

            var game = new QixGame(config, seed);

            Assert.That(game.Enemy.X - game.Enemy.Radius, Is.GreaterThanOrEqualTo(QixConfig.SubCell));
            Assert.That(game.Enemy.X + game.Enemy.Radius, Is.LessThanOrEqualTo(4 * QixConfig.SubCell));
        }
    }
}
