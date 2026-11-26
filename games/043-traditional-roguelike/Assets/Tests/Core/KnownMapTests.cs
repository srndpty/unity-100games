using IntGeometry;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>既知マップ：視界とは別に、見た地形と最後に見た品を覚える。</summary>
    public sealed class KnownMapTests
    {
        [Test]
        public void OnlySeenTilesAreKnownAndTheyStayKnownAfterLeaving()
        {
            var run = Lab.Quiet(3);
            var start = run.Player.Position;
            for (int y = 0; y < run.Grid.Height; y++)
            {
                for (int x = 0; x < run.Grid.Width; x++)
                {
                    var p = new Vec2i(x, y);
                    Assert.That(run.View.IsKnown(p), Is.EqualTo(run.View.IsVisible(p)), "最初は見えているマスだけ既知");
                    Assert.That(run.View.TileAt(p).HasValue, Is.EqualTo(run.View.IsVisible(p)));
                }
            }

            // 別の部屋へ移ると、元の部屋は見えないが既知のまま。
            var other = run.Grid.Rooms[(run.Grid.RoomAt(start) + 1) % run.Grid.Rooms.Count].Center;
            run.Teleport(other);
            if (!run.View.IsVisible(start))
            {
                Assert.That(run.View.IsKnown(start), Is.True);
                Assert.That(run.View.TileAt(start), Is.EqualTo(run.Grid[start]));
            }
        }

        [Test]
        public void ItemMemoryIsStaleUntilSeenAgain()
        {
            var run = Lab.Quiet(4);
            var spot = run.Player.Position + Direction.East.Offset();
            if (!run.Grid.IsWalkable(spot)) { spot = run.Player.Position + Direction.West.Offset(); }
            run.SetItem(spot, ItemKind.Bread);
            run.Refresh();
            Assert.That(run.View.ItemAt(spot), Is.EqualTo(ItemKind.Bread));

            // 見えない所へ移り、その間に品が消えても記憶は残る。
            var far = FarUnseenRoomTile(run, spot);
            run.Teleport(far);
            run.SetItem(spot, null);
            run.Refresh();
            Assert.That(run.View.IsVisible(spot), Is.False);
            Assert.That(run.View.ItemAt(spot), Is.EqualTo(ItemKind.Bread), "記憶は古いまま");

            // 戻って見ると記憶が今の品（なし）に変わる。
            run.Teleport(spot);
            Assert.That(run.View.ItemAt(spot), Is.Null);
            run.Teleport(far);
            Assert.That(run.View.ItemAt(spot), Is.Null);
        }

        [Test]
        public void MapScrollRevealsTerrainButNotItemsOrMonsters()
        {
            var run = Lab.Quiet(6);
            var far = FarUnseenRoomTile(run, run.Player.Position);
            run.SetItem(far, ItemKind.Stone);
            run.AddMonster(MonsterKind.Golem, far + Direction.North.Offset() == run.Player.Position ? far : NeighborFloor(run, far));
            run.Player.Inventory.Add(ItemKind.MapScroll);
            run.Refresh();
            Assert.That(run.Submit(Command.Use(0)).Accepted, Is.True);
            for (int y = 0; y < run.Grid.Height; y++)
            {
                for (int x = 0; x < run.Grid.Width; x++)
                {
                    var p = new Vec2i(x, y);
                    if (run.Grid.IsWalkable(p)) { Assert.That(run.View.IsKnown(p), Is.True, $"床 {p}"); }
                }
            }

            Assert.That(run.View.IsVisible(far), Is.False);
            Assert.That(run.View.ItemAt(far), Is.Null, "品は明かさない");
            foreach (var m in run.View.Monsters) { Assert.That(run.View.IsVisible(m.Position), Is.True, "見えない敵は出さない"); }
            Assert.That(run.View.TileAt(run.Grid.Stairs), Is.EqualTo(Tile.Stairs), "階段は分かる");
        }

        internal static Vec2i FarUnseenRoomTile(CrawlRun run, Vec2i from)
        {
            foreach (var room in run.Grid.Rooms)
            {
                var c = room.Center;
                if (run.Grid[c] != Tile.Room) { continue; }
                var visible = new bool[run.Grid.Width * run.Grid.Height];
                Fov.Compute(run.Grid, c, run.Config.ViewRadius, visible);
                if (!visible[run.Grid.Index(from)] && !run.IsVisible(c)) { return c; }
            }

            throw new System.InvalidOperationException("見えない部屋がありません。");
        }

        private static Vec2i NeighborFloor(CrawlRun run, Vec2i p)
        {
            for (int d = 0; d < Directions.Count; d++)
            {
                var q = p + ((Direction)d).Offset();
                if (run.Grid.IsWalkable(q) && q != run.Player.Position) { return q; }
            }

            return p;
        }
    }
}
