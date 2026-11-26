using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>満腹度と自然回復。</summary>
    public sealed class HungerTests
    {
        [Test]
        public void SatietyDropsEveryTenActionsAndTurningIsFree()
        {
            var run = Lab.Center(1);
            var start = run.Player.Satiety;
            for (int i = 0; i < 9; i++) { run.Submit(Command.Wait()); }
            Assert.That(run.Player.Satiety, Is.EqualTo(start));
            for (int i = 0; i < 20; i++) { run.Submit(Command.Face((Direction)(i % 8))); }
            Assert.That(run.Player.Satiety, Is.EqualTo(start), "向きを変えるのは行動に数えない");
            run.Submit(Command.Wait());
            Assert.That(run.Player.Satiety, Is.EqualTo(start - 1));
        }

        [Test]
        public void WarningsAtTwentyAndZeroThenStarvationHurts()
        {
            var run = Lab.Center(2);
            var c = run.Config;
            run.Player.Satiety = c.HungryWarning + 1;
            run.Player.Actions = c.HungerEvery - 1;
            run.Submit(Command.Wait());
            Assert.That(run.Player.Satiety, Is.EqualTo(c.HungryWarning));
            Assert.That(Lab.EventsOf(run, EventKind.Hungry), Has.Count.EqualTo(1));

            run.Player.Satiety = 1;
            run.Player.Actions = c.HungerEvery - 1;
            var hp = run.Player.Hp;
            run.Submit(Command.Wait());
            Assert.That(run.Player.Satiety, Is.EqualTo(0));
            Assert.That(Lab.EventsOf(run, EventKind.Starving), Has.Count.EqualTo(1));
            Assert.That(run.Player.Hp, Is.EqualTo(hp - c.StarveDamage), "0 になった行動から減る");
            run.Submit(Command.Wait());
            Assert.That(run.Player.Hp, Is.EqualTo(hp - (2 * c.StarveDamage)));
            Assert.That(Lab.EventsOf(run, EventKind.Starving), Is.Empty, "知らせは 1 回");
        }

        [Test]
        public void NoRegenerationWhileStarvingAndEatingRestores()
        {
            var run = Lab.Center(3);
            run.Player.Satiety = 0;
            run.Player.Hp = 20;
            for (int i = 0; i < run.Config.RegenEvery * 2; i++) { run.Submit(Command.Wait()); }
            Assert.That(run.Player.Hp, Is.EqualTo(20 - (run.Config.RegenEvery * 2)));
            run.Player.Inventory.Add(ItemKind.Bread);
            run.Submit(Command.Use(0));
            Assert.That(run.Player.Satiety, Is.EqualTo(run.Config.BreadSatiety));
        }

        [Test]
        public void HpRegeneratesEverySixActionsUpToTheMaximum()
        {
            var run = Lab.Center(4);
            run.Player.Hp = run.Player.MaxHp - 2;
            run.Player.Actions = 0;
            for (int i = 0; i < run.Config.RegenEvery - 1; i++) { run.Submit(Command.Wait()); }
            Assert.That(run.Player.Hp, Is.EqualTo(run.Player.MaxHp - 2));
            run.Submit(Command.Wait());
            Assert.That(run.Player.Hp, Is.EqualTo(run.Player.MaxHp - 1));
            for (int i = 0; i < run.Config.RegenEvery * 3; i++) { run.Submit(Command.Wait()); }
            Assert.That(run.Player.Hp, Is.EqualTo(run.Player.MaxHp));
        }

        [Test]
        public void StarvationKillsWithItsOwnCause()
        {
            var run = Lab.Center(5);
            run.Player.Satiety = 0;
            run.Player.Hp = 3;
            for (int i = 0; i < 10 && run.State == RunState.Playing; i++) { run.Submit(Command.Wait()); }
            Assert.That(run.State, Is.EqualTo(RunState.Dead));
            Assert.That(run.DeathCause, Is.EqualTo(DeathCause.Starvation));
            Assert.That(run.Player.Actions, Is.EqualTo(3));
        }
    }
}
