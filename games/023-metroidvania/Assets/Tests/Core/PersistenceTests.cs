using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>
    /// 題材固有：部屋に入り直しても重要な状態（能力・壊した壁・取ったアイテム・訪れた部屋）は残り、
    /// 部屋ごとの状態（敵・弾）は初めからになる。本番の部屋で確かめる。
    /// </summary>
    public sealed class PersistenceTests
    {
        /// <summary>回廊の台の上（列 16、行 5）。右にひびの壁（列 21）、その奥に回復の実。</summary>
        private static readonly Cell _corridorShelf = new(16, 5);

        /// <summary>塔の床の穴より右。右にひびの壁（列 35）。</summary>
        private static readonly Cell _towerEast = new(33, 2);

        /// <summary>弾を押し直しながら、同じ向きに歩く。</summary>
        private static bool WalkShooting(Expedition e, int horizontal, System.Func<Expedition, bool> done, int limit = 2000)
        {
            for (var i = 0; i < limit; i++)
            {
                if (done(e)) { return true; }
                e.Tick(In(horizontal, shoot: i % 2 == 0));
            }

            return done(e);
        }

        [Test]
        public void TowerWall_StaysBroken_AfterVisitingTheLairAndComingBack()
        {
            var world = Real();
            var e = At(world, Rooms.Tower, _towerEast, Ability.DoubleJump | Ability.BreakShot);
            Assert.That(WalkShooting(e, 1, x => x.RoomIndex == Rooms.Lair), Is.True);
            var broken = e.State.BrokenCount;
            Assert.That(broken, Is.GreaterThan(0));

            // ボスの部屋の入口から左へ戻る。
            Assert.That(Until(e, Left, x => x.RoomIndex == Rooms.Tower), Is.True);
            Assert.That(e.Run.Room.Arrival(Side.Right), Is.Not.EqualTo(default(Cell)));
            Assert.That(e.State.BrokenCount, Is.EqualTo(broken), "壊れた壁はそのまま");
            Assert.That(e.State.Has(Ability.DoubleJump | Ability.BreakShot), Is.True, "能力もそのまま");

            // 壊れた所を通って左へ歩ける（もう撃たなくてよい）。
            Assert.That(Until(e, Left, x => x.Player.Body.Right < 35 * T, 200), Is.True);
            Assert.That(e.State.IsVisited(Rooms.Lair), Is.True);
        }

        [Test]
        public void CorridorFruit_StaysTaken_AndWallStaysBroken_AfterLeaving()
        {
            var world = Real();
            var config = Config();
            var e = At(world, Rooms.Corridor, _corridorShelf, Ability.BreakShot, config);
            e.Player.Hurt(3, -1);
            var fruit = FindPickup(world, PickupKind.Fruit);
            Assert.That(fruit.Room, Is.EqualTo(Rooms.Corridor));
            Assert.That(WalkShooting(e, 1, x => !x.Run.IsPickupPresent(fruit.Index), 600), Is.True, "壁を壊して実を取る");
            Assert.That(e.Player.Health, Is.EqualTo(config.MaxHealth - 3 + config.FruitHeal));

            // 左の扉から拠点へ出て、右の扉から戻る。
            Assert.That(WalkShooting(e, -1, x => x.RoomIndex == Rooms.Hub), Is.True);
            Assert.That(Until(e, Right, x => x.RoomIndex == Rooms.Corridor), Is.True);
            Assert.That(e.Run.IsPickupPresent(fruit.Index), Is.False, "取った実は戻ってこない");
            Assert.That(e.Run.Terrain.IsSolid(21, 5), Is.False, "壊した壁は壊れたまま");
            Assert.That(e.State.TakenCount, Is.EqualTo(1));
        }

        [Test]
        public void Walkers_RespawnOnReentry()
        {
            var world = Real();
            var e = At(world, Rooms.Corridor, new Cell(5, 2));
            var spawn = e.Run.Walkers[0].Body;
            Assert.That(WalkShooting(e, 1, x => !x.Run.Walkers[0].IsAlive, 600), Is.True, "1 体目を倒す");
            Assert.That(Until(e, Left, x => x.RoomIndex == Rooms.Hub), Is.True);
            Assert.That(Until(e, Right, x => x.RoomIndex == Rooms.Corridor), Is.True);
            Assert.That(e.Run.Walkers[0].IsAlive, Is.True, "敵は部屋に入るたびに初めから");
            Assert.That(e.Run.Walkers[0].Body, Is.EqualTo(spawn));
            Assert.That(e.Run.Shots, Is.Empty, "弾は部屋をまたがない");
        }

        [Test]
        public void Health_CarriesAcrossRooms()
        {
            var world = Real();
            var e = At(world, Rooms.Hub, new Cell(30, 2));
            e.Player.Hurt(2, 1);
            Assert.That(Until(e, Right, x => x.RoomIndex == Rooms.Corridor), Is.True);
            Assert.That(e.Player.Health, Is.EqualTo(Config().MaxHealth - 2));
            Assert.That(e.Player.Body, Is.EqualTo(Room.PlaceOnTile(world.Rooms[Rooms.Corridor].Arrival(Side.Left), Player.Width, Player.Height)), "左の扉の到着位置");
            Assert.That(e.RoomEntries, Is.EqualTo(2));
            Assert.That(e.RoomTicks, Is.Zero);
        }

        [Test]
        public void VerticalDoors_LinkHubAndTower()
        {
            var world = Real();

            // 塔の床の穴へ落ちると、拠点の上の足場に着く。
            var e = At(world, Rooms.Tower, new Cell(28, 2));
            Assert.That(Until(e, Right, x => x.RoomIndex == Rooms.Hub, 600), Is.True);
            Assert.That(e.Player.Body, Is.EqualTo(Room.PlaceOnTile(world.Rooms[Rooms.Hub].Arrival(Side.Top), Player.Width, Player.Height)));
            Assert.That(e.TakeEvents() & ExpeditionEvents.RoomChanged, Is.EqualTo(ExpeditionEvents.RoomChanged));
            Assert.That(Hold(e, Idle, 30) & ExpeditionEvents.RoomChanged, Is.EqualTo(ExpeditionEvents.None), "着いた位置で扉に触れ直さない");
            Assert.That(e.Player.Grounded, Is.True);
        }

        [Test]
        public void WorldState_RejectsDoubleTakesAndUnknownAbilities()
        {
            var state = new WorldState(Real());
            Assert.That(state.Take(0), Is.True);
            Assert.That(state.Take(0), Is.False, "同じアイテムは二重に取れない");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => state.Grant(Ability.None));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => state.Grant((Ability)8));
            Assert.Throws<System.ArgumentException>(() => state.Break(Rooms.Hub, 5, 5, Real().Rooms[Rooms.Hub].Map), "ひびの壁でない所は壊せない");
        }

        [Test]
        public void WorldState_CloneIsIndependent()
        {
            var world = Real();
            var state = new WorldState(world);
            var clone = state.Clone();
            state.Grant(Ability.DoubleJump);
            state.Take(1);
            state.Break(Rooms.Tower, 35, 2, world.Rooms[Rooms.Tower].Map);
            Assert.That(clone.Has(Ability.DoubleJump), Is.False);
            Assert.That(clone.IsTaken(1), Is.False);
            Assert.That(clone.BrokenCount, Is.Zero);
        }
    }
}
