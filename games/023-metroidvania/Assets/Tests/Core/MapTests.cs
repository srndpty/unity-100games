using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>文字の地図の解析と、部屋の組（扉のつながり・アイテムの通し番号）。</summary>
    public sealed class MapTests
    {
        private static readonly string[] _tiny = { "##", "##" };

        [Test]
        public void RealWorld_HasFiveConnectedRooms()
        {
            var world = Real();
            Assert.That(world.Rooms, Has.Count.EqualTo(5));
            Assert.That(world.StartRoom, Is.EqualTo(Rooms.Hub));
            Assert.That(world.Neighbor(Rooms.Hub, Side.Right), Is.EqualTo(Rooms.Corridor));
            Assert.That(world.Neighbor(Rooms.Corridor, Side.Right), Is.EqualTo(Rooms.Cave));
            Assert.That(world.Neighbor(Rooms.Hub, Side.Top), Is.EqualTo(Rooms.Tower));
            Assert.That(world.Neighbor(Rooms.Tower, Side.Right), Is.EqualTo(Rooms.Lair));
            Assert.That(world.Neighbor(Rooms.Tower, Side.Bottom), Is.EqualTo(Rooms.Hub));
            Assert.That(world.Neighbor(Rooms.Lair, Side.Right), Is.EqualTo(-1));
            Assert.That(world.Rooms[Rooms.Lair].Boss.HasValue, Is.True);
            Assert.That(world.PickupCount, Is.EqualTo(4), "回復の実 2・能力 2");
        }

        [Test]
        public void RealWorld_EachAbilityAppearsOnce()
        {
            var world = Real();
            Assert.That(FindPickup(world, PickupKind.DoubleJump).Room, Is.EqualTo(Rooms.Cave));
            Assert.That(FindPickup(world, PickupKind.BreakShot).Room, Is.EqualTo(Rooms.Tower));
        }

        [Test]
        public void Parse_ReadsDoorsArrivalsAndObjects()
        {
            var room = Map(0, 0,
                "#####",
                "D.$.D",
                "Dl+rD",
                "#####");
            Assert.That(room.HasDoor(Side.Left), Is.True);
            Assert.That(room.HasDoor(Side.Right), Is.True);
            Assert.That(room.HasDoor(Side.Top), Is.False);
            Assert.That(room.Arrival(Side.Left), Is.EqualTo(new Cell(1, 1)));
            Assert.That(room.Arrival(Side.Right), Is.EqualTo(new Cell(3, 1)));
            Assert.That(room.Statues, Is.EqualTo(new[] { new Cell(2, 2) }));
            Assert.That(room.Pickups[0].Kind, Is.EqualTo(PickupKind.Fruit));
            Assert.That(room.Pickups[0].Cell, Is.EqualTo(new Cell(2, 1)));
            Assert.That(room.Map[0, 2], Is.EqualTo(TileKind.Door));
            Assert.Throws<InvalidOperationException>(() => room.Arrival(Side.Top));
        }

        [Test]
        public void Parse_RejectsBadMaps()
        {
            Assert.Throws<ArgumentException>(() => Map(0, 0, "D...", "#l.#", "####"), "扉は角に置けない");
            Assert.Throws<ArgumentException>(() => Map(0, 0, "####", "#D.#", "#l.#", "####"), "扉は端だけ");
            Assert.Throws<ArgumentException>(() => Map(0, 0, "####", "D..#", "D..#", "####"), "扉には到着位置が要る");
            Assert.Throws<ArgumentException>(() => Map(0, 0, "####", "#l.#", "####"), "到着位置だけで扉がない");
            Assert.Throws<ArgumentException>(() => Map(0, 0, "####", "D..#", "Dl.#", "#.##"), "到着位置の真下が固体でない");
            Assert.Throws<ArgumentException>(() => Map(0, 0, "####", "#SS#", "####"), "開始位置が 2 つ");
            Assert.Throws<ArgumentException>(() => Map(0, 0, "####", "#?.#", "####"), "使えない文字");
            Assert.Throws<ArgumentException>(() => Map(0, 0, "####", "#..", "####"), "幅が揃わない");
            Assert.Throws<ArgumentException>(() => Room.Parse("x", 0, 0, _tiny), "本番の大きさでない");
        }

        [Test]
        public void World_RejectsBrokenConnections()
        {
            var left = Map(0, 0, "####", "#..D", "#SrD", "####");
            var right = Map(1, 0, "####", "D..#", "Dl.#", "####");
            Assert.DoesNotThrow(() => Worlds(left, right));
            Assert.Throws<ArgumentException>(() => Worlds(left), "扉の先に部屋がない");
            var noDoor = Map(1, 0, "####", "#..#", "#..#", "####");
            Assert.Throws<ArgumentException>(() => Worlds(left, noDoor), "隣に反対側の扉がない");
            var sameCell = Map(0, 0, "####", "#..#", "#..#", "####");
            Assert.Throws<ArgumentException>(() => Worlds(left, sameCell), "格子の同じ位置");
            var noStart = Map(0, 0, "####", "#..#", "#..#", "####");
            Assert.Throws<ArgumentException>(() => Worlds(noStart), "開始位置がない");
            Assert.Throws<ArgumentException>(() => Worlds(), "部屋がない");
        }

        [Test]
        public void World_NumbersPickupsAcrossRooms()
        {
            var a = Map(0, 0, "#####", "#++.D", "#S.rD", "#####");
            var b = Map(1, 0, "####", "D+.#", "Dl.#", "####");
            var world = Worlds(a, b);
            Assert.That(world.PickupCount, Is.EqualTo(3));
            Assert.That(world.PickupId(1, 0), Is.EqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.PickupId(1, 1));
        }

        [Test]
        public void TileMap_EdgesAndValidation()
        {
            var map = Real().Rooms[Rooms.Hub].Map;
            Assert.That(map[-1, 5], Is.EqualTo(TileKind.Solid), "左右の外は壁");
            Assert.That(map[40, 5], Is.EqualTo(TileKind.Solid));
            Assert.That(map[5, -1], Is.EqualTo(TileKind.Empty), "上下の外は空");
            Assert.That(map[5, 22], Is.EqualTo(TileKind.Empty));
            Assert.Throws<ArgumentException>(() => _ = new TileMap(2, 2, new TileKind[3]));
            Assert.Throws<ArgumentException>(() => _ = new TileMap(1, 1, new[] { (TileKind)99 }));
            Assert.Throws<ArgumentNullException>(() => _ = new TileMap(1, 1, null!));
        }
    }
}
