using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>地形との衝突解決・接地・壁・トゲの当たり。</summary>
    public sealed class TerrainTests
    {
        private static readonly TileMap _wall = Map(
            "......E",
            ".......",
            "...#...",
            "S......",
            "#######").Map;

        [Test]
        public void MoveX_StopsAtWallFace_WithoutSkippingColumns()
        {
            var box = new Box(0, T, Player.Width, Player.Height);
            Assert.That(BodyMotion.MoveX(_wall, ref box, 3 * T), Is.False, "行 1 には壁がない");
            box = new Box(0, 2 * T, Player.Width, Player.Height);
            Assert.That(BodyMotion.MoveX(_wall, ref box, T - 1), Is.False);
            Assert.That(BodyMotion.MoveX(_wall, ref box, T - 1), Is.False);
            Assert.That(BodyMotion.MoveX(_wall, ref box, T - 1), Is.True);
            Assert.That(box.Right, Is.EqualTo(3 * T));
        }

        [Test]
        public void MoveY_LandsOnFloorAndHitsCeiling()
        {
            var box = new Box(0, T + 100, Player.Width, Player.Height);
            Assert.That(BodyMotion.MoveY(_wall, ref box, -200), Is.True);
            Assert.That(box.Bottom, Is.EqualTo(T));
            box = new Box(3 * T, T, Player.Width, Player.Height);
            Assert.That(BodyMotion.MoveY(_wall, ref box, 200), Is.True);
            Assert.That(box.Top, Is.EqualTo(2 * T));
        }

        [Test]
        public void GroundProbe_OnlyWhenExactlyOnFloor()
        {
            Assert.That(GroundProbe.IsGrounded(_wall, new Box(0, T, Player.Width, Player.Height)), Is.True);
            Assert.That(GroundProbe.IsGrounded(_wall, new Box(0, T + 1, Player.Width, Player.Height)), Is.False);
        }

        [Test]
        public void MapEdges_SidesAreWallsAndBelowIsEmpty()
        {
            Assert.That(_wall.IsSolid(-1, 2), Is.True);
            Assert.That(_wall.IsSolid(7, 2), Is.True);
            Assert.That(_wall.IsSolid(3, -1), Is.False);
            Assert.That(_wall.IsSolid(3, 99), Is.False);
        }

        [Test]
        public void WallProbe_DetectsWallsWithinReach()
        {
            var nextToBlock = new Box((3 * T) - Player.Width - 10, 2 * T, Player.Width, Player.Height);
            Assert.That(WallProbe.Near(_wall, nextToBlock, 1, 11), Is.True);
            Assert.That(WallProbe.Near(_wall, nextToBlock, 1, 10), Is.False);
            Assert.That(WallProbe.Near(_wall, nextToBlock, -1, 255), Is.False);
            var flushLeft = new Box(0, 2 * T, Player.Width, Player.Height);
            Assert.That(WallProbe.Near(_wall, flushLeft, -1, 1), Is.True, "左の外は壁");
            Assert.That(WallProbe.KickSide(_wall, flushLeft, 1, 1), Is.EqualTo(-1));
            Assert.That(WallProbe.KickSide(_wall, new Box(T, 3 * T, Player.Width, Player.Height), 1, 1), Is.Zero);
        }

        [Test]
        public void WallProbe_BothSidesPrefersFacing()
        {
            var narrow = Map("#S#E", "####").Map;
            var box = new Box(T + ((T - Player.Width) / 2), T, Player.Width, Player.Height);
            Assert.That(WallProbe.KickSide(narrow, box, 1, 100), Is.EqualTo(1));
            Assert.That(WallProbe.KickSide(narrow, box, -1, 100), Is.EqualTo(-1));
        }

        [Test]
        public void WallProbe_RejectsInvalidArguments()
        {
            var box = new Box(0, T, Player.Width, Player.Height);
            Assert.Throws<ArgumentOutOfRangeException>(() => WallProbe.Near(_wall, box, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => WallProbe.Near(_wall, box, 1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => WallProbe.Near(_wall, box, 1, T));
        }

        [Test]
        public void Spike_OnlyLowerHalfHurts()
        {
            var map = Map("E...", "S^..", "####").Map;
            var x = T + ((T - Player.Width) / 2);
            Assert.That(map.TouchesSpike(new Box(x, T + TileMap.SpikeHeight, Player.Width, Player.Height)), Is.False, "上半分は安全（辺が接するだけ）");
            Assert.That(map.TouchesSpike(new Box(x, T + TileMap.SpikeHeight - 1, Player.Width, Player.Height)), Is.True);
            Assert.That(map.TouchesSpike(new Box(2 * T, T, Player.Width, Player.Height)), Is.False, "横に接するだけなら触れていない");
        }

        [Test]
        public void TileMap_RejectsInvalidData()
        {
            Assert.Throws<ArgumentException>(() => _ = new TileMap(2, 2, new TileKind[3]));
            Assert.Throws<ArgumentException>(() => _ = new TileMap(1, 1, new[] { (TileKind)99 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TileMap(0, 1, Array.Empty<TileKind>()));
            Assert.Throws<ArgumentNullException>(() => _ = new TileMap(1, 1, null!));
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
    }
}
