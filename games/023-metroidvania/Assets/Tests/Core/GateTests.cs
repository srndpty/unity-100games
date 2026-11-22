using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>
    /// 題材固有：未取得の能力ではゲートを通れず、取得後は通れる・戻れる。本番の部屋で確かめる。
    /// 高さのゲートは到達範囲の探索（<see cref="Reachability"/>）、ひびの壁は壁の列が床から天井までふさいでいることと、実際に撃って通ること。
    /// </summary>
    public sealed class GateTests
    {
        private static readonly Cell _hubStart = new(4, 2);
        private static readonly Cell _pitFloor = new(14, 2);
        /// <summary>塔の床の穴（列 30〜32）より右。</summary>
        private static readonly Cell _towerEast = new(33, 2);

        [Test]
        public void HighLedge_WithoutDoubleJump_TheTowerIsUnreachable()
        {
            var world = Real();
            var search = Reachability.Run(At(world, Rooms.Hub, _hubStart), e => e.RoomIndex == Rooms.Tower);
            TestContext.Out.WriteLine($"展開 {search.Expanded}、最高 {search.HighestFeet / (double)T:F2} タイル");
            Assert.That(search.Capped, Is.False, "到達できる状態を尽くした");
            Assert.That(search.Found, Is.False, "塔へ行けない");
            Assert.That(search.HighestFeet, Is.LessThan(7 * T), "高い段（足場の上面 7 タイル）に乗れない");
        }

        [Test]
        public void HighLedge_WithDoubleJump_LeadsToTheTower()
        {
            var world = Real();
            var search = Reachability.Run(At(world, Rooms.Hub, _hubStart, Ability.DoubleJump), e => e.RoomIndex == Rooms.Tower, (31 * T, 21 * T));
            TestContext.Out.WriteLine($"展開 {search.Expanded}");
            Assert.That(search.Found, Is.True);
        }

        [Test]
        public void Pit_WithoutDoubleJump_CannotGoBack()
        {
            // 二段ジャンプの玉を取ったことにだけして（能力は与えずに）、穴の底から戻れるかを調べる。
            var world = Real();
            var orb = FindPickup(world, PickupKind.DoubleJump);
            var state = new WorldState(world);
            state.Take(world.PickupId(orb.Room, orb.Index));
            var search = Reachability.Run(Expedition.StartAt(Config(), world, state, Rooms.Cave, _pitFloor), e => e.RoomIndex == Rooms.Corridor);
            TestContext.Out.WriteLine($"展開 {search.Expanded}、最高 {search.HighestFeet / (double)T:F2} タイル");
            Assert.That(search.Capped, Is.False);
            Assert.That(search.Found, Is.False, "穴から出られない");
        }

        [Test]
        public void Pit_AfterTakingDoubleJump_LeadsBack()
        {
            var world = Real();
            var e = At(world, Rooms.Cave, _pitFloor);
            Assert.That(e.State.Has(Ability.DoubleJump), Is.False);

            // 右へ歩いて玉を取る。
            Assert.That(Until(e, Right, x => x.State.Has(Ability.DoubleJump)), Is.True);
            var search = Reachability.Run(e, x => x.RoomIndex == Rooms.Corridor, (0, 7 * T));
            Assert.That(search.Found, Is.True, "取った後は回廊へ戻れる");
        }

        [Test]
        public void CrackedWall_SealsTheLairFromFloorToCeiling()
        {
            var tower = Real().Rooms[Rooms.Tower];
            var terrain = new Terrain(tower.Map, new bool[tower.Map.Width * tower.Map.Height]);
            var cracked = 0;
            for (var row = 0; row < tower.Map.Height; row++)
            {
                Assert.That(terrain.IsSolid(35, row), Is.True, $"列 35 の行 {row} は通れない");
                if (tower.Map[35, row] == TileKind.Cracked) { cracked++; }
            }

            Assert.That(cracked, Is.EqualTo(3), "通れるようになるのはひびの壁の 3 マスだけ");
            Assert.That(tower.HasDoor(Side.Right), Is.True);
        }

        [Test]
        public void CrackedWall_WithoutBreakShot_ShotsDoNotOpenIt()
        {
            var e = At(Real(), Rooms.Tower, _towerEast, Ability.DoubleJump);
            for (var i = 0; i < 600; i++) { e.Tick(In(1, jump: i % 30 < 15, shoot: i % 2 == 0)); }
            Assert.That(e.RoomIndex, Is.EqualTo(Rooms.Tower));
            Assert.That(e.State.BrokenCount, Is.Zero);
            Assert.That(e.Player.Body.Right, Is.LessThanOrEqualTo(35 * T));
        }

        [Test]
        public void CrackedWall_WithBreakShot_OpensTheWayToTheLair()
        {
            var e = At(Real(), Rooms.Tower, _towerEast, Ability.DoubleJump | Ability.BreakShot);
            var events = ExpeditionEvents.None;
            for (var i = 0; i < 600 && e.RoomIndex == Rooms.Tower; i++)
            {
                e.Tick(In(1, shoot: i % 2 == 0));
                events |= e.TakeEvents();
            }

            Assert.That(events & ExpeditionEvents.WallBroken, Is.EqualTo(ExpeditionEvents.WallBroken));
            Assert.That(e.RoomIndex, Is.EqualTo(Rooms.Lair));
        }
    }
}
