using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>部屋の解析・10 部屋のデータ・部屋を順に抜ける進行。</summary>
    public sealed class ClimbAndRoomsTests
    {
        private static Room Corridor() => Map(
            "......",
            "S...E.",
            "######");

        private static Room Pit() => Map(
            "E.....",
            "S.....",
            "#.....");

        private static string[] Lines(params string[] rows) => rows;

        private static Climb ClimbOf(params Room[] rooms) => new(new PrecisionConfig(), rooms);

        [Test]
        public void Climb_ProceedsThroughRoomsAndWinsOnce()
        {
            var climb = ClimbOf(Corridor(), Corridor(), Corridor());
            var cleared = 0;
            var ticks = 0L;
            for (var i = 0; i < 6000 && !climb.IsWon; i++)
            {
                climb.Tick(Right);
                ticks++;
                if ((climb.TakeEvents() & RunEventKind.Cleared) != 0) { cleared++; }
            }

            Assert.That(climb.IsWon, Is.True);
            Assert.That(cleared, Is.EqualTo(3));
            Assert.That(climb.RoomIndex, Is.EqualTo(2));
            Assert.That(climb.ElapsedTicks, Is.EqualTo(ticks));
            var hash = climb.Hash();
            Assert.That(climb.Tick(Right), Is.False, "勝利の後の Tick は受け付けない");
            Assert.That(climb.Hash(), Is.EqualTo(hash));
            Assert.That(climb.TakeEvents(), Is.EqualTo(RunEventKind.None), "2 回目の勝利は起きない");
        }

        [Test]
        public void Climb_NextRoomStartsAtItsStartAndHeldJumpMustBeRepressed()
        {
            var climb = ClimbOf(Corridor(), Corridor());
            for (var i = 0; i < 600 && climb.RoomIndex == 0; i++) { climb.Tick(In(1, jump: true)); }
            Assert.That(climb.RoomIndex, Is.EqualTo(1));
            Assert.That(climb.Run.Player.Body, Is.EqualTo(climb.Run.Room.StartBox));
            climb.TakeEvents();
            climb.Tick(In(0, jump: true));
            Assert.That(climb.TakeEvents() & RunEventKind.Jumped, Is.EqualTo(RunEventKind.None), "前の部屋から押したままのジャンプでは跳ばない");
        }

        [Test]
        public void Climb_CountsDeathsAcrossRooms()
        {
            var climb = new Climb(new PrecisionConfig { DeathTicks = 1 }, new[] { Pit() });
            for (var i = 0; i < 3000 && climb.Deaths < 7; i++) { climb.Tick(Right); }
            Assert.That(climb.Deaths, Is.EqualTo(7));
            Assert.That(climb.Run.Deaths, Is.EqualTo(7));
            Assert.That(climb.IsWon, Is.False);
        }

        [Test]
        public void Climb_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentNullException>(() => _ = new Climb(null!, new[] { Corridor() }));
            Assert.Throws<ArgumentNullException>(() => _ = new Climb(new PrecisionConfig(), null!));
            Assert.Throws<ArgumentException>(() => _ = new Climb(new PrecisionConfig(), Array.Empty<Room>()));
            Assert.Throws<ArgumentException>(() => _ = new Climb(new PrecisionConfig(), new Room[] { null! }));
        }

        [Test]
        public void Rooms_AreTenFullScreenRoomsWithUniqueNames()
        {
            var rooms = Rooms.All();
            Assert.That(rooms, Has.Count.EqualTo(10));
            foreach (var room in rooms)
            {
                Assert.That(room.Map.Width, Is.EqualTo(Room.Columns), room.Name);
                Assert.That(room.Map.Height, Is.EqualTo(Room.Rows), room.Name);
            }

            Assert.That(rooms.Select(r => r.Name).Distinct().Count(), Is.EqualTo(10));
        }

        [Test]
        public void Rooms_ExitIsOnTheRightEdge()
        {
            foreach (var room in Rooms.All())
            {
                var exits = new List<(int, int)>();
                for (var column = 0; column < room.Map.Width; column++)
                {
                    for (var row = 0; row < room.Map.Height; row++)
                    {
                        if (room.Map[column, row] == TileKind.Exit) { exits.Add((column, row)); }
                    }
                }

                Assert.That(exits.All(e => e.Item1 == Room.Columns - 1), Is.True, room.Name);
            }
        }

        [Test]
        public void Room_ParsesTilesBottomUp()
        {
            var room = Map(
                "E^",
                "S.",
                "##");
            Assert.That(room.StartColumn, Is.Zero);
            Assert.That(room.StartRow, Is.EqualTo(1));
            Assert.That(room.Map[0, 2], Is.EqualTo(TileKind.Exit));
            Assert.That(room.Map[1, 2], Is.EqualTo(TileKind.Spike));
            Assert.That(room.Map[0, 0], Is.EqualTo(TileKind.Solid));
            Assert.That(room.Map[0, 1], Is.EqualTo(TileKind.Empty), "開始位置のマスは空");
            Assert.That(room.StartBox, Is.EqualTo(Room.PlaceOnTile(0, 1, Player.Width, Player.Height)));
        }

        private static IEnumerable<TestCaseData> InvalidMaps()
        {
            yield return new TestCaseData("E.", "..", "##").SetName("Room_RejectsMissingStart");
            yield return new TestCaseData("..", "S.", "##").SetName("Room_RejectsMissingExit");
            yield return new TestCaseData("ES", "S.", "##").SetName("Room_RejectsTwoStarts");
            yield return new TestCaseData("E.", ".S", "#.").SetName("Room_RejectsStartWithoutFloor");
            yield return new TestCaseData("E.", "S?", "##").SetName("Room_RejectsUnknownCharacter");
        }

        [TestCaseSource(nameof(InvalidMaps))]
        public void Room_RejectsInvalidMaps(string top, string middle, string bottom)
        {
            Assert.Throws<ArgumentException>(() => Map(top, middle, bottom));
        }

        [Test]
        public void Room_RejectsWrongSize()
        {
            Assert.Throws<ArgumentException>(() => Room.Parse("x", Lines("E.", "S", "##"), 2, 3), "幅が揃っていない");
            Assert.Throws<ArgumentException>(() => Room.Parse("x", Lines("E.", "S."), 2, 3), "行数が違う");
            Assert.Throws<ArgumentOutOfRangeException>(() => Room.Parse("x", Lines("E", "S", "#"), Room.Columns + 1, 3));
            Assert.Throws<ArgumentException>(() => Room.Parse("x", Lines("E.", "S.", "##")), "既定の大きさは 40×22");
            Assert.Throws<ArgumentNullException>(() => Room.Parse("x", null!));
        }
    }
}
