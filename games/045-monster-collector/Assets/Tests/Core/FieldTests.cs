using System.Collections.Generic;
using NUnit.Framework;

namespace Game.Core.Tests
{
    /// <summary>フィールド：歩き・出入口・草むらの遭遇・トレーナー・回復所・全滅。</summary>
    public sealed class FieldTests
    {
        private static World FieldWorld(int seed = 1)
        {
            var w = World.New(Lab.Data, seed);
            w.ChooseStarter(1);
            return w;
        }

        private static FieldStep Walk(World w, Direction dir)
        {
            var result = w.FieldTick(dir);
            if (result == FieldStep.Blocked) { return result; }
            var last = result;
            while (w.Explorer.IsMoving && w.Mode == WorldMode.Field) { last = w.FieldTick(null); }
            return last;
        }

        [Test]
        public void StarterPickComesFirst_AndOnlyOnce()
        {
            var w = World.New(Lab.Data, 1);
            Assert.That(w.Mode, Is.EqualTo(WorldMode.StarterPick));
            Assert.That(w.FieldTick(Direction.Up), Is.EqualTo(FieldStep.None), "選ぶ前は歩けない");
            Assert.That(w.ChooseStarter(3), Is.False);
            Assert.That(w.ChooseStarter(2), Is.True);
            Assert.That(w.ChooseStarter(0), Is.False, "2 回は選べない");
            Assert.That(w.Roster.Party.Count, Is.EqualTo(1));
            Assert.That(w.Roster.Party[0].Species.Id, Is.EqualTo("mossling"));
        }

        [Test]
        public void WalkingTakesStepTicks_AndWallsBlock()
        {
            var w = FieldWorld();
            var x = w.Explorer.X;
            w.FieldTick(Direction.Left);
            Assert.That(w.Explorer.X, Is.EqualTo(x - 1));
            for (int i = 0; i < Lab.Data.Rules.StepTicks - 1; i++)
            {
                Assert.That(w.Explorer.IsMoving, Is.True);
                w.FieldTick(null);
            }

            w.FieldTick(null);
            Assert.That(w.Explorer.IsMoving, Is.False);
            w.WarpForTest("town", 1, 1, Direction.Up);
            Assert.That(w.FieldTick(Direction.Up), Is.EqualTo(FieldStep.Blocked));
            Assert.That(w.Explorer.Y, Is.EqualTo(1));
        }

        [Test]
        public void PortalMovesToTheRoute()
        {
            var w = FieldWorld();
            w.WarpForTest("town", 18, 6, Direction.Right);
            Assert.That(Walk(w, Direction.Right), Is.EqualTo(FieldStep.Portal));
            Assert.That(w.Explorer.MapId, Is.EqualTo("route"));
            Assert.That((w.Explorer.X, w.Explorer.Y), Is.EqualTo((1, 7)));
        }

        [Test]
        public void NoEncounterOffTheGrass_AndNotDuringTheGraceSteps()
        {
            var w = FieldWorld();
            w.WarpForTest("route", 1, 7, Direction.Right);
            for (int i = 0; i < 9; i++) { Assert.That(Walk(w, i % 2 == 0 ? Direction.Right : Direction.Left), Is.Not.EqualTo(FieldStep.Encounter), "道では遭遇しない"); }

            for (int seed = 1; seed <= 30; seed++)
            {
                var g = FieldWorld(seed);
                g.WarpForTest("route", 3, 2, Direction.Right);
                var steps = 0;
                while (g.Mode == WorldMode.Field && steps < 500)
                {
                    steps++;
                    Walk(g, steps % 2 == 0 ? Direction.Right : Direction.Left);
                }

                Assert.That(g.Mode, Is.EqualTo(WorldMode.Battle), $"シード {seed}：草むらではいつか遭遇する");
                Assert.That(steps, Is.GreaterThan(Lab.Data.Rules.GraceSteps), "猶予の歩数の間は遭遇しない");
                var foe = g.Battle!.FoeMonster;
                Assert.That(foe.Level, Is.InRange(2, 4));
                Assert.That(new List<string> { "wickmoth", "burrowroot", "brinecrab" }, Has.Member(foe.Species.Id));
                Assert.That(g.Seen & (1 << foe.Species.Index), Is.Not.Zero, "出会った種族は図鑑に見た印");
            }
        }

        [Test]
        public void TrainerBlocksTheRoad_UntilDefeated_AndIsNotFoughtAgain()
        {
            var w = FieldWorld();
            w.WarpForTest("route", 11, 7, Direction.Right);
            Assert.That(w.FieldTick(Direction.Right), Is.EqualTo(FieldStep.Blocked));
            Assert.That(w.Interact(out var trainer), Is.EqualTo(InteractKind.Trainer));
            Assert.That(trainer!.Id, Is.EqualTo("saki"));
            Assert.That(w.Mode, Is.EqualTo(WorldMode.Battle));
            var battle = w.Battle!;
            foreach (var m in battle.Foes) { m.Hp = 0; }
            battle.Foes[0].Hp = 1;
            battle.PlayerMonster.Hp = battle.PlayerMonster.MaxHp;
            while (!battle.IsOver) { battle.Submit(BattleCommand.Fight(0)); }
            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Won));
            var after = w.FinishBattle();
            Assert.That(after.Trainer!.Id, Is.EqualTo("saki"));
            Assert.That(w.IsDefeated(trainer), Is.True);
            Assert.That(w.Interact(out _), Is.EqualTo(InteractKind.Nothing), "倒したトレーナーとは戦わない");
            Assert.That(Walk(w, Direction.Right), Is.Not.EqualTo(FieldStep.Blocked), "倒すと通れる");
            Assert.That(w.Explorer.X, Is.EqualTo(12));
        }

        [Test]
        public void CenterHealsAndRefills_OnlyWhenFacingTheCounter()
        {
            var w = FieldWorld();
            w.Roster.Party[0].Hp = 1;
            w.SetItemsForTest(0, 1);
            w.WarpForTest("town", 10, 4, Direction.Down);
            Assert.That(w.AtCenter, Is.False);
            Assert.That(w.Interact(out _), Is.EqualTo(InteractKind.Nothing));
            w.WarpForTest("town", 10, 4, Direction.Up);
            Assert.That(w.AtCenter, Is.True);
            Assert.That(w.Interact(out _), Is.EqualTo(InteractKind.Center));
            Assert.That(w.Roster.Party[0].Hp, Is.EqualTo(w.Roster.Party[0].MaxHp));
            Assert.That((w.Balls, w.Potions), Is.EqualTo((Lab.Data.Rules.Balls, Lab.Data.Rules.Potions)));
        }

        [Test]
        public void LosingReturnsToTheCenterHealed_KeepingProgress()
        {
            var w = FieldWorld();
            w.DefeatTrainerForTest(0);
            w.WarpForTest("route", 20, 2, Direction.Right);
            w.StartWildForTest(Lab.Mon("cinderpup", 20, "blaze"));
            var battle = w.Battle!;
            battle.PlayerMonster.Hp = 1;
            while (!battle.IsOver) { battle.Submit(BattleCommand.Fight(0)); }
            Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Lost));
            var balls = battle.Balls;
            w.FinishBattle();
            Assert.That(w.Mode, Is.EqualTo(WorldMode.Field));
            Assert.That(w.AtCenter, Is.True);
            Assert.That(w.Roster.Party[0].Hp, Is.EqualTo(w.Roster.Party[0].MaxHp));
            Assert.That(w.TrainersDefeated, Is.EqualTo(1), "倒したトレーナーは残る");
            Assert.That(w.Balls, Is.EqualTo(balls));
            Assert.That(w.Losses, Is.EqualTo(1));
        }

        [Test]
        public void BeatingTheThirdTrainerClears()
        {
            var w = FieldWorld();
            w.DefeatTrainerForTest(0);
            w.DefeatTrainerForTest(1);
            w.WarpForTest("route", 39, 7, Direction.Right);
            Assert.That(w.Interact(out var t), Is.EqualTo(InteractKind.Trainer));
            Assert.That(t!.Id, Is.EqualTo("touma"));
            foreach (var m in w.Battle!.Foes) { m.Hp = 1; }
            w.Battle.Party[0].GainExp(Lab.Data.Rules, 100000);
            w.Battle.Party[0].Hp = w.Battle.Party[0].MaxHp;
            while (!w.Battle.IsOver) { w.Battle.Submit(BattleCommand.Fight(0)); }
            var after = w.FinishBattle();
            Assert.That(after.Cleared, Is.True);
            Assert.That(w.Mode, Is.EqualTo(WorldMode.Cleared));
            Assert.That(w.FieldTick(Direction.Left), Is.EqualTo(FieldStep.None), "クリア後は動かない");
            var ticks = w.PlayTicks;
            w.AddPlayTicks(100);
            Assert.That(w.PlayTicks, Is.EqualTo(ticks), "クリア後は遊んだ時間を数えない");
        }

        [Test]
        public void PendingMovesAreResolvedAfterTheBattle()
        {
            var w = FieldWorld();
            var full = Lab.Mon("cinderpup", 12, "tackle", "growl", "spark", "smolder");
            full.GainExp(Lab.Data.Rules, Formula.ExpForLevel(Lab.Data.Rules, 13) - full.Exp - 1);
            w.Roster.LoadUnchecked(new[] { full }, System.Array.Empty<Monster>());
            w.StartWildForTest(Lab.Mon("wickmoth", 2, "spark"));
            while (!w.Battle!.IsOver) { w.Battle.Submit(BattleCommand.Fight(2)); }
            w.FinishBattle();
            Assert.That(w.Mode, Is.EqualTo(WorldMode.LearnMove));
            Assert.That(w.ResolveLearn(4), Is.False, "ない枠は選べない");
            Assert.That(w.ResolveLearn(1), Is.True);
            Assert.That(w.Roster.Party[0].Moves[1].Move.Id, Is.EqualTo("cinderfang"));
            Assert.That(w.Mode, Is.EqualTo(WorldMode.Field));
            Assert.That(w.ResolveLearn(-1), Is.False);
        }
    }
}
