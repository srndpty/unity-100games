using System.Text;
using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>視界（対称シャドウキャスティング）。</summary>
    public sealed class FovTests
    {
        private static bool[] Compute(FloorGrid grid, Vec2i origin, int radius)
        {
            var visible = new bool[grid.Width * grid.Height];
            Fov.Compute(grid, origin, radius, visible);
            return visible;
        }

        private static FloorGrid OpenRoom(int size)
        {
            var rows = new string[size];
            for (int y = 0; y < size; y++)
            {
                var sb = new StringBuilder();
                for (int x = 0; x < size; x++) { sb.Append(x == 0 || y == 0 || x == size - 1 || y == size - 1 ? '#' : '.'); }
                rows[y] = sb.ToString();
            }

            return FloorGrid.Parse(rows);
        }

        [Test]
        public void OpenRoomShowsExactlyTheDiscWithinRadius()
        {
            var grid = OpenRoom(31);
            var origin = new Vec2i(15, 15);
            const int r = 8;
            var visible = Compute(grid, origin, r);
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var dx = x - origin.X;
                    var dy = y - origin.Y;
                    var inside = (dx * dx) + (dy * dy) <= (r * r) + r;
                    Assert.That(visible[grid.Index(new Vec2i(x, y))], Is.EqualTo(inside), $"({x}, {y})");
                }
            }
        }

        [Test]
        public void PillarCastsShadowAndIsItselfVisible()
        {
            var grid = FloorGrid.Parse(
                "###########",
                "#.........#",
                "#.........#",
                "#....#....#",
                "#.........#",
                "#.........#",
                "#.........#",
                "###########");
            var visible = Compute(grid, new Vec2i(5, 6), 10);
            Assert.That(visible[grid.Index(new Vec2i(5, 3))], Is.True, "柱そのもの");
            Assert.That(visible[grid.Index(new Vec2i(5, 2))], Is.False, "柱の真後ろ");
            Assert.That(visible[grid.Index(new Vec2i(5, 1))], Is.False);
            Assert.That(visible[grid.Index(new Vec2i(1, 1))], Is.True, "斜めの遠い角");
            Assert.That(visible[grid.Index(new Vec2i(0, 0))], Is.True, "外壁の角");
        }

        [Test]
        public void WallsBlockCorridorsAndRoomsBehindThem()
        {
            var grid = FloorGrid.Parse(
                "#########",
                "#...#...#",
                "#...+...#",
                "#...#...#",
                "#########");
            var visible = Compute(grid, new Vec2i(1, 1), 10);
            Assert.That(visible[grid.Index(new Vec2i(4, 2))], Is.True, "戸口");
            Assert.That(visible[grid.Index(new Vec2i(4, 1))], Is.True, "仕切りの壁");
            Assert.That(visible[grid.Index(new Vec2i(7, 1))], Is.False, "仕切りの向こうの同じ行");
            Assert.That(visible[grid.Index(new Vec2i(5, 2))], Is.True, "戸口の先");
        }

        [Test]
        public void OriginIsAlwaysVisibleAndAdjacentTilesAreVisible()
        {
            var grid = FloorGrid.Parse(
                "#####",
                "#.#.#",
                "##.##",
                "#.#.#",
                "#####");
            var visible = Compute(grid, new Vec2i(2, 2), 1);
            for (int d = 0; d < Directions.Count; d++)
            {
                Assert.That(visible[grid.Index(new Vec2i(2, 2) + ((Direction)d).Offset())], Is.True, ((Direction)d).ToString());
            }

            Assert.That(visible[grid.Index(new Vec2i(2, 2))], Is.True);
        }

        [Test]
        public void VisibilityBetweenFloorTilesIsSymmetricOnRandomMaps()
        {
            var rng = new GameRng(9);
            for (int map = 0; map < 25; map++)
            {
                const int size = 16;
                var rows = new string[size];
                for (int y = 0; y < size; y++)
                {
                    var sb = new StringBuilder();
                    for (int x = 0; x < size; x++)
                    {
                        var edge = x == 0 || y == 0 || x == size - 1 || y == size - 1;
                        sb.Append(edge || rng.Next(100) < 28 ? '#' : '.');
                    }

                    rows[y] = sb.ToString();
                }

                var grid = FloorGrid.Parse(rows);
                var views = new bool[size * size][];
                for (int i = 0; i < size * size; i++)
                {
                    var p = new Vec2i(i % size, i / size);
                    if (grid.IsWalkable(p)) { views[i] = Compute(grid, p, 7); }
                }

                for (int a = 0; a < size * size; a++)
                {
                    if (views[a] == null) { continue; }
                    for (int b = 0; b < size * size; b++)
                    {
                        if (views[b] == null) { continue; }
                        Assert.That(views[a][b], Is.EqualTo(views[b][a]), $"地図 {map}：{a} と {b}");
                    }
                }
            }
        }

        [Test]
        public void StandingInsideAGeneratedRoomShowsTheWholeRoomWithinRadius()
        {
            var config = new CrawlConfig();
            for (int seed = 1; seed <= 30; seed++)
            {
                var layout = FloorGenerator.Generate(seed, 1, config);
                var grid = layout.Grid;
                var visible = Compute(grid, layout.Start, config.ViewRadius);
                var room = grid.Rooms[grid.RoomAt(layout.Start)];
                for (int y = room.Y0; y <= room.Y1; y++)
                {
                    for (int x = room.X0; x <= room.X1; x++)
                    {
                        var dx = x - layout.Start.X;
                        var dy = y - layout.Start.Y;
                        if ((dx * dx) + (dy * dy) > (config.ViewRadius * config.ViewRadius) + config.ViewRadius) { continue; }
                        Assert.That(visible[grid.Index(new Vec2i(x, y))], Is.True, $"シード {seed} ({x}, {y})");
                    }
                }
            }
        }

        [Test]
        public void WrongBufferSizeIsRejected()
        {
            var grid = OpenRoom(5);
            Assert.That(() => Fov.Compute(grid, new Vec2i(2, 2), 3, new bool[3]), Throws.ArgumentException);
        }
    }
}
