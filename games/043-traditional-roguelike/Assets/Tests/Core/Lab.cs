using System.Collections.Generic;
using IntGeometry;

namespace Game.Core.Tests
{
    /// <summary>テストの道具：場面を作る。</summary>
    internal static class Lab
    {
        public static CrawlRun NewRun(int seed = 1, CrawlConfig? config = null) => new(config ?? new CrawlConfig(), seed);

        /// <summary>敵を全て除き、足元・周りの品も除いた静かな場面。</summary>
        public static CrawlRun Quiet(int seed = 1)
        {
            var run = NewRun(seed);
            run.ClearMonsters();
            run.Refresh();
            return run;
        }

        /// <summary>自機が歩ける床で、指定の方向が壁になっている場所を探す。</summary>
        public static Vec2i FloorFacingWall(CrawlRun run, Direction direction)
        {
            var grid = run.Grid;
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var p = new Vec2i(x, y);
                    if (grid.Rooms.Count > 0 && grid.RoomAt(p) >= 0 && grid[p] == Tile.Room && !grid.IsWalkable(p + direction.Offset()))
                    {
                        return p;
                    }
                }
            }

            throw new System.InvalidOperationException("見つかりません。");
        }

        /// <summary>斜めの先は床だが片方の脇が壁（角抜け）になる場所を探す。</summary>
        public static bool TryCornerCut(CrawlRun run, out Vec2i position, out Direction direction)
        {
            var grid = run.Grid;
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var p = new Vec2i(x, y);
                    if (!grid.IsWalkable(p)) { continue; }
                    foreach (var d in new[] { Direction.NorthEast, Direction.SouthEast, Direction.SouthWest, Direction.NorthWest })
                    {
                        if (grid.IsWalkable(p + d.Offset()) && !grid.CanStep(p, d))
                        {
                            position = p;
                            direction = d;
                            return true;
                        }
                    }
                }
            }

            position = default;
            direction = default;
            return false;
        }

        /// <summary>部屋の中央付近の、四方が床のマス（部屋 index の中から）。</summary>
        public static Vec2i RoomInterior(CrawlRun run, int roomIndex = -1)
        {
            var grid = run.Grid;
            for (int r = 0; r < grid.Rooms.Count; r++)
            {
                if (roomIndex >= 0 && r != roomIndex) { continue; }
                var room = grid.Rooms[r];
                if (room.Width >= 3 && room.Height >= 3) { return room.Center; }
            }

            throw new System.InvalidOperationException("3×3 以上の部屋がありません。");
        }

        /// <summary>状態の要約（無効操作で何も変わらないことの比較用）。</summary>
        public static string Snapshot(CrawlRun run)
        {
            var parts = new List<string>
            {
                run.Hash().ToString(System.Globalization.CultureInfo.InvariantCulture),
                run.Scheduler.Now.ToString(System.Globalization.CultureInfo.InvariantCulture),
                run.Scheduler.TimeOf(TurnScheduler.PlayerId).ToString(System.Globalization.CultureInfo.InvariantCulture),
                run.ActionRng.State.ToString(System.Globalization.CultureInfo.InvariantCulture),
                run.Player.Satiety.ToString(System.Globalization.CultureInfo.InvariantCulture),
                run.Player.Actions.ToString(System.Globalization.CultureInfo.InvariantCulture),
                run.Player.Position.ToString(),
            };
            foreach (var m in run.Monsters) { parts.Add($"{m.Id}:{m.Position}:{m.Hp}"); }
            return string.Join("|", parts);
        }
    }
}
