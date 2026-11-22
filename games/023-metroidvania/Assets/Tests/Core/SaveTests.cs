using NUnit.Framework;
using static Game.Core.Tests.TestKit;

namespace Game.Core.Tests
{
    /// <summary>セーブ像と、体力 0 からの再開。</summary>
    public sealed class SaveTests
    {
        /// <summary>
        /// 1 部屋の世界。右へ歩くと 回復の実（列 2）→ セーブ像（列 3）→ 二段ジャンプ（列 4）→ 回復の実（列 5）の順に触れ、列 7 の敵に当たる。
        /// </summary>
        private static World Line() => Worlds(Map(0, 0,
            "##########",
            "#........#",
            "#........#",
            "#S+$J+.w.#",
            "##########"));

        private static Expedition Fragile(World world)
        {
            var config = Config();
            config.MaxHealth = 1;
            return new Expedition(config, world);
        }

        [Test]
        public void Statue_SavesOnTouch_AndHeals()
        {
            var world = Line();
            var config = Config();
            var e = new Expedition(config, world);
            e.Player.Hurt(2, -1);
            Hold(e, Idle, config.InvincibleTicks + 1);
            var events = ExpeditionEvents.None;
            for (var i = 0; i < 200 && (events & ExpeditionEvents.Saved) == 0; i++)
            {
                e.Tick(Right);
                events |= e.TakeEvents();
            }

            Assert.That(events & ExpeditionEvents.Saved, Is.EqualTo(ExpeditionEvents.Saved));
            Assert.That(e.Save.Cell, Is.EqualTo(new Cell(3, 1)));
            Assert.That(e.Player.Health, Is.EqualTo(config.MaxHealth), "像に触れると全回復");
            Assert.That(e.Save.State.IsTaken(0), Is.True, "像より前に取った実は記録に入る");
            Assert.That(e.Save.State.Has(Ability.DoubleJump), Is.False);
        }

        [Test]
        public void Statue_DoesNotSaveAgainWhileStillTouching()
        {
            var e = new Expedition(Config(), Line());
            Assert.That(Until(e, Right, x => (x.TakeEvents() & ExpeditionEvents.Saved) != 0, 200), Is.True);
            var save = e.Save;
            Hold(e, Idle, 30);
            Assert.That(e.Save, Is.SameAs(save), "触れたまま止まっていても記録し直さない");
        }

        [Test]
        public void Down_ThenContinue_RestoresTheSavedWorld()
        {
            var e = Fragile(Line());
            var events = ExpeditionEvents.None;
            for (var i = 0; i < 400 && e.Status == ExpeditionStatus.Playing; i++)
            {
                e.Tick(Right);
                events |= e.TakeEvents();
            }

            Assert.That(e.Status, Is.EqualTo(ExpeditionStatus.Down));
            Assert.That(events & ExpeditionEvents.Down, Is.EqualTo(ExpeditionEvents.Down));
            Assert.That(e.State.Has(Ability.DoubleJump), Is.True, "倒れる前は持っていた");
            Assert.That(e.State.TakenCount, Is.EqualTo(3));
            Assert.That(e.Deaths, Is.EqualTo(1));
            Assert.That(e.Tick(Right), Is.False, "倒れている間は進まない");

            Assert.That(e.Continue(), Is.True);
            Assert.That(e.TakeEvents() & ExpeditionEvents.Continued, Is.EqualTo(ExpeditionEvents.Continued));
            Assert.That(e.Status, Is.EqualTo(ExpeditionStatus.Playing));
            Assert.That(e.State.IsTaken(0), Is.True, "セーブ前に取った実は残る");
            Assert.That(e.State.Has(Ability.DoubleJump), Is.False, "セーブ後に取った能力は失う");
            Assert.That(e.State.IsTaken(1) || e.State.IsTaken(2), Is.False, "セーブ後に取ったアイテムは戻る");
            Assert.That(e.Player.Body, Is.EqualTo(Room.PlaceOnTile(new Cell(3, 1), Player.Width, Player.Height)), "像の前から");
            Assert.That(e.Player.Health, Is.EqualTo(1));
            Assert.That(e.Run.Walkers[0].IsAlive, Is.True);
            Assert.That(Hold(e, Idle, 5) & ExpeditionEvents.Saved, Is.EqualTo(ExpeditionEvents.None), "像の上に戻っても記録し直さない");
        }

        [Test]
        public void Continue_OnlyWhenDown()
        {
            var e = new Expedition(Config(), Line());
            Assert.That(e.Continue(), Is.False);
            Assert.That(e.TakeEvents(), Is.EqualTo(ExpeditionEvents.None));
        }

        [Test]
        public void Continue_WithoutAnyStatue_RestartsFromTheStart()
        {
            var world = Worlds(Map(0, 0,
                "########",
                "#......#",
                "#S+..w.#",
                "########"));
            var e = Fragile(world);
            Assert.That(Until(e, Right, x => x.Status == ExpeditionStatus.Down, 400), Is.True);
            Assert.That(e.State.IsTaken(0), Is.True);
            e.Continue();
            Assert.That(e.State.IsTaken(0), Is.False, "開始位置の記録（何も取っていない）に戻る");
            Assert.That(e.Player.Body, Is.EqualTo(Room.PlaceOnTile(new Cell(1, 1), Player.Width, Player.Height)));
        }

        [Test]
        public void SaveRecord_IsNotChangedByLaterProgress()
        {
            var e = new Expedition(Config(), Line());
            Assert.That(Until(e, Right, x => (x.TakeEvents() & ExpeditionEvents.Saved) != 0, 200), Is.True);
            Hold(e, Right, 20);
            Assert.That(e.State.Has(Ability.DoubleJump), Is.True);
            Assert.That(e.Save.State.Has(Ability.DoubleJump), Is.False, "記録は複製");
        }
    }
}
