using System;
using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>文字の地図の解析と、本番の 6 部屋のつながり。</summary>
    public sealed class MapTests
    {
        [Test]
        public void RealDungeon_HasSixRooms_AndTheDesignedDoors()
        {
            var world = Real();
            Assert.That(world.Rooms.Count, Is.EqualTo(6));
            Assert.That(world.StartRoom, Is.EqualTo(Rooms.Entrance));
            Assert.That(world.BossRoom, Is.EqualTo(Rooms.Lair));
            Assert.That(world.ChestCount, Is.EqualTo(2));
            Assert.That(world.Rooms[Rooms.West].Reward.HasValue, Is.True);

            Assert.That(world.Neighbor(Rooms.Entrance, Side.Left), Is.EqualTo(Rooms.West));
            Assert.That(world.Neighbor(Rooms.Entrance, Side.Right), Is.EqualTo(Rooms.East));
            Assert.That(world.Neighbor(Rooms.Entrance, Side.Top), Is.EqualTo(Rooms.Antechamber));
            Assert.That(world.Neighbor(Rooms.Antechamber, Side.Left), Is.EqualTo(Rooms.Vault));
            Assert.That(world.Neighbor(Rooms.Antechamber, Side.Right), Is.EqualTo(Rooms.Lair));

            Assert.That(world.Rooms[Rooms.Entrance].Door(Side.Top), Is.EqualTo(DoorKind.Small));
            Assert.That(world.Rooms[Rooms.Antechamber].Door(Side.Left), Is.EqualTo(DoorKind.Small));
            Assert.That(world.Rooms[Rooms.Antechamber].Door(Side.Right), Is.EqualTo(DoorKind.Boss));
            Assert.That(world.PassageId(Rooms.Entrance, Side.Top), Is.EqualTo(world.PassageId(Rooms.Antechamber, Side.Bottom)), "扉は両側で同じ 1 枚");

            // 3 種の敵とボスがいる。
            var kinds = new bool[4];
            foreach (var room in world.Rooms)
            {
                foreach (var spawn in room.Spawns) { kinds[(int)spawn.Kind] = true; }
            }

            Assert.That(kinds, Is.All.True);
        }

        [Test]
        public void RealDungeon_StartsWithOnlyTheOpenDoorwaysOpen()
        {
            var world = Real();
            var state = new DungeonState(world);
            Assert.That(state.IsOpen(Rooms.Entrance, Side.Left), Is.True);
            Assert.That(state.IsOpen(Rooms.Entrance, Side.Right), Is.True);
            Assert.That(state.IsOpen(Rooms.Entrance, Side.Top), Is.False);
            Assert.That(state.IsOpen(Rooms.Antechamber, Side.Left), Is.False);
            Assert.That(state.IsOpen(Rooms.Antechamber, Side.Right), Is.False);
            Assert.That(state.SmallKeys, Is.EqualTo(0));
            Assert.That(state.HasBossKey, Is.False);
        }

        [Test]
        public void Arrival_IsOneTileInside_TheMiddleOfTheDoorway()
        {
            var room = Real().Rooms[Rooms.Antechamber];
            var bottom = room.Arrival(Side.Bottom);
            Assert.That(bottom.CenterX, Is.EqualTo(((9 + 12) * T) / 2), "列 9〜11 の中央");
            Assert.That(Boxes.CellOf(bottom), Is.EqualTo(new Cell(10, 1)));
            Assert.That(Boxes.CellOf(room.Arrival(Side.Left)), Is.EqualTo(new Cell(1, 5)));
            Assert.That(() => Real().Rooms[Rooms.West].Arrival(Side.Left), Throws.InvalidOperationException);
        }

        [TestCase("#?#", "Unknown")]
        [TestCase("#D#", "MiddleDoor")]
        public void Parse_RejectsBadRows(string middle, string label)
        {
            Assert.That(() => Map(0, 0, "###", middle, "#S#", "###"), Throws.ArgumentException, label);
        }

        [Test]
        public void Parse_RejectsCornerDoors_MixedSides_AndTwoStarts()
        {
            Assert.That(() => Map(0, 0, "D####", "#...#", "#.S.#", "#####"), Throws.ArgumentException, "角の出入口");
            Assert.That(() => Map(0, 0, "#####", "D...#", "L.S.#", "#####"), Throws.ArgumentException, "1 つの辺に種類が混ざる");
            Assert.That(() => Worlds(Map(0, 0, "#####", "#S..#", "#..S#", "#####")), Throws.ArgumentException, "開始位置が 2 つ");
            Assert.That(() => Map(0, 0, "#####", "#..#", "#.S.#", "#####"), Throws.ArgumentException, "行の幅がそろわない");
        }

        [Test]
        public void World_RejectsDoorsThatDoNotMatchTheNeighbor()
        {
            var a = Map(0, 0, "####", "#..D", "#S.D", "#..D", "####");
            Assert.That(a.Door(Side.Right), Is.EqualTo(DoorKind.Open));
            Assert.That(() => Worlds(a), Throws.ArgumentException, "隣の部屋がない");
            Assert.That(() => Worlds(a, Map(1, 0, "####", "L..#", "L..#", "L..#", "####")), Throws.ArgumentException, "隣の扉の種類が違う");
            Assert.That(() => Worlds(a, Map(1, 0, "####", "D..#", "D..#", "D..#", "####")), Throws.Nothing);
        }

        [Test]
        public void Parse_RejectsAnEnemyInsideAWall()
        {
            // ボス（28 px）は 1 マスより大きく、隣のマスにはみ出す。
            Assert.That(() => Map(0, 0, "#####", "#S..#", "#.M.#", "#...#", "#####"), Throws.Nothing);
            Assert.That(() => Map(0, 0, "#####", "#S..#", "#M..#", "#...#", "#####"), Throws.ArgumentException);
        }

        [Test]
        public void StateTracksVisitedRooms()
        {
            var dungeon = new Dungeon(Config(), Real(), 7);
            Assert.That(dungeon.State.IsVisited(Rooms.Entrance), Is.True);
            Assert.That(dungeon.State.IsVisited(Rooms.West), Is.False);
            Assert.That(() => dungeon.State.TryOpen(Rooms.West, Side.Top), Throws.ArgumentException, "出入口のない辺");
            Assert.That(() => dungeon.State.TryOpen(Rooms.Entrance, Side.Left), Throws.InvalidOperationException, "鍵の扉でない出入口");
        }

        [Test]
        public void StartAt_RejectsARoomOutsideTheDungeon()
        {
            var state = new DungeonState(Real());
            Assert.That(() => new Dungeon(Config(), Real(), 1), Throws.Nothing);
            Assert.That(() => Dungeon.StartAt(Config(), state, 9, new Cell(2, 2), 1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
