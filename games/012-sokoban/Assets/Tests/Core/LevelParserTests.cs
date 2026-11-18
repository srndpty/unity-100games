using System;
using System.Linq;
using NUnit.Framework;

namespace Game.Core.Tests
{
    public sealed class LevelParserTests
    {
        [Test]
        public void Parse_SeparatesEachLayer()
        {
            var map = LevelText.Map(
                "######",
                "#@$. #",
                "######");

            Assert.That(map.Width, Is.EqualTo(6));
            Assert.That(map.Height, Is.EqualTo(3));
            Assert.That(map.IsWall(new Cell(0, 0)), Is.True);
            Assert.That(map.IsFloor(new Cell(0, 0)), Is.False);
            Assert.That(map.IsFloor(new Cell(4, 1)), Is.True);
            Assert.That(map.IsGoal(new Cell(3, 1)), Is.True);
            Assert.That(map.IsFloor(new Cell(3, 1)), Is.True, "目標は床でもある");
            Assert.That(map.Initial.Player, Is.EqualTo(new Cell(1, 1)));
            Assert.That(map.Initial.Boxes, Is.EqualTo(new[] { new Cell(2, 1) }));
        }

        [Test]
        public void Parse_BoxOnGoalAndPlayerOnGoalKeepTheGoalLayer()
        {
            var map = LevelText.Map(
                "######",
                "#+*$ #",
                "######");

            Assert.That(map.IsGoal(new Cell(1, 1)), Is.True);
            Assert.That(map.IsGoal(new Cell(2, 1)), Is.True);
            Assert.That(map.Goals.Count, Is.EqualTo(2));
            Assert.That(map.Initial.Player, Is.EqualTo(new Cell(1, 1)));
            Assert.That(map.Initial.HasBox(new Cell(2, 1)), Is.True);
        }

        [Test]
        public void Parse_SpaceOutsideTheWallsIsNotFloor()
        {
            var map = LevelText.Map(
                "  #####",
                "###@$.#",
                "#######");

            Assert.That(map.IsFloor(new Cell(0, 0)), Is.False);
            Assert.That(map.IsWall(new Cell(0, 0)), Is.False);
        }

        [Test]
        public void Parse_ShortRowsAreOutsideTheBoard()
        {
            var map = LevelText.Map(
                "#####",
                "#@$.#",
                "#####",
                "###");

            Assert.That(map.Width, Is.EqualTo(5));
            Assert.That(map.IsFloor(new Cell(4, 3)), Is.False);
        }

        [Test]
        public void Queries_OutsideTheBoardAreFalse()
        {
            var map = LevelText.Map("#####", "#@$.#", "#####");
            foreach (var cell in new[] { new Cell(-1, 0), new Cell(0, -1), new Cell(5, 0), new Cell(0, 3) })
            {
                Assert.That(map.IsWall(cell), Is.False);
                Assert.That(map.IsFloor(cell), Is.False);
                Assert.That(map.IsGoal(cell), Is.False);
            }
        }

        [Test]
        public void Parse_AcceptsTheMaximumSize()
        {
            var wall = new string('#', LevelMap.MaxWidth);
            var inner = "#" + new string(' ', LevelMap.MaxWidth - 2) + "#";
            var rows = Enumerable.Repeat(inner, LevelMap.MaxHeight).ToArray();
            rows[0] = rows[LevelMap.MaxHeight - 1] = wall;
            rows[1] = "#@$." + new string(' ', LevelMap.MaxWidth - 5) + "#";

            var map = LevelParser.Parse(rows);

            Assert.That(map.Width, Is.EqualTo(LevelMap.MaxWidth));
            Assert.That(map.Height, Is.EqualTo(LevelMap.MaxHeight));
        }

        [Test]
        public void Parse_RejectsTooWide()
        {
            var wall = new string('#', LevelMap.MaxWidth + 1);
            var row = "#@$." + new string(' ', LevelMap.MaxWidth - 4) + "#";
            Assert.Throws<FormatException>(() => LevelText.Map(wall, row, wall));
        }

        [Test]
        public void Parse_RejectsTooTall()
        {
            var rows = Enumerable.Repeat("#   #", LevelMap.MaxHeight + 1).ToArray();
            rows[0] = rows[LevelMap.MaxHeight] = "#####";
            rows[1] = "#@$.#";
            Assert.Throws<FormatException>(() => LevelParser.Parse(rows));
        }

        [Test]
        public void Parse_AcceptsTheMaximumBoxCount()
        {
            var map = LevelText.Map(
                "###########",
                "#@$$$$$$$$#",
                "#         #",
                "# ........#",
                "###########");
            Assert.That(map.Initial.Boxes.Count, Is.EqualTo(LevelMap.MaxBoxes));
        }

        [Test]
        public void Parse_RejectsTooManyBoxes() =>
            Assert.Throws<FormatException>(() => LevelText.Map(
                "############",
                "#@$$$$$$$$$#",
                "#          #",
                "# .........#",
                "############"));

        [Test]
        public void Parse_RejectsNull() =>
            Assert.Throws<ArgumentNullException>(() => LevelParser.Parse(null!));

        [Test]
        public void Parse_RejectsNoRows() =>
            Assert.Throws<FormatException>(() => LevelParser.Parse(Array.Empty<string>()));

        // 行は | で区切る。
        [TestCase("", TestName = "空行だけ")]
        [TestCase("#####|#@$x#|#####", TestName = "不明な文字")]
        [TestCase("#####|# $.#|#####", TestName = "プレイヤーがいない")]
        [TestCase("######|#@@$.#|######", TestName = "プレイヤーが2人")]
        [TestCase("######|#@+$ #|######", TestName = "目標上を含めプレイヤーが2人")]
        [TestCase("#####|#@ .#|#####", TestName = "箱がない")]
        [TestCase("#####|#@$ #|#####", TestName = "目標がない")]
        [TestCase("######|#@$$.#|######", TestName = "箱が目標より多い")]
        [TestCase("######|#@$..#|######", TestName = "目標が箱より多い")]
        [TestCase("#####|#@* #|#####", TestName = "開始時点でクリア済み")]
        [TestCase("#####|#@$.|#####", TestName = "右の外周が開いている")]
        [TestCase("#####|@$. #|#####", TestName = "プレイヤーが外周上にいる")]
        [TestCase("## ##|#@$.#|#####", TestName = "上の外周が開いている")]
        [TestCase("#########|#@$ # . #|#########", TestName = "目標が壁の向こう")]
        [TestCase("#########|#@ .# $ #|#########", TestName = "箱が壁の向こう")]
        public void Parse_RejectsUnplayableLevels(string rows) =>
            Assert.Throws<FormatException>(() => LevelParser.Parse(rows.Split('|')));
    }
}
