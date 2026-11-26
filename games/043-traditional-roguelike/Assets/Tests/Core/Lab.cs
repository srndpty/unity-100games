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

        /// <summary>自機の隣で、そちらへ歩けて敵も品もないマスの方向。</summary>
        public static Direction OpenDirection(CrawlRun run, int skip = 0)
        {
            for (int d = 0; d < Directions.Count; d++)
            {
                var dir = (Direction)d;
                var q = run.Player.Position + dir.Offset();
                if (run.Grid.CanStep(run.Player.Position, dir) && run.MonsterAt(q) == null && run.ItemAt(q) == null)
                {
                    if (skip-- == 0) { return dir; }
                }
            }

            throw new System.InvalidOperationException("空いた隣がありません。");
        }

        /// <summary><paramref name="p"/> の隣の、空いていて <paramref name="p"/> へ歩ける（殴れる）マス。</summary>
        public static Vec2i NeighborOf(CrawlRun run, Vec2i p)
        {
            for (int d = 0; d < Directions.Count; d++)
            {
                var dir = (Direction)d;
                var q = p + dir.Offset();
                var back = (Direction)((d + 4) % Directions.Count);
                if (run.Grid.IsWalkable(q) && run.MonsterAt(q) == null && run.Grid.CanStep(q, back)) { return q; }
            }

            throw new System.InvalidOperationException("隣が空いていません。");
        }

        /// <summary>自機を部屋の中央（四方が床）に置き、敵と品を除いた場面。</summary>
        public static CrawlRun Center(int seed = 1)
        {
            var run = Quiet(seed);
            run.Teleport(RoomInterior(run));
            for (int d = 0; d < Directions.Count; d++) { run.SetItem(run.Player.Position + ((Direction)d).Offset(), null); }
            run.SetItem(run.Player.Position, null);
            run.Refresh();
            return run;
        }

        /// <summary>自機の隣（方向）に敵を置く。</summary>
        public static Monster Beside(CrawlRun run, MonsterKind kind, Direction direction, int hp = -1)
        {
            var m = run.AddMonster(kind, run.Player.Position + direction.Offset());
            if (hp > 0)
            {
                m.Hp = hp;
                m.MaxHp = hp;
            }

            run.Refresh();
            return m;
        }

        /// <summary>直前の操作の出来事のうち指定の種類。</summary>
        public static List<RunEvent> EventsOf(CrawlRun run, EventKind kind)
        {
            var list = new List<RunEvent>();
            foreach (var e in run.Events)
            {
                if (e.Kind == kind) { list.Add(e); }
            }

            return list;
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
