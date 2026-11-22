using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>題材固有テスト：ボス撃破フラグを維持（部屋の出入り・ゲームオーバーからの再開・複製）。</summary>
    public sealed class BossTests
    {
        private static readonly string[] _quiet =
        {
            "#######",
            "#.....D",
            "#.....D",
            "#..S..D",
            "#######",
        };

        /// <summary>右の出入口から入ると、左の射手と横に並ぶ。</summary>
        private static readonly string[] _shooter =
        {
            "#######",
            "#.....D",
            "#a....D",
            "#..S..D",
            "#######",
        };

        private static readonly string[] _lair =
        {
            "#########",
            "#.......#",
            "D.......#",
            "D.....M.#",
            "D.......#",
            "#.......#",
            "#########",
        };

        private static World Arena(string[] entrance) => Worlds(Map(0, 0, entrance), Map(1, 0, _lair));

        /// <summary>ボスの間でボスの左 2 マスから右を向いて斬る（ボスの体力は 1）。</summary>
        private static Dungeon Defeat(World world, VaultConfig config)
        {
            var dungeon = At(world, 1, new Cell(4, 3), config);
            Hold(dungeon, Right, 1);
            var events = Press(dungeon, In(attack: true));
            events |= Hold(dungeon, Idle, config.SwingActiveTo);
            Assert.That(events & DungeonEvents.BossDefeated, Is.EqualTo(DungeonEvents.BossDefeated));
            Assert.That(dungeon.State.BossDefeated, Is.True);
            return dungeon;
        }

        private static VaultConfig OneHitBoss() => new() { BossHealth = 1 };

        [Test]
        public void DefeatedBoss_DoesNotReturn_AfterLeavingAndComingBack()
        {
            var dungeon = Defeat(Arena(_quiet), OneHitBoss());
            Assert.That(dungeon.Run.Boss, Is.Null);
            Assert.That(dungeon.Run.TreasureCell, Is.EqualTo(new Cell(6, 3)), "ボスのいた場所に秘宝");
            Assert.That(dungeon.Run.Projectiles, Is.Empty, "撃破でボスの弾は消える");

            Assert.That(Until(dungeon, Left, d => d.RoomIndex == 0 && d.Status == DungeonStatus.Playing, 600), Is.True);
            Assert.That(Until(dungeon, Right, d => d.RoomIndex == 1 && d.Status == DungeonStatus.Playing, 600), Is.True);
            Assert.That(dungeon.State.BossDefeated, Is.True);
            Assert.That(dungeon.Run.Boss, Is.Null, "入り直してもボスは出ない");
            Assert.That(dungeon.Run.Enemies, Is.Empty);
            Assert.That(dungeon.Run.TreasureCell, Is.EqualTo(new Cell(6, 3)), "秘宝は残る");
        }

        [Test]
        public void DefeatedBoss_StaysDefeated_AfterGameOverAndContinue()
        {
            var config = OneHitBoss();
            config.MaxHealth = 1;
            var dungeon = Defeat(Arena(_shooter), config);
            Assert.That(Until(dungeon, Left, d => d.RoomIndex == 0 && d.Status == DungeonStatus.Playing, 600), Is.True);
            Assert.That(Until(dungeon, Idle, d => d.Status == DungeonStatus.Down, 600), Is.True, "射手の弾で倒れる");
            Assert.That(dungeon.Deaths, Is.EqualTo(1));
            Assert.That(dungeon.Continue(), Is.True);
            Assert.That(dungeon.State.BossDefeated, Is.True, "再開してもボス撃破は残る");
            Assert.That(dungeon.Hero.Health, Is.EqualTo(config.MaxHealth));

            Assert.That(Until(dungeon, Right, d => d.RoomIndex == 1 && d.Status == DungeonStatus.Playing, 600), Is.True);
            Assert.That(dungeon.Run.Boss, Is.Null);
            Assert.That(dungeon.Run.TreasureCell.HasValue, Is.True);
        }

        [Test]
        public void TouchingTheTreasure_Wins_AndLaterTicksDoNothing()
        {
            var dungeon = Defeat(Arena(_quiet), OneHitBoss());
            var events = DungeonEvents.None;
            Assert.That(Until(dungeon, Right, d =>
            {
                events |= d.TakeEvents();
                return d.IsWon;
            }, 200), Is.True);
            Assert.That(events & DungeonEvents.Won, Is.EqualTo(DungeonEvents.Won));
            var hash = dungeon.Hash();
            Assert.That(dungeon.Tick(Right), Is.False, "勝った後の Tick は受け付けない");
            Assert.That(dungeon.Continue(), Is.False);
            Assert.That(dungeon.Hash(), Is.EqualTo(hash));
        }

        [Test]
        public void TreasureDoesNotExist_BeforeTheBossIsDefeated()
        {
            var dungeon = At(Arena(_quiet), 1, new Cell(1, 3));
            Assert.That(dungeon.Run.TreasureCell.HasValue, Is.False);
            Assert.That(dungeon.Run.Boss, Is.Not.Null);
        }

        [Test]
        public void CloneTakenBeforeTheDefeat_IsNotChangedByIt()
        {
            var config = OneHitBoss();
            var dungeon = At(Arena(_quiet), 1, new Cell(4, 3), config);
            var before = dungeon.Clone();
            var state = dungeon.State.Clone();
            Hold(dungeon, Right, 1);
            Press(dungeon, In(attack: true));
            Hold(dungeon, Idle, config.SwingActiveTo);
            Assert.That(dungeon.State.BossDefeated, Is.True);
            Assert.That(before.State.BossDefeated, Is.False);
            Assert.That(before.Run.Boss, Is.Not.Null);
            Assert.That(state.BossDefeated, Is.False);
        }

        [Test]
        public void UndefeatedBoss_IsBackAtFullHealth_AfterLeaving()
        {
            var config = new VaultConfig { BossHealth = 3 };
            var dungeon = At(Arena(_quiet), 1, new Cell(4, 3), config);
            Hold(dungeon, Right, 1);
            Press(dungeon, In(attack: true));
            Hold(dungeon, Idle, config.SwingActiveTo);
            Assert.That(dungeon.Run.Boss!.Health, Is.EqualTo(2));
            Assert.That(Until(dungeon, Left, d => d.RoomIndex == 0 && d.Status == DungeonStatus.Playing, 600), Is.True);
            Assert.That(Until(dungeon, Right, d => d.RoomIndex == 1 && d.Status == DungeonStatus.Playing, 600), Is.True);
            Assert.That(dungeon.Run.Boss!.Health, Is.EqualTo(3), "撃破していないボスの体力は部屋ごと");
        }

        [Test]
        public void DefeatBossTwice_ReturnsFalse()
        {
            var state = new DungeonState(Arena(_quiet));
            Assert.That(state.DefeatBoss(), Is.True);
            Assert.That(state.DefeatBoss(), Is.False);
            Assert.That(state.BossDefeated, Is.True);
        }
    }
}
