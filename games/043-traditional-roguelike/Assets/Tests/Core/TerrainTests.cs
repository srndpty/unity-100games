using System.Collections.Generic;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>地形の生成：再現性・検証・多様さ。</summary>
    public sealed class TerrainTests
    {
        private static string Rows(FloorGrid grid)
        {
            var rows = new string[grid.Height];
            for (int y = 0; y < grid.Height; y++) { rows[y] = grid.Row(y); }
            return string.Join("\n", rows);
        }

        [Test]
        public void SameSeedGivesSameTerrainOnEveryFloor()
        {
            var config = new CrawlConfig();
            for (int floor = 1; floor <= CrawlConfig.FloorCount; floor++)
            {
                var a = FloorGenerator.Generate(77, floor, config);
                var b = FloorGenerator.Generate(77, floor, config);
                Assert.That(Rows(a.Grid), Is.EqualTo(Rows(b.Grid)), $"{floor} 階");
                Assert.That(a.Start, Is.EqualTo(b.Start));
            }
        }

        [Test]
        public void DifferentPlayDoesNotChangeTheNextFloor()
        {
            // 1 階で別々の行動をしてから 2 階へ降りても、2 階の地形は同じ。
            var expected = Rows(FloorGenerator.Generate(5, 2, new CrawlConfig()).Grid);
            for (int variant = 0; variant < 3; variant++)
            {
                var run = Lab.NewRun(5);
                for (int i = 0; i < variant * 7; i++) { run.Submit(Command.Wait()); }
                run.Submit(Command.Move((Direction)variant));
                run.ClearMonsters();
                run.Teleport(run.Grid.Stairs);
                Assert.That(run.Submit(Command.Descend()).Accepted, Is.True);
                Assert.That(run.Floor, Is.EqualTo(2));
                Assert.That(Rows(run.Grid), Is.EqualTo(expected), $"変化 {variant}");
            }
        }

        [Test]
        public void FloorsDifferAcrossSeedsAndFloors()
        {
            var config = new CrawlConfig();
            var seen = new HashSet<string>();
            for (int seed = 1; seed <= 20; seed++)
            {
                for (int floor = 1; floor <= CrawlConfig.FloorCount; floor++)
                {
                    seen.Add(Rows(FloorGenerator.Generate(seed, floor, config).Grid));
                }
            }

            Assert.That(seen.Count, Is.EqualTo(100));
        }

        [Test]
        public void GeneratedFloorsAreValidWithoutHittingTheRetryLimit()
        {
            var config = new CrawlConfig();
            var maxAttempts = 0;
            for (int seed = 1; seed <= 400; seed++)
            {
                for (int floor = 1; floor <= CrawlConfig.FloorCount; floor++)
                {
                    var layout = FloorGenerator.Generate(seed, floor, config);
                    var grid = layout.Grid;
                    Assert.That(FloorGenerator.IsValid(grid, layout.Start), Is.True, $"シード {seed} {floor} 階");
                    Assert.That(grid.Rooms.Count, Is.InRange(config.MinRooms, config.MaxRooms));
                    Assert.That(grid[grid.Stairs], Is.EqualTo(Tile.Stairs));
                    Assert.That(OuterRingIsWall(grid), Is.True);
                    maxAttempts = System.Math.Max(maxAttempts, layout.Attempts);
                }
            }

            // 作り方で通路がつながるので、検証は 1 回目で通る（引き直しは保険）。
            Assert.That(maxAttempts, Is.EqualTo(1));
        }

        [TestCase(30, 21, 3, 3)]
        [TestCase(30, 21, 9, 9)]
        [TestCase(120, 60, 9, 9)]
        [TestCase(48, 27, 3, 3)]
        public void BoundarySizesStillGenerateValidFloors(int width, int height, int minRooms, int maxRooms)
        {
            var config = new CrawlConfig { Width = width, Height = height, MinRooms = minRooms, MaxRooms = maxRooms, MinExtraCorridors = 0, MaxExtraCorridors = 4 };
            config.Validate();
            for (int seed = 1; seed <= 60; seed++)
            {
                var layout = FloorGenerator.Generate(seed, 1, config);
                Assert.That(FloorGenerator.IsValid(layout.Grid, layout.Start), Is.True);
                Assert.That(layout.Grid.Rooms.Count, Is.InRange(minRooms, maxRooms));
            }
        }

        [Test]
        public void ValidationRejectsDisconnectedFloorAndStairsInStartRoom()
        {
            var joined = FloorGrid.Parse(
                "########",
                "#..++.>#",
                "########");
            Assert.That(FloorGenerator.IsValid(joined, new Vec2i(1, 1)), Is.True);
            Assert.That(FloorGenerator.IsValid(joined, new Vec2i(5, 1)), Is.False, "階段と開始が同じ部屋");
            var split = FloorGrid.Parse(
                "########",
                "#..#+.>#",
                "########");
            Assert.That(FloorGenerator.IsValid(split, new Vec2i(1, 1)), Is.False, "つながっていない");
        }

        [Test]
        public void CornerCuttingIsNotAllowed()
        {
            var grid = FloorGrid.Parse(
                "####",
                "#.##",
                "##.#",
                "####");
            Assert.That(grid.CanStep(new Vec2i(1, 1), Direction.SouthEast), Is.False);
            Assert.That(grid.Distances(new Vec2i(1, 1))[grid.Index(new Vec2i(2, 2))], Is.EqualTo(-1));
            var open = FloorGrid.Parse(
                "####",
                "#..#",
                "#..#",
                "####");
            Assert.That(open.CanStep(new Vec2i(1, 1), Direction.SouthEast), Is.True);
        }

        private static bool OuterRingIsWall(FloorGrid grid)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.IsWalkable(new Vec2i(x, 0)) || grid.IsWalkable(new Vec2i(x, grid.Height - 1))) { return false; }
            }

            for (int y = 0; y < grid.Height; y++)
            {
                if (grid.IsWalkable(new Vec2i(0, y)) || grid.IsWalkable(new Vec2i(grid.Width - 1, y))) { return false; }
            }

            return true;
        }
    }
}
