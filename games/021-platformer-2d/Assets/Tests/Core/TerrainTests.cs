using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>単位・箱・地形・衝突解決・接地検出。</summary>
    public sealed class TerrainTests
    {
        [TestCase(0, 0)]
        [TestCase(255, 0)]
        [TestCase(256, 1)]
        [TestCase(-1, -1)]
        [TestCase(-256, -1)]
        [TestCase(-257, -2)]
        public void FloorDiv_RoundsTowardNegativeInfinity(int value, int expected)
        {
            Assert.That(Units.FloorDiv(value, T), Is.EqualTo(expected));
        }

        [Test]
        public void Box_TouchingEdgesDoNotOverlap()
        {
            var a = new Box(0, 0, 10, 10);
            Assert.That(a.Overlaps(new Box(10, 0, 10, 10)), Is.False);
            Assert.That(a.Overlaps(new Box(0, 10, 10, 10)), Is.False);
            Assert.That(a.Overlaps(new Box(9, 9, 10, 10)), Is.True);
        }

        [Test]
        public void Box_RejectsEmptySize()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Box(0, 0, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Box(0, 0, 1, -1));
        }

        [Test]
        public void TileMap_OutsideIsWallOnSidesAndEmptyAboveAndBelow()
        {
            var map = Flat(10).Map;
            Assert.That(map.IsSolid(-1, 3), Is.True);
            Assert.That(map.IsSolid(10, 3), Is.True);
            Assert.That(map.IsSolid(3, -1), Is.False);
            Assert.That(map.IsSolid(3, 99), Is.False);
            Assert.That(map.IsSolid(3, 0), Is.True);
        }

        [Test]
        public void Parse_ReadsRowsBottomUp()
        {
            var level = Map(
                "G...",
                ".S..",
                "####");
            Assert.That(level.Map.Height, Is.EqualTo(3));
            Assert.That(level.Map[0, 2], Is.EqualTo(TileKind.Goal));
            Assert.That(level.Map[0, 0], Is.EqualTo(TileKind.Solid));
            Assert.That((level.StartColumn, level.StartRow), Is.EqualTo((1, 1)));
            Assert.That(level.Map[1, 1], Is.EqualTo(TileKind.Empty), "開始位置のマスは空");
        }

        [Test]
        public void Parse_ReadsEnemies()
        {
            var level = Map(
                "G.....",
                "Swht..",
                "######");
            Assert.That(level.Enemies, Has.Count.EqualTo(3));
            Assert.That(level.Enemies[0].Kind, Is.EqualTo(EnemyKind.Walker));
            Assert.That(level.Enemies[1].Kind, Is.EqualTo(EnemyKind.Hopper));
            Assert.That(level.Enemies[2].Kind, Is.EqualTo(EnemyKind.Thorn));
            Assert.That(level.Enemies[2].Column, Is.EqualTo(3));
            Assert.That(level.Map[3, 1], Is.EqualTo(TileKind.Empty));
        }

        private static readonly object[] _invalidMaps =
        {
            new TestCaseData((object)new[] { "G..", "S.", "###" }).SetName("Parse_RejectsRaggedRows"),
            new TestCaseData((object)new[] { "G..", "...", "###" }).SetName("Parse_RejectsMissingStart"),
            new TestCaseData((object)new[] { "G..", "SS.", "###" }).SetName("Parse_RejectsTwoStarts"),
            new TestCaseData((object)new[] { "...", "S..", "###" }).SetName("Parse_RejectsMissingGoal"),
            new TestCaseData((object)new[] { "G..", "S.x", "###" }).SetName("Parse_RejectsUnknownCharacter"),
            new TestCaseData((object)new[] { "G.w", "S..", "###" }).SetName("Parse_RejectsFloatingEnemy"),
            new TestCaseData((object)new[] { "" }).SetName("Parse_RejectsEmptyRow"),
        };

        [TestCaseSource(nameof(_invalidMaps))]
        public void Parse_RejectsInvalidMaps(string[] rows)
        {
            Assert.Throws<ArgumentException>(() => Level.Parse("bad", rows));
        }

        [Test]
        public void Parse_RejectsNoRows()
        {
            Assert.Throws<ArgumentException>(() => Level.Parse("bad", Array.Empty<string>()));
        }

        [Test]
        public void MoveX_StopsAtWallFace()
        {
            var map = Map(
                "G....",
                "S..#.",
                "#####").Map;
            var box = new Box(0, T, 100, 100);
            Assert.That(BodyMotion.MoveX(map, ref box, 1000), Is.True);
            Assert.That(box.Right, Is.EqualTo(3 * T));

            box = new Box(4 * T, T, 100, 100);
            Assert.That(BodyMotion.MoveX(map, ref box, -1000), Is.True);
            Assert.That(box.Left, Is.EqualTo(4 * T));
        }

        [Test]
        public void MoveX_PassesUnderOverhangWhenItFits()
        {
            // 列 3 の固体は 2 段目の上。高さ 1 タイルちょうどの体は 1 段目を抜けられる。
            var map = Map(
                "G....",
                "...#.",
                "S....",
                "#####").Map;
            var box = new Box(0, T, 100, T);
            Assert.That(BodyMotion.MoveX(map, ref box, 5 * T), Is.True, "右端の壁で止まる");
            Assert.That(box.Right, Is.EqualTo(5 * T));

            var tall = new Box(0, T, 100, T + 1);
            Assert.That(BodyMotion.MoveX(map, ref tall, 4 * T), Is.True);
            Assert.That(tall.Right, Is.EqualTo(3 * T), "1 単位でも高ければ引っかかる");
        }

        [Test]
        public void MoveY_LandsOnFloorAndHitsCeiling()
        {
            var map = Map(
                "G#...",
                ".....",
                ".....",
                "S....",
                "#####").Map;
            var box = new Box(T, 3 * T, 100, 100);
            Assert.That(BodyMotion.MoveY(map, ref box, -5000), Is.True);
            Assert.That(box.Bottom, Is.EqualTo(T));

            Assert.That(BodyMotion.MoveY(map, ref box, 5000), Is.True);
            Assert.That(box.Top, Is.EqualTo(4 * T));
        }

        [Test]
        public void MoveY_FallsThroughPitBelowMap()
        {
            var map = Map(
                "G....",
                "S....",
                "##.##").Map;
            var box = new Box((2 * T) + 10, T, 100, 100);
            Assert.That(BodyMotion.MoveY(map, ref box, -3 * T), Is.False);
            Assert.That(box.Top, Is.LessThan(0));
        }

        /// <summary>どんな移動量でも、どこから動かしても、固体に重なった状態で止まらない（すり抜けない）。</summary>
        [Test]
        public void Motion_NeverEndsInsideSolid_RandomMoves()
        {
            var map = Map(
                "G.........",
                "..#....#..",
                ".....#....",
                "...#....#.",
                "S.........",
                "##########").Map;
            var random = new Lcg(12345);
            for (var i = 0; i < 20000; i++)
            {
                var w = random.Next(1, T + 1);
                var h = random.Next(1, T + 1);
                var box = new Box(random.Next(0, (10 * T) - w), random.Next(T, 5 * T), w, h);
                if (OverlapsSolid(map, box)) { continue; }
                BodyMotion.MoveX(map, ref box, random.Next(-1000, 1001));
                Assert.That(OverlapsSolid(map, box), Is.False, $"横 {i}: {box}");
                BodyMotion.MoveY(map, ref box, random.Next(-1000, 1001));
                Assert.That(OverlapsSolid(map, box), Is.False, $"縦 {i}: {box}");
            }
        }

        [Test]
        public void GroundProbe_RequiresExactContact()
        {
            var map = Flat(10).Map;
            Assert.That(GroundProbe.IsGrounded(map, new Box(T, T, 100, 100)), Is.True);
            Assert.That(GroundProbe.IsGrounded(map, new Box(T, T + 1, 100, 100)), Is.False, "1 単位でも浮いていれば空中");
        }

        [Test]
        public void GroundProbe_OneUnitOnLedgeCounts()
        {
            var map = Map(
                "G....",
                "S....",
                "##...").Map;
            Assert.That(GroundProbe.IsGrounded(map, new Box((2 * T) - 1, T, 100, 100)), Is.True, "足場に 1 単位乗っている");
            Assert.That(GroundProbe.IsGrounded(map, new Box(2 * T, T, 100, 100)), Is.False, "辺が接するだけ");
        }

        [Test]
        public void HasGroundAhead_LooksAtNextStep()
        {
            var map = Map(
                "G....",
                "S....",
                "##...").Map;
            var box = new Box(2 * T - 100, T, 100, 100);
            Assert.That(GroundProbe.HasGroundAhead(map, box, 1, 1), Is.False);
            Assert.That(GroundProbe.HasGroundAhead(map, box, -1, 1), Is.True);
            Assert.That(GroundProbe.HasGroundAhead(map, box.Offset(-1, 0), 1, 1), Is.True);
        }

        private static bool OverlapsSolid(TileMap map, Box box)
        {
            for (var row = Units.FloorDiv(box.Bottom, T); row <= Units.FloorDiv(box.Top - 1, T); row++)
            {
                if (map.AnySolidInRow(row, box)) { return true; }
            }

            return false;
        }
    }

    /// <summary>テスト用の決まった系列の乱数（Mono と .NET で同じ）。</summary>
    internal sealed class Lcg
    {
        private uint _state;

        public Lcg(uint seed)
        {
            _state = seed;
        }

        /// <summary>[min, max) の整数。</summary>
        public int Next(int min, int max)
        {
            unchecked
            {
                _state = (_state * 1664525u) + 1013904223u;
            }

            return min + (int)((_state >> 8) % (uint)(max - min));
        }
    }
}
