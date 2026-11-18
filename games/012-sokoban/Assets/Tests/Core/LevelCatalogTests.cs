using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class LevelCatalogTests
    {
        // 各問の解答例。ゲーム本体には解答機能を持たず、全問がクリアできることの確認にだけ使う。
        private static readonly string[] _solutions =
        {
            "RR",
            "RRRULDLUUDRRUULLL",
            "UURRDULLDDLULUR",
            "ULLDLURRRRRDRU",
            "ULULLDRRLDDRRURURURRDLL",
            "RRRULURDRRUULDLLULLDRDDLLUR",
            "LULULLDDDDRRRUURRDRRULLLULDDULU",
            "URRRDRULLLUULLDLDRRRRRRDRULLLLUULDLDRRRRR",
            "RUULULDDDRRRLUURURRDLLLLULDLLDDRRRRUURRDDLDLUDDRRRULL",
            "RUULUURDRRRLDRRRRDRRUULDUULLLDLDDLDLLUULURRRRDRRRDDRRULRUULDUULLLDLDRRRLLLDLDRRRR",
        };

        private static IEnumerable<int> Levels => Enumerable.Range(0, 10);

        [Test]
        public void Catalog_HasTenNamedLevels()
        {
            Assert.That(LevelCatalog.Count, Is.EqualTo(10));
            Assert.That(LevelCatalog.Names.Count, Is.EqualTo(10));
            Assert.That(LevelCatalog.Names.Distinct().Count(), Is.EqualTo(10));
            Assert.That(LevelCatalog.Names, Has.None.Empty);
        }

        [TestCaseSource(nameof(Levels))]
        public void Solution_ClearsTheLevelOnItsLastMove(int level)
        {
            var game = new SokobanGame(LevelCatalog.Maps[level]);
            var solution = _solutions[level];

            Assert.That(LevelText.Play(game, solution), Is.EqualTo(MoveResult.Cleared));
            Assert.That(game.MoveCount, Is.EqualTo(solution.Length));
        }

        [TestCaseSource(nameof(Levels))]
        public void Solution_ThenUndoAllBeforeTheLastMoveReturnsToTheStart(int level)
        {
            var map = LevelCatalog.Maps[level];
            var game = new SokobanGame(map);
            var solution = _solutions[level];
            LevelText.Play(game, solution.Substring(0, solution.Length - 1));

            while (game.Undo()) { }

            Assert.That(game.Board, Is.EqualTo(map.Initial));
            Assert.That(game.PushCount, Is.Zero);
        }

        [TestCaseSource(nameof(Levels))]
        public void Level_FitsTheBoardAndHasEnoughBoxes(int level)
        {
            var map = LevelCatalog.Maps[level];
            Assert.That(map.Width, Is.LessThanOrEqualTo(LevelMap.MaxWidth));
            Assert.That(map.Height, Is.LessThanOrEqualTo(LevelMap.MaxHeight));
            Assert.That(map.Goals.Count, Is.EqualTo(map.Initial.Boxes.Count));
        }

        [Test]
        public void Levels_AreAllDifferent()
        {
            var layouts = LevelCatalog.Maps.Select(Describe).ToList();
            Assert.That(layouts.Distinct().Count(), Is.EqualTo(layouts.Count));
        }

        [Test]
        public void BoxCount_GrowsWithTheLevelNumber()
        {
            var boxes = LevelCatalog.Maps.Select(map => map.Initial.Boxes.Count).ToList();
            Assert.That(boxes, Is.Ordered);
            Assert.That(boxes.First(), Is.EqualTo(1));
            Assert.That(boxes.Last(), Is.EqualTo(4));
        }

        [Test]
        public void PublicLists_CannotBeWrittenThroughACast()
        {
            var map = LevelCatalog.Maps[0];
            AssertReadOnly(map.Initial.Boxes, new Cell(0, 0));
            AssertReadOnly(new SokobanGame(map).Board.Boxes, new Cell(0, 0));
            AssertReadOnly(map.Goals, new Cell(0, 0));
            AssertReadOnly(LevelCatalog.Maps, map);
            AssertReadOnly(LevelCatalog.Names, "書き換え");
        }

        private static void AssertReadOnly<T>(IReadOnlyList<T> list, T value)
        {
            Assert.That(list, Is.Not.InstanceOf<T[]>());
            Assert.That(list, Is.Not.InstanceOf<List<T>>());
            Assert.Throws<System.NotSupportedException>(() => ((IList<T>)list)[0] = value);
        }

        private static string Describe(LevelMap map)
        {
            var cells = new List<string>();
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    var cell = new Cell(x, y);
                    cells.Add(map.IsWall(cell) ? "#" : map.IsGoal(cell) ? "." : map.Initial.HasBox(cell) ? "$" : " ");
                }
            }

            return map.Width + "x" + map.Height + string.Concat(cells);
        }
    }
}
