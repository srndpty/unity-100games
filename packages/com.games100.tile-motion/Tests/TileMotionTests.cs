using System;
using NUnit.Framework;

namespace TileMotion.Tests
{
    /// <summary>文字の地図（'#' が固体、1 行目が最上段）。左右の外は壁、上下の外は空。</summary>
    internal sealed class TextGrid : ISolidGrid
    {
        private readonly string[] _rows;

        public TextGrid(params string[] rows) => _rows = rows;

        public int Width => _rows[0].Length;

        public bool IsSolid(int column, int row)
        {
            if (column < 0 || column >= Width) { return true; }
            if (row < 0 || row >= _rows.Length) { return false; }
            return _rows[_rows.Length - 1 - row][column] == '#';
        }
    }

    public sealed class TileMotionTests
    {
        private const int T = Units.Tile;
        private const int W = 10 * Units.Pixel;
        private const int H = 14 * Units.Pixel;

        private static readonly TextGrid _wall = new(
            ".......",
            ".......",
            "...#...",
            ".......",
            "#######");

        [Test]
        public void FloorDiv_RoundsTowardNegativeInfinity()
        {
            Assert.That(Units.FloorDiv(7, 2), Is.EqualTo(3));
            Assert.That(Units.FloorDiv(-7, 2), Is.EqualTo(-4));
            Assert.That(Units.FloorDiv(-8, 2), Is.EqualTo(-4));
            Assert.That(Units.FloorDiv(-1, T), Is.EqualTo(-1));
            Assert.That(Units.FloorDiv(0, T), Is.Zero);
        }

        [Test]
        public void Box_OverlapsAsOpenIntervals()
        {
            var box = new Box(0, 0, 10, 10);
            Assert.That(box.Overlaps(new Box(9, 9, 5, 5)), Is.True);
            Assert.That(box.Overlaps(new Box(10, 0, 5, 5)), Is.False, "辺が接するだけなら重ならない");
            Assert.That(box.Overlaps(new Box(0, 10, 5, 5)), Is.False);
            Assert.That(Box.Span(box, box.Offset(30, 0)), Is.EqualTo(new Box(0, 0, 40, 10)));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Box(0, 0, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Box(0, 0, 1, -1));
        }

        [Test]
        public void MoveX_StopsAtWallFace_WithoutSkippingColumns()
        {
            var box = new Box(0, T, W, H);
            Assert.That(BodyMotion.MoveX(_wall, ref box, 3 * T), Is.False, "行 1 には壁がない");
            box = new Box(0, 2 * T, W, H);
            Assert.That(BodyMotion.MoveX(_wall, ref box, T - 1), Is.False);
            Assert.That(BodyMotion.MoveX(_wall, ref box, T - 1), Is.False);
            Assert.That(BodyMotion.MoveX(_wall, ref box, T - 1), Is.True);
            Assert.That(box.Right, Is.EqualTo(3 * T));
        }

        [Test]
        public void MoveX_LongStepDoesNotTunnel()
        {
            var box = new Box(0, 2 * T, W, H);
            Assert.That(BodyMotion.MoveX(_wall, ref box, 6 * T), Is.True, "1 回で何タイル動いても壁の手前で止まる");
            Assert.That(box.Right, Is.EqualTo(3 * T));
            box = new Box(6 * T, 2 * T, W, H);
            Assert.That(BodyMotion.MoveX(_wall, ref box, -6 * T), Is.True);
            Assert.That(box.Left, Is.EqualTo(4 * T));
        }

        [Test]
        public void MoveX_OutsideColumnsAreWalls()
        {
            var box = new Box(10, T, W, H);
            Assert.That(BodyMotion.MoveX(_wall, ref box, -100), Is.True);
            Assert.That(box.Left, Is.Zero);
        }

        [Test]
        public void MoveY_LandsOnFloorAndHitsCeiling()
        {
            var box = new Box(0, T + 100, W, H);
            Assert.That(BodyMotion.MoveY(_wall, ref box, -200), Is.True);
            Assert.That(box.Bottom, Is.EqualTo(T));
            box = new Box(3 * T, T, W, H);
            Assert.That(BodyMotion.MoveY(_wall, ref box, 200), Is.True);
            Assert.That(box.Top, Is.EqualTo(2 * T));
            box = new Box(0, 3 * T, W, H);
            Assert.That(BodyMotion.MoveY(_wall, ref box, 5 * T), Is.False, "上の外は空");
        }

        [Test]
        public void GroundProbe_OnlyWhenExactlyOnFloor()
        {
            Assert.That(GroundProbe.IsGrounded(_wall, new Box(0, T, W, H)), Is.True);
            Assert.That(GroundProbe.IsGrounded(_wall, new Box(0, T + 1, W, H)), Is.False);
            Assert.That(GroundProbe.IsGrounded(_wall, new Box(3 * T, 3 * T, W, H)), Is.True, "宙に浮いたブロックの上");
        }

        [Test]
        public void SolidQuery_OverlapsChecksEveryCoveredTile()
        {
            Assert.That(SolidQuery.Overlaps(_wall, new Box(3 * T, 2 * T, W, H)), Is.True);
            Assert.That(SolidQuery.Overlaps(_wall, new Box((3 * T) - W, 2 * T, W, H)), Is.False, "左に接するだけ");
            Assert.That(SolidQuery.Overlaps(_wall, new Box((3 * T) - W + 1, 2 * T, W, H)), Is.True);
        }

        [Test]
        public void Countdown_ArmsTicksAndClears()
        {
            var countdown = new Countdown(2);
            Assert.That(countdown.IsActive, Is.True);
            countdown.Tick();
            countdown.Tick();
            Assert.That(countdown.IsActive, Is.False);
            countdown.Tick();
            Assert.That(countdown.Remaining, Is.Zero, "0 より下へは減らない");
            countdown.Arm(3);
            countdown.Clear();
            Assert.That(countdown, Is.EqualTo(default(Countdown)));
            Assert.Throws<ArgumentOutOfRangeException>(() => countdown.Arm(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new Countdown(-1));
        }

        [Test]
        public void StateHash_IsFixedForTheSameSequence()
        {
            var a = StateHash.Start();
            a.Mix(1);
            a.Mix(-1L);
            a.Mix(true);
            var b = StateHash.Start();
            b.Mix(1);
            b.Mix(-1L);
            b.Mix(true);
            Assert.That(a.Value, Is.EqualTo(b.Value));
            Assert.That(a.Value, Is.EqualTo(0xBA6995D3u), "Mono と .NET で同じ値");
            var c = StateHash.Start();
            c.Mix(-1L);
            c.Mix(1);
            c.Mix(true);
            Assert.That(c.Value, Is.Not.EqualTo(a.Value), "順序が違えば別の値");
        }
    }
}
