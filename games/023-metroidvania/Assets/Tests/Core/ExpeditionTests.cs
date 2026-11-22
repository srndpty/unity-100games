using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>試合の開始・進行・終了と、再現性。</summary>
    public sealed class ExpeditionTests
    {
        [Test]
        public void NewGame_StartsInTheHubWithAFreshWorld()
        {
            var config = Config();
            var e = new Expedition(config, Real());
            Assert.That(e.Status, Is.EqualTo(ExpeditionStatus.Playing));
            Assert.That(e.RoomIndex, Is.EqualTo(Rooms.Hub));
            Assert.That(e.State.Abilities, Is.EqualTo(Ability.None));
            Assert.That(e.State.IsVisited(Rooms.Hub), Is.True);
            Assert.That(e.State.IsVisited(Rooms.Corridor), Is.False);
            Assert.That(e.Save.Room, Is.EqualTo(Rooms.Hub));
            Assert.That(e.Save.Cell, Is.EqualTo(e.World.Rooms[Rooms.Hub].Start!.Value), "開始位置が最初のセーブ");
            Assert.That(e.Player.Health, Is.EqualTo(config.MaxHealth));
            Assert.That(e.RoomEntries, Is.EqualTo(1));
            Assert.That(e.Deaths, Is.Zero);
        }

        [Test]
        public void SameInputs_GiveTheSameState()
        {
            var a = new Expedition(Config(), Real());
            var b = new Expedition(Config(), Real());
            for (var i = 0; i < 600; i++)
            {
                var input = In(i % 90 < 60 ? 1 : -1, jump: i % 40 < 12, shoot: i % 7 == 0);
                a.Tick(input);
                b.Tick(input);
            }

            Assert.That(a.Hash(), Is.EqualTo(b.Hash()));
        }

        [Test]
        public void Clone_AdvancesIndependently()
        {
            var e = new Expedition(Config(), Real());
            Hold(e, Right, 30);
            var hash = e.Hash();
            var clone = e.Clone();
            Assert.That(clone.Hash(), Is.EqualTo(hash));
            Hold(clone, In(1, jump: true, shoot: true), 60);
            Assert.That(e.Hash(), Is.EqualTo(hash), "複製を進めても元は変わらない");
            Assert.That(clone.Hash(), Is.Not.EqualTo(hash));
        }

        [Test]
        public void Hash_SeesBufferedInputs()
        {
            // 空中で押したジャンプ（二段ジャンプなし）はバッファに残るだけで、位置と速さは押さなかった場合と同じ。
            var a = new Expedition(Config(), Real());
            Hold(a, Idle, 2);
            Hold(a, Jump, 30);
            a.Tick(Idle);
            Assert.That(a.Player.Grounded, Is.False);
            Assert.That(a.Player.VelocityY, Is.LessThan(0), "頂点を過ぎて落ちている");
            var b = a.Clone();
            a.Tick(Jump);
            b.Tick(Idle);
            Assert.That(a.Player.Body, Is.EqualTo(b.Player.Body));
            Assert.That(a.Player.VelocityY, Is.EqualTo(b.Player.VelocityY));
            Assert.That(a.Hash(), Is.Not.EqualTo(b.Hash()), "以後の動きが分かれる状態は別のハッシュ");
        }

        [Test]
        public void FallingOutOfARoomWithoutAFloorDoor_IsDown()
        {
            var world = Worlds(Map(0, 0,
                "#######",
                "#.....#",
                "#S....#",
                "##..###"));
            var e = new Expedition(Config(), world);
            Assert.That(Until(e, Right, x => x.Status != ExpeditionStatus.Playing, 300), Is.True);
            Assert.That(e.Status, Is.EqualTo(ExpeditionStatus.Down));
            Assert.That(e.Deaths, Is.EqualTo(1));
        }

        /// <summary>
        /// 床に扉（列 2）と、扉でない穴（列 4）がある上の部屋と、上の扉でつながった下の部屋。
        /// </summary>
        private static World FloorDoorAndHole() => Worlds(
            Map(0, 1,
                "########",
                "#......#",
                "#S....b#",
                "##D#.###"),
            Map(0, 0,
                "##D#####",
                "#......#",
                "#.t....#",
                "########"));

        [Test]
        public void FallingThroughTheFloorDoor_MovesToTheRoomBelow()
        {
            var world = FloorDoorAndHole();
            var e = At(world, 0, new Cell(2, 1));
            Assert.That(Until(e, Idle, x => x.RoomIndex != 0 || x.Status != ExpeditionStatus.Playing, 300), Is.True);
            Assert.That(e.Status, Is.EqualTo(ExpeditionStatus.Playing));
            Assert.That(e.RoomIndex, Is.EqualTo(1), "床の扉から下の部屋へ");
            Assert.That(e.Player.Body, Is.EqualTo(Room.PlaceOnTile(new Cell(2, 1), Player.Width, Player.Height)));
        }

        [Test]
        public void FallingThroughAHoleThatIsNotADoor_IsDown_EvenIfTheRoomHasAFloorDoor()
        {
            // レビュー P2-2：床に扉がある部屋では、扉でない穴から落ちても倒れず、下へ落ち続けていた。
            var world = FloorDoorAndHole();
            var e = At(world, 0, new Cell(4, 1));
            Assert.That(Until(e, Idle, x => x.Status != ExpeditionStatus.Playing || x.RoomIndex != 0, 300), Is.True, "落ち続けない");
            Assert.That(e.Status, Is.EqualTo(ExpeditionStatus.Down));
            Assert.That(e.RoomIndex, Is.Zero, "扉でない穴からは移らない");
            Assert.That(e.Deaths, Is.EqualTo(1));
        }

        [Test]
        public void ElapsedTicks_CountOnlyWhilePlaying()
        {
            var world = Worlds(Map(0, 0,
                "#######",
                "#.....#",
                "#S....#",
                "##..###"));
            var e = new Expedition(Config(), world);
            Until(e, Right, x => x.Status != ExpeditionStatus.Playing, 300);
            var elapsed = e.ElapsedTicks;
            Hold(e, Right, 10);
            Assert.That(e.ElapsedTicks, Is.EqualTo(elapsed), "倒れている間は数えない");
            e.Continue();
            e.Tick(Idle);
            Assert.That(e.ElapsedTicks, Is.EqualTo(elapsed + 1));
        }

        [Test]
        public void StartAt_ValidatesArguments()
        {
            var world = Real();
            Assert.Throws<System.ArgumentNullException>(() => Expedition.StartAt(Config(), world, null!, 0, new Cell(4, 2)));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => Expedition.StartAt(Config(), world, new WorldState(world), 9, new Cell(4, 2)));
        }
    }
}
