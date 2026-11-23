using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using IntGeometry;

namespace Game.Core
{
    /// <summary>警備員 1 人の配置。巡回点の最初のマスから、<see cref="StartFacing"/> を向いて始める。</summary>
    public sealed class GuardPlan
    {
        public GuardPlan(string name, int startFacing, params Waypoint[] route)
        {
            if (string.IsNullOrEmpty(name)) { throw new ArgumentException("名前を指定してください。", nameof(name)); }
            if (route == null) { throw new ArgumentNullException(nameof(route)); }
            if (route.Length == 0) { throw new ArgumentException("巡回点がありません。", nameof(route)); }
            Name = name;
            StartFacing = Trig.Normalize(startFacing);
            Route = Array.AsReadOnly((Waypoint[])route.Clone());
        }

        public string Name { get; }

        public int StartFacing { get; }

        public ReadOnlyCollection<Waypoint> Route { get; }

        public Cell Start => Route[0].Cell;
    }

    /// <summary>
    /// 施設（地図・入口兼出口・目的物・警備員の配置）。作るときに、歩けるマスがすべてつながっていること、
    /// 巡回点が歩けるマスにあることを確かめる。
    /// </summary>
    public sealed class Facility
    {
        public Facility(FacilityMap map, params GuardPlan[] guards)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            if (guards == null) { throw new ArgumentNullException(nameof(guards)); }
            Exit = map.Find(TileKind.Exit);
            Objective = map.Find(TileKind.Objective);
            RequireConnected(map, Exit);
            foreach (var guard in guards)
            {
                if (guard == null) { throw new ArgumentException("警備員が null です。", nameof(guards)); }
                foreach (var waypoint in guard.Route)
                {
                    if (!map.IsWalkable(waypoint.Cell)) { throw new ArgumentException($"{guard.Name} の巡回点 {waypoint.Cell} が歩けるマスにありません。", nameof(guards)); }
                }
            }

            Guards = Array.AsReadOnly((GuardPlan[])guards.Clone());
        }

        public FacilityMap Map { get; }

        public Cell Exit { get; }

        public Cell Objective { get; }

        public ReadOnlyCollection<GuardPlan> Guards { get; }

        /// <summary>本番の施設の地図（行 0 が最下段。最初の行が最上段）。</summary>
        public static readonly IReadOnlyList<string> WardRows = Array.AsReadOnly(new[]
        {
            "################################", // 17
            "#.........#.........#..........#", // 16
            "#...........................O..#", // 15
            "#..X...X..#...XXX...#..........#", // 14
            "#.........#.........#..........#", // 13
            "#.........#.........#XX....XX..#", // 12
            "#####.#######.#########..#######", // 11
            "#..............................#", // 10
            "#...XX.......XXXX.......XX.....#", // 9
            "#..............................#", // 8
            "#..............................#", // 7
            "#######.#########.######.#######", // 6
            "#.......#.........#............#", // 5
            "#..XX...#...X.X...#....XXX.....#", // 4
            "#.......#.........#............#", // 3
            "#.......#.........#............#", // 2
            "#E......#.........#............#", // 1
            "################################", // 0
        });

        /// <summary>
        /// 本番の施設 HUSH WARD。南西の入口から入り、北東の資料室の機密ファイルを取って入口へ戻る。
        /// <list type="bullet">
        /// <item>A：中央の廊下を東西に往復し、両端で廊下を見回す。</item>
        /// <item>B：資料室の正面の扉の前に立ち、扉のほうを見回す見張り。</item>
        /// <item>C：北の中央の部屋を東西に往復する。</item>
        /// </list>
        /// </summary>
        public static Facility Ward()
        {
            var rows = new string[WardRows.Count];
            for (var i = 0; i < rows.Length; i++) { rows[i] = WardRows[i]; }
            return new Facility(
                FacilityMap.Parse(rows),
                new GuardPlan("A", Trig.Right, new Waypoint(new Cell(2, 8), 90, Trig.Right), new Waypoint(new Cell(29, 8), 90, Trig.Left)),
                new GuardPlan("B", Trig.Down, new Waypoint(new Cell(24, 13), 240, Trig.Down)),
                new GuardPlan("C", Trig.Right, new Waypoint(new Cell(12, 13), 60, Trig.Right), new Waypoint(new Cell(18, 13), 60, Trig.Left)));
        }

        private static void RequireConnected(FacilityMap map, Cell from)
        {
            var distance = new int[map.Count];
            var queue = new int[map.Count];
            var reached = PathField.Flood(map, from, int.MaxValue, distance, queue);
            var walkable = 0;
            for (var i = 0; i < map.Count; i++)
            {
                if (map.IsWalkable(map.CellAt(i))) { walkable++; }
            }

            if (reached != walkable) { throw new ArgumentException($"入口から歩いて行けないマスがあります（{walkable - reached} マス）。", nameof(map)); }
        }
    }
}
