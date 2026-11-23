using System.Collections.Generic;
using IntGeometry;
using TileMotion;

namespace Game.Core.Tests
{
    /// <summary>テストの部品。</summary>
    internal static class TestKit
    {
        public const int T = Units.Tile;

        /// <summary>マスの中心。</summary>
        public static Vec2i C(int column, int row) => new Cell(column, row).Center;

        public static IntruderInput Move(int x, int y) => new(x, y, false, false);

        public static IntruderInput Sneak(int x, int y) => new(x, y, false, true);

        public static IntruderInput Run(int x, int y) => new(x, y, true, false);

        /// <summary>何も起きない広い部屋（20×9、入口は左下・目的物は右上）。</summary>
        public static string[] OpenRoom() => new[]
        {
            "####################",
            "#.................O#",
            "#..................#",
            "#..................#",
            "#..................#",
            "#..................#",
            "#..................#",
            "#E.................#",
            "####################",
        };

        /// <summary>
        /// <paramref name="rows"/> の地図に、指定のマスに立って <paramref name="facing"/> を向き続ける見張りを 1 人置いた施設。
        /// 見回しの振れ幅は設定の <see cref="WardConfig.LookSweep"/>（テストでは 0 にすることが多い）。
        /// </summary>
        public static Facility WithSentry(string[] rows, Cell sentry, int facing)
        {
            return new Facility(FacilityMap.Parse(rows), new GuardPlan("S", facing, new Waypoint(sentry, 600, facing)));
        }

        public static Facility WithGuards(string[] rows, params GuardPlan[] guards) => new(FacilityMap.Parse(rows), guards);

        /// <summary>見回しで向きを振らない設定。</summary>
        public static WardConfig Steady()
        {
            var config = new WardConfig { LookSweep = 0 };
            config.Validate();
            return config;
        }

        public static void Repeat(Infiltration match, IntruderInput input, int ticks)
        {
            for (var i = 0; i < ticks && !match.IsOver; i++) { match.Tick(input); }
        }

        /// <summary>捜索で中心の右隣（なければ中心）を選ぶだけの領域（判断のテスト用、地図なし）。</summary>
        public sealed class RightOfCenter : ISearchArea
        {
            public List<Cell> Picked { get; } = new();

            public Cell Pick(Cell center, SeededRandom random)
            {
                var cell = new Cell(center.Column + 1, center.Row);
                Picked.Add(cell);
                return cell;
            }
        }
    }
}
