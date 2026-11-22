using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>題材固有テスト：鍵消費後のみ扉が開く。</summary>
    public sealed class DoorKeyTests
    {
        private static readonly string[] _first =
        {
            "#######",
            "#k....X",
            "#.S...X",
            "#K....X",
            "#######",
        };

        private static readonly string[] _next =
        {
            "#######",
            "X.....Y",
            "X.....Y",
            "X.....Y",
            "#######",
        };

        /// <summary>最初の部屋（小鍵の宝箱・ボス鍵の宝箱・開始位置）。右の辺に扉 <paramref name="door"/>。</summary>
        private static string[] First(char door) => _first.Replace('X', door);

        /// <summary>隣の部屋。左の辺に扉 <paramref name="left"/>、右の辺に <paramref name="right"/>（'#' なら壁）。</summary>
        private static string[] Next(char left, char right) => _next.Replace('X', left).Replace('Y', right);

        private static World SmallPair() => Worlds(Map(0, 0, First('L')), Map(1, 0, Next('L', '#')));

        private static readonly Cell _start = new(2, 2);

        private static Dungeon WithKeys(World world, int smallKeys, bool bossKey)
        {
            return At(world, 0, _start, setup: state =>
            {
                if (smallKeys >= 1) { state.TryOpenChest(0, 0); }
                if (bossKey) { state.TryOpenChest(0, 1); }
                Assert.That(state.SmallKeys, Is.EqualTo(smallKeys));
            });
        }

        [Test]
        public void SmallDoor_WithoutKey_StaysSolid_AndKeyCountUnchanged()
        {
            var dungeon = WithKeys(SmallPair(), 0, false);
            var events = Hold(dungeon, Right, 120);
            Assert.That(dungeon.RoomIndex, Is.EqualTo(0), "鍵なしでは扉を通れない");
            Assert.That(dungeon.Hero.Body.Right, Is.EqualTo(6 * T), "扉の手前で止まる");
            Assert.That(events & DungeonEvents.DoorLocked, Is.Not.EqualTo(DungeonEvents.None));
            Assert.That(events & DungeonEvents.DoorOpened, Is.EqualTo(DungeonEvents.None));
            Assert.That(dungeon.State.SmallKeys, Is.EqualTo(0));
            Assert.That(dungeon.State.IsOpen(0, Side.Right), Is.False);
        }

        [Test]
        public void SmallDoor_WithKey_ConsumesOneKey_AndOpensBothSides()
        {
            var dungeon = WithKeys(SmallPair(), 1, false);
            DungeonEvents events = DungeonEvents.None;
            Assert.That(Until(dungeon, Right, d =>
            {
                events |= d.TakeEvents();
                return (events & DungeonEvents.DoorOpened) != 0;
            }), Is.True);
            Assert.That(dungeon.State.SmallKeys, Is.EqualTo(0), "小鍵を 1 つ使う");
            Assert.That(dungeon.State.IsOpen(0, Side.Right), Is.True);
            Assert.That(dungeon.State.IsOpen(1, Side.Left), Is.True, "反対側も同じ 1 枚");
            Assert.That(dungeon.Run.Terrain.IsSolid(6, 2), Is.False, "開いた扉は固体でない");

            Assert.That(Until(dungeon, Right, d => d.RoomIndex == 1, 200), Is.True, "開いた扉を通って隣の部屋へ");
        }

        [Test]
        public void OpenedDoor_StaysOpen_WhenComingBack_WithoutAnotherKey()
        {
            var dungeon = WithKeys(SmallPair(), 1, false);
            Assert.That(Until(dungeon, Right, d => d.RoomIndex == 1 && d.Status == DungeonStatus.Playing, 400), Is.True);
            Assert.That(Until(dungeon, Left, d => d.RoomIndex == 0 && d.Status == DungeonStatus.Playing, 400), Is.True, "戻るときに鍵は要らない");
            Assert.That(dungeon.State.SmallKeys, Is.EqualTo(0));
            Assert.That(Until(dungeon, Right, d => d.RoomIndex == 1, 400), Is.True, "何度でも通れる");
        }

        [Test]
        public void SecondDoor_NeedsAnotherKey()
        {
            var world = Worlds(Map(0, 0, First('L')), Map(1, 0, Next('L', 'L')), Map(2, 0, Next('L', '#')));
            var dungeon = WithKeys(world, 1, false);
            Assert.That(Until(dungeon, Right, d => d.RoomIndex == 1 && d.Status == DungeonStatus.Playing, 400), Is.True);
            var events = Hold(dungeon, Right, 120);
            Assert.That(dungeon.RoomIndex, Is.EqualTo(1), "2 枚目は開かない");
            Assert.That(events & DungeonEvents.DoorLocked, Is.Not.EqualTo(DungeonEvents.None));
            Assert.That(dungeon.State.IsOpen(1, Side.Right), Is.False);
        }

        [Test]
        public void BossDoor_DoesNotOpenWithSmallKey()
        {
            var world = Worlds(Map(0, 0, First('B')), Map(1, 0, Next('B', '#')));
            var dungeon = WithKeys(world, 1, false);
            var events = Hold(dungeon, Right, 120);
            Assert.That(dungeon.RoomIndex, Is.EqualTo(0));
            Assert.That(events & DungeonEvents.DoorLocked, Is.Not.EqualTo(DungeonEvents.None));
            Assert.That(dungeon.State.SmallKeys, Is.EqualTo(1), "小鍵は使わない");
        }

        [Test]
        public void BossDoor_OpensWithBossKey_WhichIsNotConsumed()
        {
            var world = Worlds(Map(0, 0, First('B')), Map(1, 0, Next('B', '#')));
            var dungeon = WithKeys(world, 0, true);
            Assert.That(Until(dungeon, Right, d => d.RoomIndex == 1, 400), Is.True);
            Assert.That(dungeon.State.HasBossKey, Is.True, "ボス鍵は無くならない");
        }

        [Test]
        public void SmallDoor_DoesNotOpenWithBossKey()
        {
            var dungeon = WithKeys(SmallPair(), 0, true);
            Hold(dungeon, Right, 120);
            Assert.That(dungeon.RoomIndex, Is.EqualTo(0));
            Assert.That(dungeon.State.IsOpen(0, Side.Right), Is.False);
        }

        [Test]
        public void State_TryOpen_ReportsEachCase()
        {
            var state = new DungeonState(SmallPair());
            Assert.That(state.TryOpen(0, Side.Right), Is.EqualTo(DoorResult.NoKey));
            state.TryOpenChest(0, 0);
            Assert.That(state.TryOpen(1, Side.Left), Is.EqualTo(DoorResult.Opened), "反対側から開けても同じ扉");
            Assert.That(state.SmallKeys, Is.EqualTo(0));
            Assert.That(state.TryOpen(0, Side.Right), Is.EqualTo(DoorResult.AlreadyOpen), "開いた扉では鍵を使わない");
        }

        [Test]
        public void Chest_OpenedTwice_GivesOneKey()
        {
            var dungeon = At(SmallPair(), 0, new Cell(1, 2));

            // 宝箱（1, 3）は真上。上を向く。
            Hold(dungeon, Up, 1);
            var first = Press(dungeon, In(attack: true));
            var second = Press(dungeon, In(attack: true));
            Assert.That(first & DungeonEvents.ChestOpened, Is.Not.EqualTo(DungeonEvents.None));
            Assert.That(second & DungeonEvents.ChestOpened, Is.EqualTo(DungeonEvents.None), "開いた宝箱の前では剣を振る");
            Assert.That(second & DungeonEvents.Swung, Is.Not.EqualTo(DungeonEvents.None));
            Assert.That(dungeon.State.SmallKeys, Is.EqualTo(1));
            Assert.That(dungeon.State.TryOpenChest(0, 0), Is.False);
            Assert.That(dungeon.State.SmallKeys, Is.EqualTo(1));
        }

        [Test]
        public void Chest_NotInFront_IsNotOpened()
        {
            var dungeon = At(SmallPair(), 0, new Cell(4, 2));
            var events = Press(dungeon, In(attack: true));
            Assert.That(events & DungeonEvents.ChestOpened, Is.EqualTo(DungeonEvents.None));
            Assert.That(events & DungeonEvents.Swung, Is.Not.EqualTo(DungeonEvents.None));
            Assert.That(dungeon.State.SmallKeys, Is.EqualTo(0));
        }
    }

    internal static class RowsExtensions
    {
        public static string[] Replace(this string[] rows, char from, char to)
        {
            var result = new string[rows.Length];
            for (var i = 0; i < rows.Length; i++) { result[i] = rows[i].Replace(from, to); }
            return result;
        }
    }
}
