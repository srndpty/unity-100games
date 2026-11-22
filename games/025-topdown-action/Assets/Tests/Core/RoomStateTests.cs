using NUnit.Framework;
using TileMotion;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>完了判定「部屋跨ぎで状態一貫」と、部屋の切り替え（遷移）の規則。</summary>
    public sealed class RoomStateTests
    {
        private static readonly string[] _hall =
        {
            "#######",
            "#k....D",
            "#.S...D",
            "#.....D",
            "#######",
        };

        /// <summary>突進兵（構えが長いので動かない）を倒すと R に小鍵が出る部屋。</summary>
        private static readonly string[] _guard =
        {
            "#########",
            "D.......#",
            "D.c...R.#",
            "D.......#",
            "#########",
        };

        private static VaultConfig Calm() => new() { ChargerWindupTicks = VaultConfig.MaxStatusTicks, ChargerHealth = 1 };

        private static World Hall() => Worlds(Map(0, 0, _hall), Map(1, 0, _guard));

        private static void GoTo(Dungeon dungeon, int room, HeroInput input)
        {
            Assert.That(Until(dungeon, input, d => d.RoomIndex == room && d.Status == DungeonStatus.Playing, 800), Is.True, $"部屋 {room} へ移れない");
        }

        [Test]
        public void Flags_SurviveRoundTrips_WhileEnemiesComeBack()
        {
            var config = Calm();
            var dungeon = At(Hall(), 1, new Cell(3, 2), config);
            var events = Hold(dungeon, Left, 1);
            events |= Press(dungeon, In(attack: true));
            events |= Hold(dungeon, Idle, config.SwingTicks);
            Assert.That(dungeon.Run.Enemies[0].IsAlive, Is.False, "突進兵を倒した");
            Assert.That(events & DungeonEvents.RewardShown, Is.EqualTo(DungeonEvents.RewardShown), "全滅で小鍵が出る");
            Assert.That(dungeon.Run.RewardCell, Is.EqualTo(new Cell(6, 2)));

            WalkTo(dungeon, new Cell(6, 2));
            Assert.That(dungeon.State.SmallKeys, Is.EqualTo(1), "全滅の小鍵を取った");
            Assert.That(dungeon.Run.RewardCell.HasValue, Is.False);

            // 往復を 2 回。
            for (var trip = 0; trip < 2; trip++)
            {
                GoTo(dungeon, 0, Left);
                Assert.That(dungeon.State.SmallKeys, Is.EqualTo(1));
                GoTo(dungeon, 1, Right);
                Assert.That(dungeon.State.SmallKeys, Is.EqualTo(1), $"{trip + 1} 回目の往復");
                Assert.That(dungeon.State.IsRewardTaken(1), Is.True);
                Assert.That(dungeon.Run.RewardCell.HasValue, Is.False, "取った小鍵は二度と出ない");
                Assert.That(dungeon.Run.AliveCount, Is.EqualTo(1), "敵は入るたびに戻る");
                Assert.That(dungeon.Run.Enemies[0].Body, Is.EqualTo(new Cell(2, 2).Centered(Enemy.SizeOf(EnemyKind.Charger))), "出現位置から");
                Assert.That(dungeon.Run.Enemies[0].Health, Is.EqualTo(config.ChargerHealth), "満タンで");
            }

            Assert.That(dungeon.State.ShowReward(1), Is.False, "全滅の小鍵は二度出ない");
            Assert.That(dungeon.State.IsVisited(0) && dungeon.State.IsVisited(1), Is.True);
        }

        [Test]
        public void OpenedChest_StaysOpen_AcrossRooms()
        {
            var dungeon = At(Hall(), 0, new Cell(1, 2));
            Hold(dungeon, Up, 1);
            Press(dungeon, In(attack: true));
            Assert.That(dungeon.State.SmallKeys, Is.EqualTo(1));
            GoTo(dungeon, 1, Right);
            GoTo(dungeon, 0, Left);
            Assert.That(dungeon.State.IsChestOpen(0, 0), Is.True);
            Assert.That(dungeon.State.SmallKeys, Is.EqualTo(1));
        }

        [Test]
        public void HealthCarriesOver_ButEnemiesStartFresh()
        {
            var dungeon = At(Hall(), 0, new Cell(2, 2), new VaultConfig { MaxHealth = 6 });
            dungeon.Hero.Hurt(2, 0, 0);
            GoTo(dungeon, 1, Right);
            Assert.That(dungeon.Hero.Health, Is.EqualTo(4), "体力は持ち越す");
        }

        [Test]
        public void Transition_IgnoresInput_AndLastsTransitionTicks()
        {
            var config = Config();
            var dungeon = At(Hall(), 0, new Cell(2, 2), config);
            var events = DungeonEvents.None;
            Assert.That(Until(dungeon, Right, d =>
            {
                events |= d.TakeEvents();
                return d.Status == DungeonStatus.Transition;
            }, 400), Is.True);
            Assert.That(events & DungeonEvents.RoomChanged, Is.EqualTo(DungeonEvents.RoomChanged));
            Assert.That(dungeon.RoomIndex, Is.EqualTo(1));
            Assert.That(dungeon.PreviousRoom, Is.EqualTo(0));
            Assert.That(dungeon.EnteredFrom, Is.EqualTo(Side.Left));
            var arrival = dungeon.World.Rooms[1].Arrival(Side.Left);
            Assert.That(dungeon.Hero.Body, Is.EqualTo(arrival));

            var hash = dungeon.Run.Enemies[0].Body;
            for (var tick = 1; tick < config.TransitionTicks; tick++)
            {
                dungeon.Tick(In(x: 1, attack: true, roll: true));
                Assert.That(dungeon.Status, Is.EqualTo(DungeonStatus.Transition), $"切り替えの {tick} Tick 目");
                Assert.That(dungeon.TakeEvents() & (DungeonEvents.Swung | DungeonEvents.Rolled | DungeonEvents.Hurt), Is.EqualTo(DungeonEvents.None));
            }

            Assert.That(dungeon.Hero.Body, Is.EqualTo(arrival), "切り替え中は動かない");
            Assert.That(dungeon.Run.Enemies[0].Body, Is.EqualTo(hash), "敵も動かない");
            dungeon.Tick(Idle);
            Assert.That(dungeon.Status, Is.EqualTo(DungeonStatus.Playing));
            Assert.That(dungeon.TakeEvents() & DungeonEvents.Arrived, Is.EqualTo(DungeonEvents.Arrived));

            // 切り替えの前から押していた剣は、一度離すまで振らない。
            Assert.That(Hold(dungeon, In(attack: true), 5) & DungeonEvents.Swung, Is.EqualTo(DungeonEvents.None));
            Assert.That(Press(dungeon, In(attack: true)) & DungeonEvents.Swung, Is.EqualTo(DungeonEvents.Swung));
        }

        [Test]
        public void LethalHitAndExitInTheSameTick_TheHitWins()
        {
            var world = Worlds(Map(0, 0, "#######", "#.....D", "#S...aD", "#.....D", "#######"), Map(1, 0, "#####", "D...#", "D...#", "D...#", "#####"));
            var lethal = At(world, 0, new Cell(5, 2), new VaultConfig { HeroSpeed = VaultConfig.MaxSpeed, MaxHealth = 1 });
            lethal.Tick(Right);
            Assert.That(lethal.Status, Is.EqualTo(DungeonStatus.Down), "触れた Tick に出入口にも触れたが、倒れるほうが先");
            Assert.That(lethal.RoomIndex, Is.EqualTo(0));

            var survives = At(world, 0, new Cell(5, 2), new VaultConfig { HeroSpeed = VaultConfig.MaxSpeed, MaxHealth = 2 });
            survives.Tick(Right);
            Assert.That(survives.Status, Is.EqualTo(DungeonStatus.Transition), "倒れなければ同じ Tick に部屋を移る");
            Assert.That(survives.Hero.Health, Is.EqualTo(1));
        }

        [Test]
        public void Continue_RestartsAtTheEntrance_WithFlagsKept()
        {
            var dungeon = At(Hall(), 1, new Cell(4, 2), new VaultConfig { MaxHealth = 1 }, state => state.TryOpenChest(0, 0));
            dungeon.Hero.Hurt(1, 0, 0);
            dungeon.Tick(Idle);
            Assert.That(dungeon.Status, Is.EqualTo(DungeonStatus.Down));
            Assert.That(dungeon.Tick(Right), Is.False, "倒れている間の Tick は受け付けない");
            Assert.That(dungeon.Continue(), Is.True);
            Assert.That(dungeon.Continue(), Is.False, "二重の再開はしない");
            Assert.That(dungeon.RoomIndex, Is.EqualTo(0));
            Assert.That(dungeon.Hero.Body, Is.EqualTo(new Cell(2, 2).Centered(Hero.Size)), "入口の開始位置");
            Assert.That(dungeon.State.SmallKeys, Is.EqualTo(1));
            Assert.That(dungeon.State.IsChestOpen(0, 0), Is.True);
            Assert.That(dungeon.Deaths, Is.EqualTo(1));
        }

        /// <summary>マス <paramref name="cell"/> の中央まで縦横に歩く。</summary>
        private static void WalkTo(Dungeon dungeon, Cell cell)
        {
            var target = cell.Centered(Hero.Size);
            for (var i = 0; i < 400; i++)
            {
                var body = dungeon.Hero.Body;
                if (System.Math.Abs(body.X - target.X) < 24 && System.Math.Abs(body.Y - target.Y) < 24) { return; }
                dungeon.Tick(In(x: System.Math.Abs(body.X - target.X) < 24 ? 0 : System.Math.Sign(target.X - body.X), y: System.Math.Abs(body.Y - target.Y) < 24 ? 0 : System.Math.Sign(target.Y - body.Y)));
            }

            Assert.Fail($"{cell} に着かない");
        }
    }
}
