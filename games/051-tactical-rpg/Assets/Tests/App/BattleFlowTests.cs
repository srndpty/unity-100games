using Game.Core;
using NUnit.Framework;

namespace Game.App.Tests
{
    public sealed class BattleFlowTests
    {
        [Test]
        public void EnemyActionsCannotRunDuringAllyTurnAndFocusClickIsDiscarded()
        {
            var flow = new BattleFlow(1, Battle.Defaults());
            Assert.That(flow.Help(), Is.True);
            Assert.That(flow.Start(), Is.True);
            Assert.That(flow.StepEnemy(), Is.False);
            flow.SetFocus(false);
            flow.SetFocus(true);
            Assert.That(flow.ClickCell(49), Is.False);
            Assert.That(flow.EndTurn(), Is.False);
            flow.PointerIdle();
            Assert.That(flow.ClickCell(49), Is.True);
            Assert.That(flow.EndTurn(), Is.True);
            Assert.That(flow.ClickCell(49), Is.False);
            Assert.That(flow.Advance(0.1, 0.5), Is.False);
            Assert.That(flow.Advance(0.4, 0.5), Is.True);
            for (var i = 0; i < 5; i++) { Assert.That(flow.StepEnemy(), Is.True); }
            Assert.That(flow.Battle.Turn, Is.EqualTo(Side.Ally));
            Assert.That(flow.Battle.Round, Is.EqualTo(2));
            Assert.That(flow.Title(), Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ProductionMapReachesAResultAndCanRestart(bool attack)
        {
            var flow = new BattleFlow(attack ? 1U : 2U, Battle.Defaults());
            flow.Start();
            while (flow.Screen == Screen.Battle && flow.Battle.Round <= 40)
            {
                if (attack)
                {
                    for (var id = 0; id < Battle.AllyCount; id++) { BattleDemo.Act(flow, id); }
                }

                flow.EndTurn();
                while (flow.StepEnemy()) { }
            }

            Assert.That(flow.Battle.Result, Is.EqualTo(attack ? Result.Won : Result.Lost));
            Assert.That(flow.EndTurn(), Is.False);
            Assert.That(flow.Start(), Is.True);
            Assert.That(flow.Battle.Round, Is.EqualTo(1));
            Assert.That(flow.Battle.Result, Is.EqualTo(Result.Playing));
        }

        [TestCase(AttackPriority.Damage, true)]
        [TestCase(AttackPriority.Defeat, true)]
        [TestCase(AttackPriority.Objective, true)]
        [TestCase(AttackPriority.Objective, false)]
        public void SamplesBattleBalance(AttackPriority priority, bool active)
        {
            const int samples = 64;
            var wins = 0;
            var losses = 0;
            var rounds = 0;
            var survivors = 0;
            for (var sample = 1; sample <= samples; sample++)
            {
                var flow = new BattleFlow((uint)sample * 2654435761U, Battle.Defaults());
                flow.Start();
                while (flow.Screen == Screen.Battle && flow.Battle.Round <= 40)
                {
                    if (active)
                    {
                        for (var id = 0; id < Battle.AllyCount; id++) { BattleDemo.Act(flow, id, priority); }
                    }

                    flow.EndTurn();
                    while (flow.StepEnemy()) { }
                }

                if (flow.Battle.Result == Result.Won) { wins++; }
                if (flow.Battle.Result == Result.Lost) { losses++; }
                rounds += flow.Battle.Result == Result.Playing ? 40 : flow.Battle.Round;
                for (var id = 0; id < Battle.AllyCount; id++)
                {
                    if (flow.Battle.Units[id].Hp > 0) { survivors++; }
                }
            }

            TestContext.WriteLine($"{priority}/{active}: 勝利{wins} 敗北{losses} 未決着{samples - wins - losses} ラウンド合計{rounds} 生存合計{survivors}");
            if (!active) { Assert.That(losses, Is.EqualTo(samples)); }
            else { Assert.That(wins, Is.GreaterThan(0)); }
        }

    }
}
